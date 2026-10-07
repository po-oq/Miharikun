using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Miharikun.ViewModels;

namespace Miharikun.Views;

public partial class RightPaneView : UserControl
{
    private MainViewModel? _viewModel;


    public RightPaneView()
    {
        InitializeComponent();
        TimelineList.EstimateHeight = EstimateRowHeight;
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.Timeline.ScrollRequested -= OnScrollRequested;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _viewModel = DataContext as MainViewModel;
            if (_viewModel is not null)
            {
                _viewModel.Timeline.ScrollRequested += OnScrollRequested;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                ApplyExpanded(_viewModel.IsTimelineExpanded);
            }
        };
    }

    private GridLength _savedRecent;
    private bool _expandedApplied;
    private RowDefinition RowRecent => Root.RowDefinitions[3];

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsTimelineExpanded) && _viewModel is not null)
            ApplyExpanded(_viewModel.IsTimelineExpanded);
    }

    /// <summary>拡大中は、下の「最近の入力」の行の高さを 0 にする（戻すときは元の高さ）。</summary>
    private void ApplyExpanded(bool expanded)
    {
        if (expanded == _expandedApplied)
            return;
        _expandedApplied = expanded;
        if (expanded)
        {
            _savedRecent = RowRecent.Height;
            RowRecent.MinHeight = 0;
            RowRecent.Height = new GridLength(0);
        }
        else
        {
            RowRecent.MinHeight = 60;
            RowRecent.Height = _savedRecent.Value > 0 ? _savedRecent : new GridLength(150);
        }
    }

    /// <summary>行が作られてからでないとスクロールできないので、レイアウト後に実行する（WPF 版の <c>BeginInvoke(Background)</c> と同じ。計画 7.9）。</summary>
    private void OnScrollRequested(TimelineItemViewModel item) =>
        Dispatcher.UIThread.Post(() =>
        {
            var index = _viewModel?.Timeline.VisibleItems.IndexOf(item) ?? -1;
            if (index >= 0)
                TimelineList.EnsureVisible(index);
        }, DispatcherPriority.Background);

    /// <summary>
    /// まだ作っていない行の高さの見積もり（本文の折り返しから）。作ると測った高さに置き換わるので、近ければ近いほど、
    /// 作るときのスクロールのずれが小さい。文字の幅：半角は 0.55 em、全角は 1 em。
    /// </summary>
    internal static double EstimateRowHeight(object row, double width)
    {
        if (row is not TimelineItemViewModel item)
            return 60;
        var big = item.Kind is Miharikun.Core.Sessions.TimelineKind.Input or Miharikun.Core.Sessions.TimelineKind.Response;
        var left = item.Kind == Miharikun.Core.Sessions.TimelineKind.Input ? 24 : 0;
        var right = item.Kind == Miharikun.Core.Sessions.TimelineKind.Response ? 36 : 12;
        var inner = Math.Max(40, width - left - right - 16 - 4);   // 吹き出しの余白 8×2・枠 2×2
        var font = big ? 12.0 : 11.0;
        var lineHeight = big ? 16.0 : 14.5;

        var lines = 0;
        foreach (var line in item.DisplayText.Split('\n'))
        {
            var w = 0.0;
            foreach (var c in line)
                w += c < 0x80 ? font * 0.55 : font;
            lines += Math.Max(1, (int)Math.Ceiling(w / inner));
        }
        return 6 + 4 + 8 + 15 + lines * lineHeight + (item.IsTruncated ? 15 : 0);   // 上下の余白・枠・余白・時刻の行・本文・「クリックで全文」
    }

    /// <summary>
    /// 行のクリック：1 回目のクリックで全文の開閉、ダブルクリック（2 回目）で全文をクリップボードへ（WPF 版の <c>LeftClick</c>／<c>LeftDoubleClick</c>
    /// と同じ。1 回目で働いた開閉は、コピー側で元に戻る）。Avalonia の <c>Tapped</c> は 2 回目にも来るので、<c>ClickCount</c> で分ける（計画 7.9）。
    /// </summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TimelineItemViewModel item } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount >= 2)
            item.CopyCommand.Execute(null);
        else
            item.ToggleExpandedCommand.Execute(null);
    }
}
