using Miharikun.Core.Install;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Services;

namespace Miharikun.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SessionMonitor _monitor;
    private readonly SynchronizationContext _ui;
    private readonly SessionMetaService _meta;
    private readonly Func<SessionKey, IReadOnlyList<AgentEvent>> _getEvents;
    private readonly Dictionary<SessionKey, SessionCardViewModel> _byKey = [];
    private readonly IUiServices _services;
    private readonly Action<string>? _log;
    private IUiTimer? _clock;

    // git（要件 10章：コミット・未コミットは App が実行する）。テストで本物の git を起動しないよう、差し替えられる形で受け取る
    private const int GitRefreshEveryTicks = 5;
    private static readonly TimeSpan GitMinInterval = TimeSpan.FromSeconds(2);
    private readonly Func<GitStatus?> _getGitStatus;
    private readonly Func<SessionSummary, IReadOnlyList<GitCommit>?> _loadCommits;
    private GitStatus? _gitStatus;
    private bool _gitBusy;
    private readonly IBackgroundRunner _background;
    private DateTimeOffset _lastGitRefresh = DateTimeOffset.MinValue;
    private int _ticks;
    private (string?, string?, string?, DateTimeOffset, DateTimeOffset) _commitKey;

    public string ProjectFolder { get; }

    /// <summary>「実行中」のまま新しい記録が来ないとき「停止」と表示するまでの時間（分）。0 以下で無効。設定画面で変わる（SetRunningTimeout）。</summary>
    public int RunningTimeoutMinutes { get; private set; }

    public ObservableCollection<SessionCardViewModel> Cards { get; } = [];

    /// <summary>
    /// 検索・フィルタ済みで、最後の動きの新しい順に並んだカード。作り直さずに合わせる（<see cref="ViewList.SyncTo"/>。
    /// 選んでいるカードは動かさない：計画 7.3）。
    /// </summary>
    public ObservableCollection<SessionCardViewModel> VisibleCards { get; } = [];

    private bool _syncingCards;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _runningOnly;
    [ObservableProperty] private bool _uncommittedOnly;
    [ObservableProperty] private bool _memoOnly;
    /// <summary>選んでいるステータス（複数可・OR）。空 = 全て。</summary>
    private readonly HashSet<StatusTab> _statusSelection = [];
    private bool _syncingStatusChips;

    /// <summary>エージェントの絞り込み（AgentFilter.All = 全て）。</summary>
    [ObservableProperty] private string _agentKey = AgentFilter.All;

    /// <summary>タイムラインの拡大モード（要件 12.4.1）。左・中央のペインを隠し、右ペインを広げる。保存しない。</summary>
    [ObservableProperty] private bool _isTimelineExpanded;

    /// <summary>拡大中だけ見出しに出す「— セッション名（状態）」。</summary>
    public string ExpandedTitle => IsTimelineExpanded && Detail is { } d ? $"— {d.Title}（{d.StateText}）" : "";

    partial void OnIsTimelineExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandedTitle));

    /// <summary>拡大⇄戻す。セッションを選んでいないときは拡大しない。</summary>
    [RelayCommand]
    private void ToggleTimelineExpanded() => IsTimelineExpanded = !IsTimelineExpanded && Selected is not null;
    private SessionCardViewModel? _selected;

    /// <summary>
    /// 選んでいるカード。一覧を合わせている間（<see cref="RefreshCards"/>）に画面の一覧から来た null は、いったん無視する
    /// （選んでいたカードが一覧に残っていれば、合わせ終わりに選び直す。隠れたときは今と同じく外す。計画 7.3 の保険）。
    /// </summary>
    public SessionCardViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_syncingCards && value is null)
                return;
            if (SetProperty(ref _selected, value))
                HandleSelectedChanged(value);
        }
    }

    [ObservableProperty] private SessionDetailViewModel? _detail;

    public TimelineViewModel Timeline { get; }
    [ObservableProperty] private IReadOnlyList<StateCount> _counts = [];

    /// <summary>「Hook なし」のセッションの数（カード全体から数える。フィルタ・検索に関係なく。どの状態の件数にも入れない。要件 12.11）。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoHook), nameof(HookWarningText))] private int _noHookCount;

    public bool HasNoHook => NoHookCount > 0;

    public string HookWarningText => HookWording.NoHookWarning(NoHookCount);

    private readonly string? _hookErrorLogPath;

    /// <summary>hook-error.log を既定のアプリで開く。ファイルが無ければ押せない。</summary>
    public RelayCommand OpenHookErrorLogCommand { get; }

    /// <summary>ステータス絞り込みのチップ（全て / 未設定 / 作業中 / 中断 / 完了。「全て」以外は複数選べる）。件数は中身だけ更新し、入れ替えない（選択が外れないように）。</summary>
    public IReadOnlyList<StatusTabItem> StatusTabs { get; }

    /// <summary>エージェント絞り込みのチップ（全て / Cursor / Claude Code）。ステータスのタブと同じく、件数は全カードから数え、入れ替えない。</summary>
    public IReadOnlyList<AgentTabItem> AgentTabs { get; } =
        AgentFilter.Options.Select(o => new AgentTabItem(o.Key, o.Name)).ToList();

    // 右ペイン下部：このプロジェクトの全セッション横断の「最近の入力」「最近閉じたセッション」（行クリックで左のカードを選択）
    [ObservableProperty] private IReadOnlyList<RecentRow> _recentInputs = [];
    [ObservableProperty] private IReadOnlyList<RecentRow> _recentClosed = [];

    public RelayCommand<RecentRow> SelectRecentCommand { get; }

    /// <summary>選んだカードが見える位置までスクロールしてほしいときに発生する。</summary>
    public event Action<SessionCardViewModel>? CardScrollRequested;

    public bool UncommittedFilterAvailable => true;
    public bool MemoFilterAvailable => true;

    /// <param name="getGitStatus">git status（背景スレッドで呼ぶ）。</param>
    /// <param name="loadCommits">セッションのコミット一覧（背景スレッドで呼ぶ）。</param>
    public MainViewModel(string projectFolder, SessionMonitor monitor, SynchronizationContext ui,
        Func<SessionKey, IReadOnlyList<AgentEvent>> getEvents, SessionMetaService meta, IUiServices services,
        Func<GitStatus?> getGitStatus, Func<SessionSummary, IReadOnlyList<GitCommit>?> loadCommits,
        int runningTimeoutMinutes = StalledRule.DefaultTimeoutMinutes, string? hookErrorLogPath = null, Action<string>? log = null,
        IBackgroundRunner? background = null)
    {
        _background = background ?? new ThreadPoolRunner();
        RunningTimeoutMinutes = runningTimeoutMinutes;
        _hookErrorLogPath = hookErrorLogPath;
        _services = services;
        _log = log;
        Timeline = new TimelineViewModel(services);
        OpenHookErrorLogCommand = new RelayCommand(
            () => _services.OpenWithDefaultApp(_hookErrorLogPath!),
            () => _hookErrorLogPath is not null && System.IO.File.Exists(_hookErrorLogPath));
        _getGitStatus = getGitStatus;
        _loadCommits = loadCommits;
        _meta = meta;
        _meta.Changed += OnMetaChanged;
        ProjectFolder = projectFolder;
        _monitor = monitor;
        _ui = ui;
        _getEvents = getEvents;

        StatusTabs = Enum.GetValues<StatusTab>().Select(t => new StatusTabItem(t, StatusFilter.Name(t))).ToList();
        foreach (var chip in StatusTabs)
        {
            chip.IsChecked = chip.Tab == StatusTab.All;
            chip.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(StatusTabItem.IsChecked)) OnStatusChipChanged(chip);
            };
        }

        SelectRecentCommand = new RelayCommand<RecentRow>(SelectRecent);

        UpdateCounts();
        _monitor.Updated += OnUpdated;
    }

    /// <summary>1 秒ごとの時計（◯分前・停止の表示・git の見直し）を始める。組み立ての最後に呼ぶ。<see cref="Dispose"/> で止まる。</summary>
    public void StartClock()
    {
        _clock ??= _services.CreateTimer(TimeSpan.FromSeconds(1), Tick);
        _clock.Start();
    }

    /// <summary>
    /// 検索・フィルタに合うカードを、最後の動きの新しい順（同じ時刻は <see cref="Cards"/> に入った順）に並べて、
    /// <see cref="VisibleCards"/> を合わせる。選んでいるカードは動かさない。
    /// </summary>
    private void RefreshCards()
    {
        var desired = Cards.Where(Visible).OrderByDescending(c => c.LastActivityAt).ToList();
        var selected = _selected;
        _syncingCards = true;
        try
        {
            ViewList.SyncTo(VisibleCards, desired, selected);
        }
        finally
        {
            _syncingCards = false;
        }

        // 合わせている間に画面の一覧が外した選択を、残っていれば選び直す。隠れたときは外す（今と同じ）
        if (selected is not null && ReferenceEquals(_selected, selected))
        {
            if (VisibleCards.Contains(selected))
                OnPropertyChanged(nameof(Selected));
            else
                Selected = null;
        }
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
            ClearStatusSelection();
            AgentKey = AgentFilter.All;
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
                card = new SessionCardViewModel(snap, now, _meta, () => RunningTimeoutMinutes);
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
        RefreshCards();
        UpdateCounts();
        UpdateRecent(now);

        // セッションが動いた直後はファイルが変わっている可能性が高いので、少し間引いて git を見直す。
        if (update.Upserts.Count > 0 && now - _lastGitRefresh > GitMinInterval)
            RefreshGit();
    }

    private void RefreshSelected(SessionSnapshot snap, DateTimeOffset now)
    {
        if (Detail is null || Detail.Key != snap.Summary.Key)
            Detail = new SessionDetailViewModel(snap, now, t => Timeline.JumpTo(t.Seq, t.Kind), _meta, ProjectFolder, () => RunningTimeoutMinutes);
        else
            Detail.Update(snap, now);
        Timeline.SetEvents(_getEvents(snap.Summary.Key), now);
        Detail.SetUncommitted(UncommittedFiles(snap.Summary));
        LoadCommits(snap.Summary);
    }

    private void HandleSelectedChanged(SessionCardViewModel? value)
    {
        Detail?.FlushMemo();   // 切り替える前に、入力途中のメモを保存する
        Timeline.Clear();
        Timeline.SearchText = "";   // 検索語は、別のセッションに切り替えたらクリア（拡大⇄戻すでは保持）
        if (value is null)
            IsTimelineExpanded = false;
        Detail = null;
        _commitKey = default;
        if (value is null)
            return;

        RefreshSelected(value.Snapshot, DateTimeOffset.Now);
        Timeline.ScrollToEnd();
    }

    /// <summary>時刻表示の更新用。1秒ごとに呼ぶ。</summary>
    public void Tick()
    {
        var now = DateTimeOffset.Now;
        RefreshClocks(now);
        UpdateRecent(now);
        if (HasNoHook)
            OpenHookErrorLogCommand.NotifyCanExecuteChanged();   // ログのファイルが後からできても押せるように

        // 利用者が IDE や別のターミナルでコミットすることもあるので、定期的にも見直す。
        if (++_ticks % GitRefreshEveryTicks == 0)
            RefreshGit();
    }

    /// <summary>
    /// 時間とともに変わる表示（◯分前・表示用の状態）を更新する。表示用の状態（実行中 → 停止）が変わったカードがあれば、
    /// 絞り込み（「実行中のみ」など）と件数も取り直す（丸・文字・絞り込み・件数が食い違わないように）。
    /// </summary>
    private void RefreshClocks(DateTimeOffset now)
    {
        var stateChanged = false;
        foreach (var card in Cards)
            stateChanged |= card.RefreshClock(now);
        Detail?.RefreshClock(now);
        if (stateChanged)
        {
            RefreshCards();
            UpdateCounts();
        }
        if (IsTimelineExpanded)
            OnPropertyChanged(nameof(ExpandedTitle));   // 名前や状態が変わったとき
    }

    /// <summary>設定画面で「停止とみなす時間」が変わったとき。すぐ効く（再起動は要らない）。</summary>
    public void SetRunningTimeout(int minutes)
    {
        RunningTimeoutMinutes = minutes;
        RefreshClocks(DateTimeOffset.Now);
    }

    private IReadOnlyList<string>? UncommittedFiles(SessionSummary s) =>
        Uncommitted.Files(_gitStatus, s.ChangedFiles, ProjectFolder);

    private int? UncommittedCount(SessionSummary s) => UncommittedFiles(s)?.Count;

    /// <summary>git status を背景で取り直し、全カードと詳細の未コミットを更新する。実行中なら何もしない。時計・イベントから呼ぶ包み（テストは <see cref="RefreshGitAsync"/> を待つ）。</summary>
    public async void RefreshGit() => await RefreshGitAsync();

    public async Task RefreshGitAsync()
    {
        if (_gitBusy)
            return;
        _gitBusy = true;
        try
        {
            _gitStatus = await _background.Run(_getGitStatus);
            _lastGitRefresh = DateTimeOffset.Now;

            foreach (var card in Cards)
                card.SetUncommitted(UncommittedCount(card.Snapshot.Summary));
            if (Selected is not null)
                Detail?.SetUncommitted(UncommittedFiles(Selected.Snapshot.Summary));
            RefreshCards();
        }
        catch (Exception ex)
        {
            _log?.Invoke("git の更新に失敗: " + ex.Message);
        }
        finally
        {
            _gitBusy = false;
        }
    }

    /// <summary>
    /// 選択中セッションのコミット一覧。範囲（開始・最新の HEAD、または head を持たないセッションのブランチと時刻）が変わったときだけ git を実行する。
    /// head を持たない Claude Code は、ブランチと時刻から head を求める（SessionCommits）。
    /// </summary>
    private async void LoadCommits(SessionSummary s) => await LoadCommitsAsync(s);

    public async Task LoadCommitsAsync(SessionSummary s)
    {
        var key = SessionCommits.Key(s);
        if (key == _commitKey)
            return;
        _commitKey = key;

        try
        {
            var commits = await _background.Run(() => _loadCommits(s));
            // 待っている間に選択や範囲が変わっていたら捨てる
            if (Detail is not null && Detail.Key == s.Key && _commitKey == key)
                Detail.SetCommits(commits);
        }
        catch (Exception ex)
        {
            _log?.Invoke("コミット一覧の取得に失敗: " + ex.Message);
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshCards();
    partial void OnRunningOnlyChanged(bool value) => RefreshCards();
    partial void OnUncommittedOnlyChanged(bool value) => RefreshCards();
    partial void OnMemoOnlyChanged(bool value) => RefreshCards();
    private void ClearStatusSelection()
    {
        _statusSelection.Clear();
        SyncStatusChips();
        RefreshCards();
    }

    private void SyncStatusChips()
    {
        _syncingStatusChips = true;
        foreach (var chip in StatusTabs)
            chip.IsChecked = chip.Tab == StatusTab.All ? _statusSelection.Count == 0 : _statusSelection.Contains(chip.Tab);
        _syncingStatusChips = false;
    }

    private void OnStatusChipChanged(StatusTabItem chip)
    {
        if (_syncingStatusChips) return;
        if (chip.Tab == StatusTab.All)
            _statusSelection.Clear();   // 「全て」を押すと他を外す。選択が空のまま外しても「全て」に戻る
        else if (chip.IsChecked)
            _statusSelection.Add(chip.Tab);
        else
            _statusSelection.Remove(chip.Tab);
        SyncStatusChips();
        RefreshCards();
    }
    partial void OnAgentKeyChanged(string value) => RefreshCards();

    private bool Visible(SessionCardViewModel c)
    {
        if (RunningOnly && c.State != SessionState.Running) return false;
        if (MemoOnly && !c.HasMemo) return false;
        if (!StatusFilter.MatchesAny(_statusSelection, c.Status)) return false;
        if (!AgentFilter.Matches(AgentKey, c.AgentId)) return false;
        if (UncommittedOnly && !(c.UncommittedCount > 0)) return false;
        return SessionSearch.Matches(c.SearchText, SearchText);
    }

    private void UpdateCounts()
    {
        var statuses = Cards.Select(c => c.Status).ToList();
        foreach (var tab in StatusTabs)
            tab.Count = StatusFilter.Count(tab.Tab, statuses);
        var agentIds = Cards.Select(c => c.AgentId).ToList();
        foreach (var tab in AgentTabs)
            tab.Count = AgentFilter.Count(tab.Key, agentIds);

        int Count(params SessionState[] states) => Cards.Count(c => states.Contains(c.State));
        var closed = Count(SessionState.Closed, SessionState.Imported);
        // 「閉じた」は、終了の記録を持つエージェント（Cursor）だけの状態。Claude Code のセッションしか無いときは出さない（要件 12.8）。
        var showClosed = closed > 0 || Cards.Count == 0 ||
                         Cards.Any(c => AgentCatalog.Find(c.AgentId)?.Capabilities.HasFlag(AgentCapabilities.SessionEnd) ?? true);
        var counts = new List<StateCount>
        {
            new(SessionState.Running, SessionText.StateName(SessionState.Running), Count(SessionState.Running)),
            new(SessionState.YourTurn, SessionText.StateName(SessionState.YourTurn), Count(SessionState.YourTurn)),
            new(SessionState.Aborted, SessionText.StateName(SessionState.Aborted), Count(SessionState.Aborted)),
            new(SessionState.Error, SessionText.StateName(SessionState.Error), Count(SessionState.Error)),
        };
        if (showClosed)
            counts.Add(new(SessionState.Closed, SessionText.StateName(SessionState.Closed), closed));
        Counts = counts;

        // Hook なしの警告（12.11）。件数はどこにも入れず、帯だけで出す。ログの有無は件数が動いたときに見直す。
        NoHookCount = Count(SessionState.NoHook);
        OpenHookErrorLogCommand.NotifyCanExecuteChanged();
    }

    // タイトル・概要・メモが変わったら、カード・詳細・検索結果に反映する。
    private void OnMetaChanged(SessionKey key)
    {
        if (_byKey.TryGetValue(key, out var card))
            card.RefreshMeta();
        if (Detail is not null && Detail.Key == key)
            Detail.RefreshMeta();
        RefreshCards();
        UpdateCounts();   // ステータスのタブの件数が変わるため
        UpdateRecent(DateTimeOffset.Now);   // 閉じたセッションの表示名が変わるため
    }

    /// <summary>入力途中のメモを保存する（ウィンドウを閉じる前など）。</summary>
    public void Flush() => Detail?.FlushMemo();

    public void Dispose()
    {
        _clock?.Dispose();
        Timeline.Dispose();
        _monitor.Updated -= OnUpdated;
        _meta.Changed -= OnMetaChanged;
    }
}

/// <summary>絞り込みタブ1つ。Count は他のフィルタに関係なく、全カードから数えた件数。</summary>
public sealed partial class StatusTabItem(StatusTab tab, string name) : ObservableObject
{
    [ObservableProperty] private bool _isChecked;
    public StatusTab Tab { get; } = tab;
    public string Name { get; } = name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private int _count;
    public string Label => $"{Name} {Count}";
}

/// <summary>エージェント絞り込みのチップ 1 つ。Count は他のフィルタに関係なく、全カードから数えた件数。</summary>
public sealed partial class AgentTabItem(string key, string name) : ObservableObject
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private int _count;
    public string Label => $"{Name} {Count}";
}

public sealed record StateCount(SessionState State, string Name, int Count);

/// <summary>Seq があれば入力の行（タイムラインの該当位置）。閉じたセッションの行は null。</summary>
public sealed record RecentRow(SessionKey Key, string Text, string TimeText, long? Seq = null);
