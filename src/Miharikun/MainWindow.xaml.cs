using System.Windows.Threading;
using Miharikun.Core.Settings;
using Miharikun.ViewModels;
using Wpf.Ui.Controls;

namespace Miharikun;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly HookSetup _hookSetup;
    private readonly ThemeService _theme;

    public MainWindow(MainViewModel viewModel, HookSetup hookSetup, ThemeService theme)
    {
        _viewModel = viewModel;
        _hookSetup = hookSetup;
        _theme = theme;
        DataContext = viewModel;
        InitializeComponent();

        Title = $"Miharikun - {viewModel.ProjectFolder}";
        TitleBar.Title = Title;

        viewModel.CardScrollRequested += card =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => CardList.ScrollIntoView(card));

        // 行が作られてからでないとスクロールできないので、レイアウト後に実行する。
        viewModel.Timeline.ScrollRequested += item =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => TimelineList.ScrollIntoView(item));

        _clock.Tick += (_, _) => _viewModel.Tick();
        _clock.Start();
        // 画面が出てから、hook が未導入なら導入を提案する
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _hookSetup.CheckAtStartup(this));
        Closing += (_, _) => _viewModel.Flush();   // 入力途中のメモを失わない
        Closed += (_, _) => _clock.Stop();
    }

    private void OnMenuButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnInstallHookClick(object sender, System.Windows.RoutedEventArgs e) => _hookSetup.InstallFromMenu(this);

    private void OnUninstallHookClick(object sender, System.Windows.RoutedEventArgs e) => _hookSetup.UninstallFromMenu(this);

    // メニューを開くたびに、いまのテーマにチェックを付ける。
    private void OnThemeMenuOpened(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem parent)
            return;
        foreach (var item in parent.Items.OfType<System.Windows.Controls.MenuItem>())
            item.IsChecked = item.Tag is string tag && tag == _theme.Mode.ToString();
    }

    private void OnThemeClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var mode))
            _theme.Set(mode);
    }
}
