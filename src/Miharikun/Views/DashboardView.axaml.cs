using Avalonia.Controls;
using Avalonia.Input;
using Miharikun.ViewModels;

namespace Miharikun.Views;

public partial class DashboardView : UserControl
{
    private MainViewModel? _viewModel;
    private GridLength _savedLeft, _savedRight;
    private TopLevel? _topLevel;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = DataContext as MainViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                ApplyTimelineExpanded(_viewModel.IsTimelineExpanded);
            }
        };
        // Esc で拡大を戻す（名前の編集中の Esc など、先に処理されたものは除く）。フォーカスがどこにも無くても効くよう、ウィンドウで受ける
        AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDown);
            _topLevel = null;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsTimelineExpanded) && _viewModel is not null)
            ApplyTimelineExpanded(_viewModel.IsTimelineExpanded);
    }

    private bool _expandedApplied;

    // 列：0 左・1 区切り・2 中央・3 区切り・4 右（x:Name は ColumnDefinition には付かないので、番号で持つ）
    private ColumnDefinition ColLeft => Root.ColumnDefinitions[0];
    private ColumnDefinition ColCenter => Root.ColumnDefinitions[2];
    private ColumnDefinition ColRight => Root.ColumnDefinitions[4];

    /// <summary>拡大モード（要件 12.4.1）：左と中央を隠して、右ペインを全幅に広げる。戻すときは元の幅に戻す。</summary>
    private void ApplyTimelineExpanded(bool expanded)
    {
        if (expanded == _expandedApplied)
            return;
        _expandedApplied = expanded;

        PaneLeft.IsVisible = Split1.IsVisible = PaneCenter.IsVisible = Split2.IsVisible = !expanded;
        if (expanded)
        {
            _savedLeft = ColLeft.Width;
            _savedRight = ColRight.Width;
            ColLeft.MinWidth = ColCenter.MinWidth = 0;
            ColLeft.Width = ColCenter.Width = new GridLength(0);
            ColRight.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            ColLeft.MinWidth = 260;
            ColCenter.MinWidth = 320;
            ColLeft.Width = _savedLeft.Value > 0 ? _savedLeft : new GridLength(360);
            ColCenter.Width = new GridLength(1, GridUnitType.Star);
            ColRight.Width = _savedRight.Value > 0 ? _savedRight : new GridLength(340);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && _viewModel is { IsTimelineExpanded: true })
        {
            _viewModel.ToggleTimelineExpandedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
