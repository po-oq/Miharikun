using System.ComponentModel;
using System.Windows.Threading;
using Miharikun.Core.Settings;
using Miharikun.ViewModels;
using Wpf.Ui.Controls;

namespace Miharikun;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    private readonly HookSetup _hookSetup;
    private readonly ThemeService _theme;
    private readonly DocumentsViewModel _documents;
    private readonly MemoViewModel _memo;
    private readonly AppSettingsStore _settings;

    public MainWindow(MainViewModel viewModel, DocumentsViewModel documents, MemoViewModel memo, HookSetup hookSetup, ThemeService theme, AppSettingsStore settings)
    {
        _settings = settings;
        _viewModel = viewModel;
        _hookSetup = hookSetup;
        _theme = theme;
        _documents = documents;
        _memo = memo;
        DataContext = viewModel;
        InitializeComponent();
        DocumentsHost.DataContext = documents;
        MemoHost.DataContext = memo;
        theme.Changed += _ =>
        {
            documents.OnThemeChanged();
            memo.OnThemeChanged();
        };
        memo.OpenInDocumentsRequested += OpenInDocumentsTab;

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

        // 画面が出てから、hook が未導入なら導入を提案する
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => RunSafe(_hookSetup.CheckAtStartupAsync)));
        Closing += OnClosing;
    }

    /// <summary>イベントから呼ぶ非同期の処理。例外はログに残す（イベントの `async void` から外へ出さない）。</summary>
    private static async void RunSafe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLog.Write("画面の操作で例外: " + ex);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _viewModel.Flush();   // 入力途中のメモ（セッションのメモ）を失わない

        // プロジェクトのメモ（12.10）：未保存なら確認する。保存に失敗したら閉じない。
        if (!_memo.IsDirty)
            return;
        MemoTab.IsSelected = true;
        var dialog = new Views.UnsavedMemoDialog { Owner = this };
        dialog.ShowDialog();
        switch (dialog.Choice)
        {
            case Views.UnsavedMemoChoice.Save:
                if (!_memo.TrySaveForClose())
                    e.Cancel = true;
                break;
            case Views.UnsavedMemoChoice.Cancel:
                e.Cancel = true;
                break;
        }
    }

    /// <summary>メモのリンクから：ドキュメントタブに切り替えてから、そのファイルを選ぶ（タブの Loaded で走査が始まる道を、先に通す）。</summary>
    private void OpenInDocumentsTab(string relativePath)
    {
        DocumentsTab.IsSelected = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _documents.OpenFromOutside(relativePath));
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

    /// <summary>アプリ全体の設定（12.9）：「停止とみなす時間」と「Hook exe の置き場所」。保存するとすぐ効く。置き場所が変わったら、導入し直すかを聞く（8.2）。</summary>
    private void OnSettingsClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var before = _hookSetup.CurrentHookDir;
        var dialog = new Views.AppSettingsDialog(_viewModel.RunningTimeoutMinutes, before, _hookSetup.DefaultHookDir) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _settings.SaveRunningTimeoutMinutes(dialog.RunningTimeoutMinutes);
        _viewModel.SetRunningTimeout(dialog.RunningTimeoutMinutes);

        if (!string.Equals(dialog.HookDir, System.IO.Path.TrimEndingDirectorySeparator(before), StringComparison.OrdinalIgnoreCase))
            RunSafe(() => _hookSetup.ChangePlacementAsync(dialog.HookDir));
    }

    private void OnDocumentSettingsClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Views.DocumentSettingsDialog(_documents.ProjectFolder, _documents.CurrentIgnoreText, _documents.DefaultIgnoreText) { Owner = this };
        if (dialog.ShowDialog() == true)
            _documents.ApplyIgnoreText(dialog.IgnoreText);
    }

    private void OnInstallHookClick(object sender, System.Windows.RoutedEventArgs e) => RunSafe(_hookSetup.InstallFromMenuAsync);

    private void OnUninstallHookClick(object sender, System.Windows.RoutedEventArgs e) => RunSafe(_hookSetup.UninstallFromMenuAsync);

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
