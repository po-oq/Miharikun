using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Miharikun.ViewModels;

namespace Miharikun.Views;

/// <summary>
/// ドキュメントタブ（要件 12.7）。監視と走査は、タブが最初に見えたとき <c>DocumentsViewModel.Start()</c> で始める（起動を遅くしない。MainWindow が呼ぶ）。
/// 画面は載せたまま隠すだけなので、プレビューの WebView は作り直されない（計画 7.17）。
/// 拡大モード（12.7.1）は、列の幅と IsVisible だけを変える（プレビューは動かさない。計画 7.8）。
/// </summary>
public partial class DocumentsView : UserControl
{
    private DocumentsViewModel? _viewModel;
    private TopLevel? _topLevel;
    private GridLength _savedTree, _savedList;
    private GridLength _savedOutline = new(260);
    private bool _expandedApplied;
    private bool? _outlineApplied;      // 最初は未適用（XAML の幅は目次を出した形なので、隠す形に 1 回合わせる）

    public DocumentsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Esc で拡大を戻す。フォーカスがどこにも無くても効くよう、ウィンドウで受ける（DashboardView と同じ）
        AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDown);
            _topLevel = null;
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as DocumentsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyExpanded(_viewModel.IsExpanded);
            ApplyOutlineVisible(_viewModel.IsOutlineVisible);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;
        if (e.PropertyName == nameof(DocumentsViewModel.IsExpanded))
            ApplyExpanded(_viewModel.IsExpanded);
        else if (e.PropertyName == nameof(DocumentsViewModel.IsOutlineVisible))
            ApplyOutlineVisible(_viewModel.IsOutlineVisible);
    }

    // 外側の列：0 ツリー・1 区切り・2 一覧・3 区切り・4 右（x:Name は ColumnDefinition に付かないので、番号で持つ）
    private ColumnDefinition ColTree => Body.ColumnDefinitions[0];
    private ColumnDefinition ColList => Body.ColumnDefinitions[2];

    // 内容カードの列：0 目次・1 区切り・2 プレビュー
    private ColumnDefinition ColOutline => ContentGrid.ColumnDefinitions[0];
    private ColumnDefinition ColOutlineSplit => ContentGrid.ColumnDefinitions[1];

    /// <summary>拡大：ツリーと一覧を隠して、内容カードを全幅に。戻すときは覚えた幅（区切り線で動かした幅も）に戻す。</summary>
    private void ApplyExpanded(bool expanded)
    {
        if (expanded == _expandedApplied)
            return;
        _expandedApplied = expanded;

        TreePane.IsVisible = Split1.IsVisible = ListPane.IsVisible = Split2.IsVisible = !expanded;
        if (expanded)
        {
            _savedTree = ColTree.Width;
            _savedList = ColList.Width;
            ColTree.MinWidth = ColList.MinWidth = 0;
            ColTree.Width = ColList.Width = new GridLength(0);
        }
        else
        {
            ColTree.MinWidth = 120;
            ColList.MinWidth = 160;
            ColTree.Width = _savedTree.Value > 0 ? _savedTree : new GridLength(200);
            ColList.Width = _savedList.Value > 0 ? _savedList : new GridLength(260);
        }
    }

    /// <summary>目次の列：md の拡大中だけ出す。隠すときは幅を 0 にする（Auto に頼らない。区切り線で px になった列に空白が残るため）。</summary>
    private void ApplyOutlineVisible(bool visible)
    {
        if (visible == _outlineApplied)
            return;
        _outlineApplied = visible;

        OutlinePane.IsVisible = OutlineSplit.IsVisible = visible;
        if (visible)
        {
            ColOutline.MinWidth = 160;
            ColOutline.Width = _savedOutline;
            ColOutlineSplit.Width = new GridLength(8);
        }
        else
        {
            if (ColOutline.Width.Value > 0)
                _savedOutline = ColOutline.Width;
            ColOutline.MinWidth = 0;
            ColOutline.Width = new GridLength(0);
            ColOutlineSplit.Width = new GridLength(0);
        }
    }

    // ── 目次の操作 ────────────────────────────────────────────────

    // 押したときだけ移る（一覧の選択の変化では移らない。作り直しで選択がずれても、プレビューは動かさない）
    private void OnOutlineTapped(object? sender, TappedEventArgs e)   // Tapped と DoubleTapped の両方（素早い 2 回目はダブルクリックになるため）
    {
        if ((e.Source as Control)?.DataContext is OutlineItemViewModel item)
            _viewModel?.JumpToHeadingCommand.Execute(item);
    }

    private void OnOutlineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OutlineList.SelectedItem is OutlineItemViewModel item)
        {
            _viewModel?.JumpToHeadingCommand.Execute(item);
            e.Handled = true;
        }
    }

    // ── Esc ──────────────────────────────────────────────────────

    // 先に処理されたもの（入力欄の Esc など）は除く。タブが見えているときだけ（隠れたタブの拡大は戻さない）。
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && IsEffectivelyVisible && _viewModel is { } vm && vm.Collapse())
            e.Handled = true;
    }
}
