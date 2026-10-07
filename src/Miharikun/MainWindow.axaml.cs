using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Miharikun.Core.Settings;
using Miharikun.Views;

namespace Miharikun;

public partial class MainWindow : Window
{
    private AppComposition? _composition;
    private bool _closeConfirmed;
    private bool _closing;
    private bool _settingsOpen;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>対象フォルダが決まって組み立てが終わったあと（「選ぶ前の中身」から「組み立て後の中身」へ。計画 7.7）。</summary>
    public void Attach(AppComposition composition)
    {
        _composition = composition;
        DataContext = composition.ViewModel;
        DocumentsHost.DataContext = composition.Documents;
        MemoHost.DataContext = composition.Memo;
        Title = $"Miharikun - {composition.ViewModel.ProjectFolder}";
        PickerPanel.IsVisible = false;
        MainPanel.IsVisible = true;

        composition.Memo.OpenInDocumentsRequested += OpenInDocumentsTab;
        composition.Start();

        // 画面が出てから、hook が未導入なら導入を提案する（計画 7.16）。mac は、フォルダを選んで組み立てが終わってから。
        if (IsVisible)
            PostStartupHookCheck();
        else
            Opened += OnOpenedOnce;
    }

    private void OnOpenedOnce(object? sender, EventArgs e)
    {
        Opened -= OnOpenedOnce;
        PostStartupHookCheck();
    }

    private void PostStartupHookCheck() =>
        Dispatcher.UIThread.Post(() => RunSafe(_composition!.HookSetup.CheckAtStartupAsync), DispatcherPriority.Background);

    /// <summary>イベントから呼ぶ非同期の処理。例外はログに残す（イベントの <c>async void</c> から外へ出さない）。</summary>
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

    // ---------------------------------------------------------------- タブ（計画 7.17）

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DashboardHost is null)
            return;   // 画面の組み立て中
        var index = MainTabs.SelectedIndex;
        DashboardHost.IsVisible = index == 0;
        DocumentsHost.IsVisible = index == 1;
        MemoHost.IsVisible = index == 2;
        if (index == 1)
            _composition?.Documents.Start();   // 最初に見えたとき、監視と走査を始める（起動を遅くしない）
        if (index == 2)
            _composition?.Memo.Start();        // 最初に見えたとき、読み込みと監視を始める（読めていなければ読み直す）
    }

    /// <summary>メモのリンクから：ドキュメントタブに切り替えてから、そのファイルを選ぶ。</summary>
    private void OpenInDocumentsTab(string relativePath)
    {
        MainTabs.SelectedIndex = 1;
        Dispatcher.UIThread.Post(() => _composition!.Documents.OpenFromOutside(relativePath), DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- 閉じるときの流れ（計画 7.6。Avalonia の Closing は待てない）

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || _composition is null)
            return;   // 閉じる（フォルダを選ぶ前も、何もしない）
        if (_closing || _settingsOpen || _composition.Ui.OpenDialogs > 0)
        {
            e.Cancel = true;   // 確認・設定を出している間に、もう一度閉じようとした（2 つ目のダイアログは出さない）
            return;
        }

        _composition.ViewModel.Flush();   // 入力途中のメモ（セッションのメモ）を失わない
        if (!_composition.Memo.IsDirty)
            return;

        // プロジェクトのメモ（要件 12.10）が未保存：先に取り消して、確認はあとで出す（終了の処理の最中に入れ子の画面を作らない）
        e.Cancel = true;
        _closing = true;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        MainTabs.SelectedIndex = 2;
        Dispatcher.UIThread.Post(ConfirmUnsavedMemo);
    }

    private async void ConfirmUnsavedMemo()
    {
        try
        {
            switch (await UnsavedMemoDialog.ShowAsync(this))
            {
                case UnsavedMemoChoice.Save when _composition!.Memo.TrySaveForClose():
                case UnsavedMemoChoice.Discard:
                    _closeConfirmed = true;
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("閉じる前の確認で例外: " + ex);
        }
        finally
        {
            _closing = false;
        }
    }

    // ---------------------------------------------------------------- ⚙ のメニュー

    private void OnMenuOpened(object? sender, EventArgs e)
    {
        var mode = _composition?.Theme.Mode;
        ThemeSystem.IsChecked = mode == AppTheme.System;
        ThemeLight.IsChecked = mode == AppTheme.Light;
        ThemeDark.IsChecked = mode == AppTheme.Dark;
    }

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var mode))
            _composition?.Theme.Set(mode);
    }

    /// <summary>プロジェクトごとの除外パターン（要件 12.7）。保存すると監視と走査をやり直す。</summary>
    private void OnDocumentSettingsClick(object? sender, RoutedEventArgs e) => RunSafe(async () =>
    {
        var documents = _composition!.Documents;
        _settingsOpen = true;
        string? text;
        try
        {
            text = await DocumentSettingsDialog.ShowAsync(this, documents.ProjectFolder, documents.CurrentIgnoreText, documents.DefaultIgnoreText);
        }
        finally
        {
            _settingsOpen = false;
        }
        if (text is not null)
            documents.ApplyIgnoreText(text);
    });

    private void OnInstallHookClick(object? sender, RoutedEventArgs e) => RunSafe(_composition!.HookSetup.InstallFromMenuAsync);

    private void OnUninstallHookClick(object? sender, RoutedEventArgs e) => RunSafe(_composition!.HookSetup.UninstallFromMenuAsync);

    /// <summary>
    /// アプリ全体の設定（要件 12.9）：「停止とみなす時間」と「Hook の置き場所」。保存するとすぐ効く。
    /// 置き場所が変わったら、導入し直すかを聞く（8.2）。
    /// </summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e) => RunSafe(async () =>
    {
        var composition = _composition!;
        var before = composition.HookSetup.CurrentHookDir;
        AppSettingsResult? result;
        _settingsOpen = true;
        try
        {
            result = await AppSettingsDialog.ShowAsync(this, composition.ViewModel.RunningTimeoutMinutes, before, composition.HookSetup.DefaultHookDir);
        }
        finally
        {
            _settingsOpen = false;
        }
        if (result is null)
            return;

        composition.Settings.SaveRunningTimeoutMinutes(result.RunningTimeoutMinutes);
        composition.ViewModel.SetRunningTimeout(result.RunningTimeoutMinutes);
        if (!string.Equals(result.HookDir, Path.TrimEndingDirectorySeparator(before), StringComparison.OrdinalIgnoreCase))
            await composition.HookSetup.ChangePlacementAsync(result.HookDir);
    });
}
