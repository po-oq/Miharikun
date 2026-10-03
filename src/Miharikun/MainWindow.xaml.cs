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

        _clock.Tick += (_, _) => _viewModel.Tick();
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
    }
}