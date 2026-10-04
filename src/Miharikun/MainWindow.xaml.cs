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
    private readonly DocumentsViewModel _documents;

    public MainWindow(MainViewModel viewModel, DocumentsViewModel documents, HookSetup hookSetup, ThemeService theme)
    {
        _viewModel = viewModel;
        _hookSetup = hookSetup;
        _theme = theme;
        _documents = documents;
        DataContext = viewModel;
        InitializeComponent();
        DocumentsHost.DataContext = documents;

        Title = $"Miharikun - {viewModel.ProjectFolder}";
        TitleBar.Title = Title;

        viewModel.CardScrollRequested += card =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => CardList.ScrollIntoView(card));

        // 行が作られてからでないとスクロールできないので、レイアウト後に実行する。
        viewModel.Timeline.ScrollRequested += item =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => TimelineList.ScrollIntoView(item));

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsTimelineExpanded))
                ApplyTimelineExpanded(viewModel.IsTimelineExpanded);
        };

        _clock.Tick += (_, _) => _viewModel.Tick();
        _clock.Start();
        // 画面が出てから、hook が未導入なら導入を提案する
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _hookSetup.CheckAtStartup(this));
        Closing += (_, _) => _viewModel.Flush();   // 入力途中のメモを失わない
        Closed += (_, _) => _clock.Stop();
    }

    // 拡大モード（要件 12.4.1）：左と中央を隠して、右ペインを全幅に広げる。戻すときは元の幅に戻す。
    private System.Windows.GridLength _savedLeft, _savedRight, _savedRecent;

    private void ApplyTimelineExpanded(bool expanded)
    {
        var hide = expanded ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        PaneLeft.Visibility = Split1.Visibility = PaneCenter.Visibility = Split2.Visibility = hide;
        RecentSplitter.Visibility = RecentPanel.Visibility = hide;

        if (expanded)
        {
            _savedLeft = ColLeft.Width;
            _savedRight = ColRight.Width;
            _savedRecent = RowRecent.Height;
            ColLeft.MinWidth = ColCenter.MinWidth = 0;
            ColLeft.Width = ColCenter.Width = new System.Windows.GridLength(0);
            ColRight.Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
            RowRecent.MinHeight = 0;
            RowRecent.Height = new System.Windows.GridLength(0);
        }
        else
        {
            ColLeft.MinWidth = 260;
            ColCenter.MinWidth = 320;
            ColLeft.Width = _savedLeft.Value > 0 ? _savedLeft : new System.Windows.GridLength(360);
            ColCenter.Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
            ColRight.Width = _savedRight.Value > 0 ? _savedRight : new System.Windows.GridLength(340);
            RowRecent.MinHeight = 60;
            RowRecent.Height = _savedRecent.Value > 0 ? _savedRecent : new System.Windows.GridLength(150);
        }
    }

    // Esc で拡大を戻す（名前の編集中の Esc など、先に処理されたものは除く）
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape && !e.Handled && _viewModel.IsTimelineExpanded)
        {
            _viewModel.ToggleTimelineExpandedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMenuButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnDocumentSettingsClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Views.DocumentSettingsDialog(_documents.ProjectFolder, _documents.CurrentIgnoreText, _documents.DefaultIgnoreText) { Owner = this };
        if (dialog.ShowDialog() == true)
            _documents.ApplyIgnoreText(dialog.IgnoreText);
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
