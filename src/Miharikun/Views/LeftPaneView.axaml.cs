using Avalonia.Controls;
using Avalonia.Threading;
using Miharikun.ViewModels;

namespace Miharikun.Views;

public partial class LeftPaneView : UserControl
{
    private MainViewModel? _viewModel;
    private bool _syncingAgent;

    public LeftPaneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.CardScrollRequested -= OnCardScrollRequested;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _viewModel = DataContext as MainViewModel;
            if (_viewModel is not null)
            {
                _viewModel.CardScrollRequested += OnCardScrollRequested;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                Dispatcher.UIThread.Post(SelectAgentChip, DispatcherPriority.Loaded);
            }
        };
    }

    /// <summary>「最近の入力」などで選ばれたカードを、見える所へ。並びが変わったあとに実行する（行が作られてからでないとスクロールできない）。</summary>
    private void OnCardScrollRequested(SessionCardViewModel card) =>
        Dispatcher.UIThread.Post(() => CardList.ScrollIntoView(card), DispatcherPriority.Background);

    // ---- エージェントのチップ（1 つだけ選ぶ） ----
    // ListBox の SelectedValue を AgentKey に双方向でバインドすると、「全て」の項目の Key が空文字なので、選択を作り直すときに
    // null が AgentKey に書き戻されてしまう。そこで、選ばれた項目（null は無視）と AgentKey を、ここで互いに写す。

    private void OnAgentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingAgent || _viewModel is null || AgentList.SelectedItem is not AgentTabItem tab)
            return;
        _viewModel.AgentKey = tab.Key;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AgentKey))
            SelectAgentChip();
    }

    private void SelectAgentChip()
    {
        if (_viewModel is null)
            return;
        var tab = _viewModel.AgentTabs.FirstOrDefault(t => t.Key == _viewModel.AgentKey);
        if (tab is null || ReferenceEquals(AgentList.SelectedItem, tab))
            return;
        _syncingAgent = true;
        try { AgentList.SelectedItem = tab; }
        finally { _syncingAgent = false; }
    }
}
