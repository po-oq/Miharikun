using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;

namespace Miharikun.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SessionMonitor _monitor;
    private readonly SynchronizationContext _ui;
    private readonly SessionMetaService _meta;
    private readonly Func<SessionKey, IReadOnlyList<AgentEvent>> _getEvents;
    private readonly Dictionary<SessionKey, SessionCardViewModel> _byKey = [];

    // git（要件 10章：コミット・未コミットは App が実行する）
    private const int GitRefreshEveryTicks = 5;
    private static readonly TimeSpan GitMinInterval = TimeSpan.FromSeconds(2);
    private readonly GitClient _git;
    private GitStatus? _gitStatus;
    private bool _gitBusy;
    private DateTimeOffset _lastGitRefresh = DateTimeOffset.MinValue;
    private int _ticks;
    private (string? From, string? To) _commitRange;

    public string ProjectFolder { get; }

    public ObservableCollection<SessionCardViewModel> Cards { get; } = [];

    /// <summary>検索・フィルタ済みで、最後の動きの新しい順に並んだカード。</summary>
    public ICollectionView CardsView { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _runningOnly;
    [ObservableProperty] private bool _uncommittedOnly;
    [ObservableProperty] private bool _memoOnly;
    [ObservableProperty] private StatusTab _statusTab = StatusTab.All;

    /// <summary>タイムラインの拡大モード（要件 12.4.1）。左・中央のペインを隠し、右ペインを広げる。保存しない。</summary>
    [ObservableProperty] private bool _isTimelineExpanded;

    /// <summary>拡大中だけ見出しに出す「— セッション名（状態）」。</summary>
    public string ExpandedTitle => IsTimelineExpanded && Detail is { } d ? $"— {d.Title}（{d.StateText}）" : "";

    partial void OnIsTimelineExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandedTitle));

    /// <summary>拡大⇄戻す。セッションを選んでいないときは拡大しない。</summary>
    [RelayCommand]
    private void ToggleTimelineExpanded() => IsTimelineExpanded = !IsTimelineExpanded && Selected is not null;
    [ObservableProperty] private SessionCardViewModel? _selected;
    [ObservableProperty] private SessionDetailViewModel? _detail;

    public TimelineViewModel Timeline { get; } = new();
    [ObservableProperty] private IReadOnlyList<StateCount> _counts = [];

    /// <summary>ステータス絞り込みタブ（全て / 未設定 / 作業中 / 中断 / 完了）。件数は中身だけ更新し、入れ替えない（選択が外れないように）。</summary>
    public IReadOnlyList<StatusTabItem> StatusTabs { get; } =
        Enum.GetValues<StatusTab>().Select(t => new StatusTabItem(t, StatusFilter.Name(t))).ToList();

    // 右ペイン下部：このプロジェクトの全セッション横断の「最近の入力」「最近閉じたセッション」（行クリックで左のカードを選択）
    [ObservableProperty] private IReadOnlyList<RecentRow> _recentInputs = [];
    [ObservableProperty] private IReadOnlyList<RecentRow> _recentClosed = [];

    public RelayCommand<RecentRow> SelectRecentCommand { get; }

    /// <summary>選んだカードが見える位置までスクロールしてほしいときに発生する。</summary>
    public event Action<SessionCardViewModel>? CardScrollRequested;

    public bool UncommittedFilterAvailable => true;
    public bool MemoFilterAvailable => true;

    public MainViewModel(string projectFolder, SessionMonitor monitor, SynchronizationContext ui,
        Func<SessionKey, IReadOnlyList<AgentEvent>> getEvents, SessionMetaService meta, GitClient git)
    {
        _git = git;
        _meta = meta;
        _meta.Changed += OnMetaChanged;
        ProjectFolder = projectFolder;
        _monitor = monitor;
        _ui = ui;
        _getEvents = getEvents;

        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.SortDescriptions.Add(new SortDescription(nameof(SessionCardViewModel.LastActivityAt), ListSortDirection.Descending));
        CardsView.Filter = o => o is SessionCardViewModel c && Visible(c);

        SelectRecentCommand = new RelayCommand<RecentRow>(SelectRecent);

        UpdateCounts();
        _monitor.Updated += OnUpdated;
    }

    /// <summary>
    /// 右ペイン下部の行クリック。左のカードを選び、入力の行ならタイムラインの該当の依頼も選ぶ（同じセッションに連続して入力していても、どの行か分かる）。
    /// </summary>
    private void SelectRecent(RecentRow? row)
    {
        if (row is null)
            return;
        SelectSession(row.Key);
        if (row.Seq is { } seq && ReferenceEquals(Selected, _byKey.GetValueOrDefault(row.Key)))
            Timeline.JumpTo(seq, TimelineKind.Input);
    }

    /// <summary>
    /// 行クリックで左のカードを選ぶ。検索やフィルタで隠れているカードのときは、フィルタを解除して見えるようにする。
    /// </summary>
    private void SelectSession(SessionKey? key)
    {
        if (key is null || !_byKey.TryGetValue(key, out var card))
            return;

        if (!Visible(card))
        {
            SearchText = "";
            RunningOnly = false;
            UncommittedOnly = false;
            MemoOnly = false;
            StatusTab = StatusTab.All;
        }
        Selected = card;
        CardScrollRequested?.Invoke(card);
    }

    private void UpdateRecent(DateTimeOffset now)
    {
        var summaries = Cards.Select(c => c.Snapshot.Summary).ToList();

        var inputs = RecentActivity.Inputs(summaries)
            .Select(i => new RecentRow(i.Key, FirstLine(i.Text), SessionText.RelativeTime(i.At, now), i.Seq)).ToList();
        var closed = RecentActivity.Closed(summaries)
            .Select(c => new RecentRow(c.Key, _byKey.TryGetValue(c.Key, out var card) ? card.Title : c.Key.SessionId,
                SessionText.RelativeTime(c.ClosedAt, now))).ToList();

        // 毎秒の更新で中身が同じなら差し替えない（クリック中に行が作り直されるのを避ける）
        if (!inputs.SequenceEqual(RecentInputs)) RecentInputs = inputs;
        if (!closed.SequenceEqual(RecentClosed)) RecentClosed = closed;
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    // 監視スレッドから呼ばれる。UI スレッドへ渡すだけにする。
    private void OnUpdated(SessionUpdate update) => _ui.Post(_ => Apply(update), null);

    public void Apply(SessionUpdate update)
    {
        var now = DateTimeOffset.Now;
        foreach (var snap in update.Upserts)
        {
            if (_byKey.TryGetValue(snap.Summary.Key, out var card))
            {
                card.Apply(snap, now);
                card.SetUncommitted(UncommittedCount(snap.Summary));
                if (ReferenceEquals(card, Selected))
                    RefreshSelected(snap, now);
            }
            else
            {
                card = new SessionCardViewModel(snap, now, _meta);
                card.SetUncommitted(UncommittedCount(snap.Summary));
                _byKey[card.Key] = card;
                Cards.Add(card);
            }
        }
        foreach (var key in update.Removed)
        {
            if (_byKey.Remove(key, out var card))
            {
                Cards.Remove(card);
                if (ReferenceEquals(Selected, card))
                    Selected = null;
            }
        }
        CardsView.Refresh();
        UpdateCounts();
        UpdateRecent(now);

        // セッションが動いた直後はファイルが変わっている可能性が高いので、少し間引いて git を見直す。
        if (update.Upserts.Count > 0 && now - _lastGitRefresh > GitMinInterval)
            RefreshGit();
    }

    private void RefreshSelected(SessionSnapshot snap, DateTimeOffset now)
    {
        if (Detail is null || Detail.Key != snap.Summary.Key)
            Detail = new SessionDetailViewModel(snap, now, t => Timeline.JumpTo(t.Seq, t.Kind), _meta, ProjectFolder);
        else
            Detail.Update(snap, now);
        Timeline.SetEvents(_getEvents(snap.Summary.Key), now);
        Detail.SetUncommitted(UncommittedFiles(snap.Summary));
        LoadCommits(snap.Summary);
    }

    partial void OnSelectedChanged(SessionCardViewModel? value)
    {
        Detail?.FlushMemo();   // 切り替える前に、入力途中のメモを保存する
        Timeline.Clear();
        Timeline.SearchText = "";   // 検索語は、別のセッションに切り替えたらクリア（拡大⇄戻すでは保持）
        if (value is null)
            IsTimelineExpanded = false;
        Detail = null;
        _commitRange = default;
        if (value is null)
            return;

        RefreshSelected(value.Snapshot, DateTimeOffset.Now);
        Timeline.ScrollToEnd();
    }

    /// <summary>時刻表示の更新用。1秒ごとに呼ぶ。</summary>
    public void Tick()
    {
        var now = DateTimeOffset.Now;
        foreach (var card in Cards)
            card.RefreshClock(now);
        Detail?.RefreshClock(now);
        if (IsTimelineExpanded)
            OnPropertyChanged(nameof(ExpandedTitle));   // 名前や状態が変わったとき
        UpdateRecent(now);

        // 利用者が IDE や別のターミナルでコミットすることもあるので、定期的にも見直す。
        if (++_ticks % GitRefreshEveryTicks == 0)
            RefreshGit();
    }

    private IReadOnlyList<string>? UncommittedFiles(SessionSummary s) =>
        Uncommitted.Files(_gitStatus, s.ChangedFiles, ProjectFolder);

    private int? UncommittedCount(SessionSummary s) => UncommittedFiles(s)?.Count;

    /// <summary>git status を背景で取り直し、全カードと詳細の未コミットを更新する。実行中なら何もしない。</summary>
    public async void RefreshGit()
    {
        if (_gitBusy)
            return;
        _gitBusy = true;
        try
        {
            _gitStatus = await Task.Run(_git.GetStatus);
            _lastGitRefresh = DateTimeOffset.Now;

            foreach (var card in Cards)
                card.SetUncommitted(UncommittedCount(card.Snapshot.Summary));
            if (Selected is not null)
                Detail?.SetUncommitted(UncommittedFiles(Selected.Snapshot.Summary));
            CardsView.Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Write("git の更新に失敗: " + ex.Message);
        }
        finally
        {
            _gitBusy = false;
        }
    }

    /// <summary>選択中セッションのコミット一覧。開始・最新の HEAD が変わったときだけ git log を実行する。</summary>
    private async void LoadCommits(SessionSummary s)
    {
        var range = (s.StartHead, s.LatestHead);
        if (range == _commitRange)
            return;
        _commitRange = range;

        try
        {
            var commits = await Task.Run(() => _git.GetCommits(range.StartHead, range.LatestHead));
            // 待っている間に選択や範囲が変わっていたら捨てる
            if (Detail is not null && Detail.Key == s.Key && _commitRange == range)
                Detail.SetCommits(commits);
        }
        catch (Exception ex)
        {
            AppLog.Write("コミット一覧の取得に失敗: " + ex.Message);
        }
    }

    partial void OnSearchTextChanged(string value) => CardsView.Refresh();
    partial void OnRunningOnlyChanged(bool value) => CardsView.Refresh();
    partial void OnUncommittedOnlyChanged(bool value) => CardsView.Refresh();
    partial void OnMemoOnlyChanged(bool value) => CardsView.Refresh();
    partial void OnStatusTabChanged(StatusTab value) => CardsView.Refresh();

    private bool Visible(SessionCardViewModel c)
    {
        if (RunningOnly && c.State != SessionState.Running) return false;
        if (MemoOnly && !c.HasMemo) return false;
        if (!StatusFilter.Matches(StatusTab, c.Status)) return false;
        if (UncommittedOnly && !(c.UncommittedCount > 0)) return false;
        return SessionSearch.Matches(c.SearchText, SearchText);
    }

    private void UpdateCounts()
    {
        var statuses = Cards.Select(c => c.Status).ToList();
        foreach (var tab in StatusTabs)
            tab.Count = StatusFilter.Count(tab.Tab, statuses);

        int Count(params SessionState[] states) => Cards.Count(c => states.Contains(c.State));
        Counts =
        [
            new(SessionState.Running, SessionText.StateName(SessionState.Running), Count(SessionState.Running)),
            new(SessionState.YourTurn, SessionText.StateName(SessionState.YourTurn), Count(SessionState.YourTurn)),
            new(SessionState.Aborted, SessionText.StateName(SessionState.Aborted), Count(SessionState.Aborted)),
            new(SessionState.Error, SessionText.StateName(SessionState.Error), Count(SessionState.Error)),
            new(SessionState.Closed, SessionText.StateName(SessionState.Closed), Count(SessionState.Closed, SessionState.Imported)),
        ];
    }

    // タイトル・概要・メモが変わったら、カード・詳細・検索結果に反映する。
    private void OnMetaChanged(SessionKey key)
    {
        if (_byKey.TryGetValue(key, out var card))
            card.RefreshMeta();
        if (Detail is not null && Detail.Key == key)
            Detail.RefreshMeta();
        CardsView.Refresh();
        UpdateCounts();   // ステータスのタブの件数が変わるため
        UpdateRecent(DateTimeOffset.Now);   // 閉じたセッションの表示名が変わるため
    }

    /// <summary>入力途中のメモを保存する（ウィンドウを閉じる前など）。</summary>
    public void Flush() => Detail?.FlushMemo();

    public void Dispose()
    {
        _monitor.Updated -= OnUpdated;
        _meta.Changed -= OnMetaChanged;
    }
}
/// <summary>絞り込みタブ1つ。Count は他のフィルタに関係なく、全カードから数えた件数。</summary>
public sealed partial class StatusTabItem(StatusTab tab, string name) : ObservableObject
{
    public StatusTab Tab { get; } = tab;
    public string Name { get; } = name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private int _count;
    public string Label => $"{Name} {Count}";
}

public sealed record StateCount(SessionState State, string Name, int Count);

/// <summary>Seq があれば入力の行（タイムラインの該当位置）。閉じたセッションの行は null。</summary>
public sealed record RecentRow(SessionKey Key, string Text, string TimeText, long? Seq = null);
