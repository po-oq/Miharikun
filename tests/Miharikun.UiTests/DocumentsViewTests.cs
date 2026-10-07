using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.Tests.Presentation;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>ドキュメントタブ（31-2）：一覧・ツリー・概要・ボタン・空の表示。プレビューの中身（WebView）は実機で確かめる。</summary>
public sealed class DocumentsViewTests : IDisposable
{
    /// <summary>VM が背景から送ってくる更新を、UI スレッドで実行する。</summary>
    private sealed class UiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => Dispatcher.UIThread.Post(() => d(state));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-docview-" + Guid.NewGuid().ToString("N"));
    private readonly FakeUiServices _services = new();
    private DocumentsViewModel? _vm;

    public void Dispose()
    {
        _vm?.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (Window Window, DocumentsViewModel Vm) Open(bool withFiles)
    {
        var project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(Path.Combine(project, "docs", "sub"));
        if (withFiles)
        {
            File.WriteAllText(Path.Combine(project, "docs", "a.md"), "# 見出し A\n本文");
            File.WriteAllText(Path.Combine(project, "docs", "sub", "b.html"), "<html><title>B</title></html>");
        }
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        _vm = new DocumentsViewModel(project, new ProjectSettingsStore(paths), paths, () => false, new UiContext(), _services);
        var window = new Window { Width = 1100, Height = 600, Content = new DocumentsView { DataContext = _vm } };
        window.Show();
        _vm.Start();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (_vm.IsScanning && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Flush();
        return (window, _vm);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Window w, string automationId) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == automationId);

    [AvaloniaFact]
    public void The_list_shows_the_files_and_the_tree_shows_the_folders_with_counts()
    {
        var (w, vm) = Open(withFiles: true);

        Assert.Equal(2, Find<ListBox>(w, "DocList").ItemCount);
        Assert.False(Find<TextBlock>(w, "DocEmpty").IsVisible);
        var tree = Find<TreeView>(w, "DocTree");
        Assert.Equal(1, tree.ItemCount);
        Assert.Equal(2, vm.TreeRoots[0].Count);
    }

    [AvaloniaFact]
    public void Selecting_a_row_shows_the_overview_and_enables_the_buttons()
    {
        var (w, vm) = Open(withFiles: true);
        Assert.False(Find<Button>(w, "OpenFolderButton").IsEnabled);

        vm.SelectedRow = vm.Rows.First(r => r.Name == "a.md");
        Flush();

        Assert.NotNull(vm.Overview);
        Assert.True(Find<Button>(w, "OpenFolderButton").IsEnabled);
        Assert.True(Find<Button>(w, "OpenExternalButton").IsEnabled);
        Assert.True(Find<Button>(w, "ReloadPreviewButton").IsEnabled);
        Assert.Equal(ShellOpen.RevealButtonText, Find<Button>(w, "OpenFolderButton").Content);
    }

    [AvaloniaFact]
    public void The_filter_box_narrows_the_list()
    {
        var (w, vm) = Open(withFiles: true);

        Find<TextBox>(w, "DocFilter").Text = "b.html";
        Flush();
        var until = DateTime.UtcNow.AddSeconds(5);
        while (vm.Rows.Count != 1 && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.Equal(1, Find<ListBox>(w, "DocList").ItemCount);
    }

    [AvaloniaFact]
    public void A_folder_without_documents_shows_the_empty_text()
    {
        var (w, _) = Open(withFiles: false);

        Assert.True(Find<TextBlock>(w, "DocEmpty").IsVisible);
    }

    [AvaloniaFact]
    public void The_settings_dialog_returns_the_text_on_save_and_null_on_cancel()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new DocumentSettingsDialog("/p", "node_modules/\n", "default\n");
        var task = dialog.ShowDialog<string?>(owner);
        Flush();

        dialog.FindControl<Button>("ResetButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("default\n", dialog.FindControl<TextBox>("IgnoreBox")!.Text);
        dialog.FindControl<Button>("SaveButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Flush();

        Assert.Equal("default\n", task.GetAwaiter().GetResult());
    }
}
