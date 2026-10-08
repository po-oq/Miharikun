using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

    // ── 拡大モード（Issue #28。計画 7.8）─────────────────────────────

    private static MarkdownPreview PreviewOf(Window w) => w.GetVisualDescendants().OfType<MarkdownPreview>().First();

    private static OutlineHeading H(int level, string text, bool? done = null, int d = 0, int t = 0) =>
        new(level, text, text.ToLowerInvariant(), done, d, t);

    private static (Window Window, DocumentsViewModel Vm) OpenSelected(DocumentsViewTests t, string name)
    {
        var (w, vm) = t.Open(withFiles: true);
        vm.SelectedRow = vm.Rows.First(r => r.Name == name);
        Flush();
        return (w, vm);
    }

    [AvaloniaFact]
    public void The_expand_button_is_disabled_without_a_file_and_the_toggle_changes_its_text()
    {
        var (w, vm) = Open(withFiles: true);
        var button = Find<Button>(w, "DocExpandButton");
        Assert.Equal("⤢ 拡大", button.Content);
        Assert.False(button.Command!.CanExecute(null));

        vm.SelectedRow = vm.Rows.First(r => r.Name == "a.md");
        Flush();
        Assert.True(button.Command!.CanExecute(null));

        button.Command!.Execute(null);
        Flush();
        Assert.Equal("⤡ 戻す", button.Content);
    }

    [AvaloniaFact]
    public void Expanding_hides_the_upper_bar_tree_list_and_overview_and_widens_the_content()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        var preview = PreviewOf(w);
        var widthBefore = preview.Bounds.Width;

        vm.ToggleExpandedCommand.Execute(null);
        Flush();

        Assert.False(Find<Button>(w, "RescanButton").IsEffectivelyVisible);
        Assert.False(Find<TreeView>(w, "DocTree").IsEffectivelyVisible);
        Assert.False(Find<ListBox>(w, "DocList").IsEffectivelyVisible);
        Assert.False(Find<ContentControl>(w, "DocOverview").IsEffectivelyVisible);
        Assert.True(preview.Bounds.Width > widthBefore * 1.3);
        Assert.True(Find<Button>(w, "DocExpandButton").IsEffectivelyVisible);

        vm.Collapse();
        Flush();

        Assert.True(Find<Button>(w, "RescanButton").IsEffectivelyVisible);
        Assert.True(Find<TreeView>(w, "DocTree").IsEffectivelyVisible);
        Assert.True(Find<ListBox>(w, "DocList").IsEffectivelyVisible);
        Assert.True(Find<ContentControl>(w, "DocOverview").IsEffectivelyVisible);
        Assert.Equal(widthBefore, preview.Bounds.Width, 1);          // 元の幅に戻る
    }

    [AvaloniaFact]
    public void The_outline_column_shows_only_while_expanded_on_a_markdown_file()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        var outline = Find<ListBox>(w, "DocOutline");
        var preview = PreviewOf(w);
        var widthNormal = preview.Bounds.Width;
        Assert.False(outline.IsEffectivelyVisible);

        vm.ToggleExpandedCommand.Execute(null);
        Flush();
        Assert.True(outline.IsEffectivelyVisible);
        Assert.True(outline.Bounds.Width >= 160);
        var widthWithOutline = preview.Bounds.Width;

        vm.OpenFromOutside("docs/sub/b.html");                         // html には目次を出さない（プレビューは全幅）
        Flush();
        Assert.True(vm.IsExpanded);
        Assert.False(outline.IsEffectivelyVisible);
        Assert.True(preview.Bounds.Width > widthWithOutline + 150);

        vm.OpenFromOutside("docs/a.md");
        Flush();
        Assert.True(outline.IsEffectivelyVisible);
        Assert.Equal(widthWithOutline, preview.Bounds.Width, 1);

        vm.Collapse();
        Flush();
        Assert.Equal(widthNormal, preview.Bounds.Width, 1);
    }

    [AvaloniaFact]
    public void The_preview_stays_the_same_attached_instance_through_expand_and_collapse()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        var preview = PreviewOf(w);
        var parent = preview.Parent;

        vm.ToggleExpandedCommand.Execute(null);
        Flush();
        Assert.Same(preview, PreviewOf(w));
        Assert.Same(parent, preview.Parent);
        Assert.True(preview.IsAttachedToVisualTree());

        vm.Collapse();
        Flush();
        Assert.Same(preview, PreviewOf(w));
        Assert.Same(parent, preview.Parent);
        Assert.Single(w.GetVisualDescendants().OfType<MarkdownPreview>());
    }

    [AvaloniaFact]
    public void Outline_items_show_their_text_task_counts_and_progress()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        vm.ToggleExpandedCommand.Execute(null);
        Flush();

        vm.OnOutline(vm.CurrentTarget, [H(1, "計画"), H(2, "済み", true, 2, 5), H(2, "未了", false)]);
        Flush();

        var texts = Find<ListBox>(w, "DocOutline").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("計画", texts);
        Assert.Contains("✅ 済み", texts);
        Assert.Contains("⬜ 未了", texts);
        Assert.Contains("2/5", texts);
        Assert.Equal("✅ 1 / 2", Find<TextBlock>(w, "DocProgress").Text);
        Assert.True(Find<TextBlock>(w, "DocProgress").IsEffectivelyVisible);
        Assert.False(Find<TextBlock>(w, "DocOutlineEmpty").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void An_outline_without_headings_says_so_and_hides_the_progress()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        vm.ToggleExpandedCommand.Execute(null);
        Flush();

        vm.OnOutline(vm.CurrentTarget, []);
        Flush();

        Assert.True(Find<TextBlock>(w, "DocOutlineEmpty").IsEffectivelyVisible);
        Assert.False(Find<TextBlock>(w, "DocProgress").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Tapping_an_outline_item_asks_the_preview_to_jump_every_time()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        vm.ToggleExpandedCommand.Execute(null);
        vm.OnOutline(vm.CurrentTarget, [H(1, "計画"), H(2, "節")]);
        Flush();
        var requested = new List<string>();
        vm.HeadingScrollRequested += requested.Add;
        var row = Find<ListBox>(w, "DocOutline").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "節");
        var point = row.TranslatePoint(new Point(4, 4), w)!.Value;

        for (var i = 0; i < 2; i++)
        {
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Flush();
        }

        Assert.Equal(["節", "節"], requested);
    }

    [AvaloniaFact]
    public void The_expanded_header_shows_the_title_and_path()
    {
        var (w, vm) = OpenSelected(this, "a.md");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (vm.Overview is null && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        vm.ToggleExpandedCommand.Execute(null);
        Flush();

        var texts = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("— 見出し A", texts);
        Assert.Contains("docs/a.md", texts);
    }
}
