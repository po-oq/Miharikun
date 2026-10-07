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

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>対象フォルダが決まって組み立てが終わったあと（「選ぶ前の中身」から「組み立て後の中身」へ。計画 7.7）。</summary>
    public void Attach(AppComposition composition)
    {
        _composition = composition;
        DataContext = composition.ViewModel;
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
        if (_closing)
        {
            e.Cancel = true;   // 確認を出している間に、もう一度閉じようとした
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

    private void OnInstallHookClick(object? sender, RoutedEventArgs e) => RunSafe(_composition!.HookSetup.InstallFromMenuAsync);

    private void OnUninstallHookClick(object? sender, RoutedEventArgs e) => RunSafe(_composition!.HookSetup.UninstallFromMenuAsync);
}
