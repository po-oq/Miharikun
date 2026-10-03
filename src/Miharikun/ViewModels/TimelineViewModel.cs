using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

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

    /// <summary>ダブルクリックでコピーした直後（約1.5秒）。バブルの文言を「コピーしました」にする。</summary>
    [ObservableProperty] private bool _justCopied;

    public RelayCommand CopyCommand { get; }

    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public TimelineItemViewModel(TimelineItem item, DateTimeOffset now)
    {
        Item = item;
        TimeText = item.HasTime ? SessionText.Clock(item.At, now) : "";
        (PreviewText, IsTruncated) = TextPreview.Make(item.Text);
        ToggleExpandedCommand = new RelayCommand(() => { if (IsTruncated) IsExpanded = !IsExpanded; });
        CopyCommand = new RelayCommand(Copy);
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); JustCopied = false; };
    }

    /// <summary>全文（省略表示の分も含む）をコピーする。ダブルクリックの1回目のクリックで全文の開閉が働いているので、元に戻す。</summary>
    private void Copy()
    {
        if (IsTruncated)
            IsExpanded = !IsExpanded;
        if (!ClipboardHelper.TrySetText(Item.Text))
            return;
        JustCopied = true;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    /// <summary>画面に出す本文：開いていれば全文、閉じていれば先頭だけ。</summary>
    public string DisplayText => IsExpanded ? Item.Text : PreviewText;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
}

/// <summary>右ペインのタイムライン。表示するセッションの全イベントを持ち、種別フィルタは表示側で絞る。</summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    /// <summary>これを超える差し替えは、1件ずつ通知せずコレクションごと作り直す。</summary>
    private const int BulkThreshold = 30;

    private TimelineItemViewModel? _highlighted;

    [ObservableProperty] private ObservableCollection<TimelineItemViewModel> _items = [];
    [ObservableProperty] private ICollectionView _view;

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

    public TimelineViewModel()
    {
        _view = CreateView(_items);
        _copiedAllTimer.Tick += (_, _) => { _copiedAllTimer.Stop(); CopiedAll = false; };
    }

    private ICollectionView CreateView(ObservableCollection<TimelineItemViewModel> items)
    {
        var view = CollectionViewSource.GetDefaultView(items);
        view.Filter = o => o is TimelineItemViewModel i && IsShown(i.Kind) && TimelineText.Matches(i.Item, SearchText);
        return view;
    }

    partial void OnShowInputChanged(bool value) => RefreshView();
    partial void OnShowResponseChanged(bool value) => RefreshView();
    partial void OnShowThoughtChanged(bool value) => RefreshView();
    partial void OnShowToolChanged(bool value) => RefreshView();
    partial void OnShowCompactionChanged(bool value) => RefreshView();
    partial void OnSearchTextChanged(string value) => RefreshView();

    private void RefreshView()
    {
        View.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var shown = View.Cast<object>().Count();
        var total = Items.Count(i => IsShown(i.Kind));
        CountText = TimelineText.CountText(shown, total, SearchText);
    }

    /// <summary>いま表示している行（種別・検索で絞った結果）を、「時刻　種別：本文」の形で連結した文字列。</summary>
    public string CopyAllText() => TimelineText.CopyAll(View.Cast<TimelineItemViewModel>().Select(i => i.Item));

    /// <summary>「全部コピー」の直後（約1.5秒）。ボタンの文言を「コピーしました」にする。</summary>
    [ObservableProperty] private bool _copiedAll;

    private readonly DispatcherTimer _copiedAllTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    [RelayCommand]
    private void CopyAll()
    {
        if (!View.Cast<object>().Any() || !ClipboardHelper.TrySetText(CopyAllText()))
            return;
        CopiedAll = true;
        _copiedAllTimer.Stop();
        _copiedAllTimer.Start();
    }

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
        Items = new ObservableCollection<TimelineItemViewModel>(items);
        View = CreateView(Items);
        UpdateCount();
    }

    /// <summary>
    /// イベント全体からタイムラインを作り直す。変わった所（通常は末尾）だけ差し替えるので、スクロール位置が飛ばない。
    /// </summary>
    public void SetEvents(IReadOnlyList<AgentEvent> events, DateTimeOffset now)
    {
        var built = TimelineBuilder.Build(events);

        var common = 0;
        while (common < Items.Count && common < built.Count && Items[common].Item == built[common])
            common++;

        var removed = Items.Count - common;
        var added = built.Count - common;
        if (removed + added == 0)
            return;

        if (removed + added > BulkThreshold)
        {
            var all = Items.Take(common).ToList();
            all.AddRange(built.Skip(common).Select(i => new TimelineItemViewModel(i, now)));
            Replace(all);
            return;
        }

        while (Items.Count > common)
        {
            if (ReferenceEquals(Items[^1], _highlighted)) _highlighted = null;
            Items.RemoveAt(Items.Count - 1);
        }
        for (var i = common; i < built.Count; i++)
            Items.Add(new TimelineItemViewModel(built[i], now));
        UpdateCount();
    }

    /// <summary>表示中の最後の行までスクロールする（セッションを開いた直後に最新から見せる）。</summary>
    public void ScrollToEnd()
    {
        var last = Items.LastOrDefault(i => IsShown(i.Kind));
        if (last is not null)
            ScrollRequested?.Invoke(last);
    }

    /// <summary>該当イベントの行を強調してスクロールする。種別フィルタが OFF なら ON に切り替える。</summary>
    public bool JumpTo(long seq, TimelineKind kind)
    {
        var item = Items.FirstOrDefault(i => i.Item.Seq == seq || i.Item.EndSeq == seq);
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
}