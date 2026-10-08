using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Services;

namespace Miharikun.ViewModels;

public sealed partial class TimelineItemViewModel : ObservableObject
{
    // 長い本文は、一覧が極端に長くならないよう先頭だけ見せ、クリックで全文を開閉する。
    public TimelineItem Item { get; }
    public TimelineKind Kind => Item.Kind;
    public string TimeText { get; }

    /// <summary>折りたたんだときの本文（短ければ全文と同じ）。</summary>
    public string PreviewText { get; }

    /// <summary>全文を見せるには開く必要があるか。</summary>
    public bool IsTruncated { get; }

    [ObservableProperty] private bool _isHighlighted;

    /// <summary>全文を開いているか。</summary>
    [ObservableProperty] private bool _isExpanded;

    public RelayCommand ToggleExpandedCommand { get; }

    /// <summary>ダブルクリックでコピーした直後（約1.5秒）。バブルの文言を「コピーしました」にする。戻すのは <see cref="TimelineViewModel"/>（タイマーは 1 つ）。</summary>
    [ObservableProperty] private bool _justCopied;

    public AsyncRelayCommand CopyCommand { get; }

    private readonly Func<TimelineItemViewModel, Task> _copy;

    /// <param name="copy">クリップボードへ入れて「コピーしました」を出す処理（<see cref="TimelineViewModel"/> が持つ）。</param>
    public TimelineItemViewModel(TimelineItem item, DateTimeOffset now, Func<TimelineItemViewModel, Task> copy)
    {
        Item = item;
        _copy = copy;
        TimeText = item.HasTime ? SessionText.Clock(item.At, now) : "";
        (PreviewText, IsTruncated) = TextPreview.Make(item.Text);
        ToggleExpandedCommand = new RelayCommand(() => { if (IsTruncated) IsExpanded = !IsExpanded; });
        CopyCommand = new AsyncRelayCommand(CopyAsync);
    }

    /// <summary>全文（省略表示の分も含む）をコピーする。ダブルクリックの1回目のクリックで全文の開閉が働いているので、元に戻す。</summary>
    private async Task CopyAsync()
    {
        if (IsTruncated)
            IsExpanded = !IsExpanded;
        await _copy(this);
    }

    /// <summary>画面に出す本文：開いていれば全文、閉じていれば先頭だけ。</summary>
    public string DisplayText => IsExpanded ? Item.Text : PreviewText;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
}

/// <summary>右ペインのタイムライン。表示するセッションの全イベントを持ち、種別フィルタは表示側で絞る。</summary>
public sealed partial class TimelineViewModel : ObservableObject, IDisposable
{
    /// <summary>これを超える差し替えは、1件ずつ通知せずコレクションごと作り直す。</summary>
    private const int BulkThreshold = 30;

    private readonly IUiServices _services;
    private readonly IUiTimer _copiedTimer;
    private readonly IUiTimer _copiedAllTimer;
    private TimelineItemViewModel? _copiedItem;
    private TimelineItemViewModel? _highlighted;

    /// <summary>セッションの全行（種別・検索で絞る前）。</summary>
    private List<TimelineItemViewModel> _all = [];

    /// <summary>
    /// 種別・検索で絞った行（画面に出す）。末尾の追記は <c>Add</c>、それ以外（種別・検索の変更・セッションの切り替え・30 件超の差し替え）は
    /// 入れ物ごと差し替える（通知 1 回。1 件ずつ通知すると、5,000 行で数千回になる。計画 7.3）。
    /// </summary>
    [ObservableProperty] private ObservableCollection<TimelineItemViewModel> _visibleItems = [];

    // 既定は入力・返事・圧縮が ON（要件 12.4）。
    [ObservableProperty] private bool _showInput = true;
    [ObservableProperty] private bool _showResponse = true;
    [ObservableProperty] private bool _showThought;
    [ObservableProperty] private bool _showTool;
    [ObservableProperty] private bool _showCompaction = true;

    /// <summary>検索語（要件 12.4）。含む行だけを表示する。種別のチップとは AND。</summary>
    [ObservableProperty] private string _searchText = "";

    /// <summary>検索中だけ「3/48件」。分母は種別で絞った後の件数。</summary>
    [ObservableProperty] private string _countText = "";

    /// <summary>ジャンプ先の行を見える位置までスクロールしてほしいときに発生する。</summary>
    public event Action<TimelineItemViewModel>? ScrollRequested;

    public TimelineViewModel(IUiServices services)
    {
        _services = services;
        _copiedTimer = services.CreateTimer(TimeSpan.FromSeconds(1.5), () =>
        {
            _copiedTimer!.Stop();
            if (_copiedItem is not null)
                _copiedItem.JustCopied = false;
            _copiedItem = null;
        });
        _copiedAllTimer = services.CreateTimer(TimeSpan.FromSeconds(1.5), () =>
        {
            _copiedAllTimer!.Stop();
            CopiedAll = false;
        });
    }

    private bool Matches(TimelineItemViewModel i) => IsShown(i.Kind) && TimelineText.Matches(i.Item, SearchText);

    partial void OnShowInputChanged(bool value) => RefreshView();
    partial void OnShowResponseChanged(bool value) => RefreshView();
    partial void OnShowThoughtChanged(bool value) => RefreshView();
    partial void OnShowToolChanged(bool value) => RefreshView();
    partial void OnShowCompactionChanged(bool value) => RefreshView();
    partial void OnSearchTextChanged(string value) => RefreshView();

    /// <summary>種別・検索が変わった。入れ物ごと差し替える。</summary>
    private void RefreshView()
    {
        VisibleItems = new ObservableCollection<TimelineItemViewModel>(_all.Where(Matches));
        UpdateCount();
    }

    private void UpdateCount()
    {
        var shown = VisibleItems.Count;
        var total = _all.Count(i => IsShown(i.Kind));
        CountText = TimelineText.CountText(shown, total, SearchText);
    }

    /// <summary>いま表示している行（種別・検索で絞った結果）を、「時刻　種別：本文」の形で連結した文字列。</summary>
    public string CopyAllText() => TimelineText.CopyAll(VisibleItems.Select(i => i.Item));

    /// <summary>「全部コピー」の直後（約1.5秒）。ボタンの文言を「コピーしました」にする。</summary>
    [ObservableProperty] private bool _copiedAll;

    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (VisibleItems.Count == 0 || !await _services.SetClipboardTextAsync(CopyAllText()))
            return;
        CopiedAll = true;
        _copiedAllTimer.Stop();
        _copiedAllTimer.Start();
    }

    /// <summary>
    /// 行のコピー（行の VM から呼ばれる）。入れたら「コピーしました」を出す。タイマーは 1 つで、
    /// 別の行をコピーしたら、前の行の表示はすぐ戻す（計画 7.4）。
    /// </summary>
    private async Task CopyItemAsync(TimelineItemViewModel item)
    {
        if (!await _services.SetClipboardTextAsync(item.Item.Text))
            return;
        if (_copiedItem is not null && !ReferenceEquals(_copiedItem, item))
            _copiedItem.JustCopied = false;
        item.JustCopied = true;
        _copiedItem = item;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private TimelineItemViewModel NewRow(TimelineItem item, DateTimeOffset now) => new(item, now, CopyItemAsync);

    private bool IsShown(TimelineKind kind) => kind switch
    {
        TimelineKind.Input => ShowInput,
        TimelineKind.Response => ShowResponse,
        TimelineKind.Thought => ShowThought,
        TimelineKind.Tool => ShowTool,
        TimelineKind.Compaction => ShowCompaction,
        _ => true,   // セッション開始・終了は常に表示
    };

    public void Clear() => Replace([]);

    private void Replace(List<TimelineItemViewModel> items)
    {
        if (_highlighted is not null && !items.Contains(_highlighted))
            _highlighted = null;
        _all = items;
        VisibleItems = new ObservableCollection<TimelineItemViewModel>(items.Where(Matches));
        UpdateCount();
    }

    /// <summary>
    /// イベント全体からタイムラインを作り直す。変わった所（通常は末尾）だけ差し替えるので、スクロール位置が飛ばない。
    /// </summary>
    public void SetEvents(IReadOnlyList<AgentEvent> events, DateTimeOffset now)
    {
        var built = TimelineBuilder.Build(events);

        var common = 0;
        while (common < _all.Count && common < built.Count && _all[common].Item == built[common])
            common++;

        var removed = _all.Count - common;
        var added = built.Count - common;
        if (removed + added == 0)
            return;

        if (removed + added > BulkThreshold)
        {
            var all = _all.Take(common).ToList();
            all.AddRange(built.Skip(common).Select(i => NewRow(i, now)));
            Replace(all);
            return;
        }

        while (_all.Count > common)
        {
            var last = _all[^1];
            if (ReferenceEquals(last, _highlighted)) _highlighted = null;
            if (VisibleItems.Count > 0 && ReferenceEquals(VisibleItems[^1], last))
                VisibleItems.RemoveAt(VisibleItems.Count - 1);   // 表示中の行は、並びが同じなので末尾にある
            _all.RemoveAt(_all.Count - 1);
        }
        for (var i = common; i < built.Count; i++)
        {
            var row = NewRow(built[i], now);
            _all.Add(row);
            if (Matches(row))
                VisibleItems.Add(row);
        }
        UpdateCount();
    }

    /// <summary>表示中の最後の行までスクロールする（セッションを開いた直後に最新から見せる）。</summary>
    public void ScrollToEnd()
    {
        var last = _all.LastOrDefault(i => IsShown(i.Kind));
        if (last is not null)
            ScrollRequested?.Invoke(last);
    }

    /// <summary>該当イベントの行を強調してスクロールする。種別フィルタが OFF なら ON に切り替える。</summary>
    public bool JumpTo(long seq, TimelineKind kind)
    {
        // 番号と種類が両方一致する行を優先する（Claude Code は 1 行のログから複数のイベントが出て、同じ番号になることがある）
        var found = TimelineBuilder.Find(_all.Select(i => i.Item).ToList(), seq, kind);
        var item = found is null ? null : _all.FirstOrDefault(i => ReferenceEquals(i.Item, found));
        if (item is null)
            return false;

        EnableFilter(kind);
        if (!TimelineText.Matches(item.Item, SearchText))
            SearchText = "";   // 検索で隠れている行へは、検索を解除して飛ぶ

        if (_highlighted is not null) _highlighted.IsHighlighted = false;
        item.IsHighlighted = true;
        _highlighted = item;
        ScrollRequested?.Invoke(item);
        return true;
    }

    private void EnableFilter(TimelineKind kind)
    {
        switch (kind)
        {
            case TimelineKind.Input: ShowInput = true; break;
            case TimelineKind.Response: ShowResponse = true; break;
            case TimelineKind.Thought: ShowThought = true; break;
            case TimelineKind.Tool: ShowTool = true; break;
            case TimelineKind.Compaction: ShowCompaction = true; break;
        }
    }

    public void Dispose()
    {
        _copiedTimer.Dispose();
        _copiedAllTimer.Dispose();
    }
}
