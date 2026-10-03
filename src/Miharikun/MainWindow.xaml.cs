using System.Windows.Threading;
using Miharikun.ViewModels;
using Wpf.Ui.Controls;

namespace Miharikun;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Title = $"Miharikun - {viewModel.ProjectFolder}";
        TitleBar.Title = Title;

        // 行が作られてからでないとスクロールできないので、レイアウト後に実行する。
        viewModel.Timeline.ScrollRequested += item =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => TimelineList.ScrollIntoView(item));

        _clock.Tick += (_, _) => _viewModel.Tick();
        _clock.Start();
        Closing += (_, _) => _viewModel.Flush();   // 入力途中のメモを失わない
        Closed += (_, _) => _clock.Stop();
    }
}