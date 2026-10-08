using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.Tests.Presentation;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>Esc（Issue #28。計画 7.4）：ドキュメントの拡大とダッシュボードのタイムラインの拡大が、タブをまたいで戻らない。</summary>
public sealed class DocumentsEscapeTests : IDisposable
{
    private sealed class UiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => Dispatcher.UIThread.Post(() => d(state));
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-docesc-" + Guid.NewGuid().ToString("N"));
    private DocumentsViewModel? _docs;
    private MainVmHarness? _main;

    public void Dispose()
    {
        _docs?.Dispose();
        _main?.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>ダッシュボードとドキュメントを重ねて置く（本物は MainWindow のタブ。IsVisible で切り替える）。</summary>
    private (Window Window, DashboardView Dashboard, DocumentsView Documents) Open()
    {
        var project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(Path.Combine(project, "docs"));
        File.WriteAllText(Path.Combine(project, "docs", "a.md"), "# a");
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        _docs = new DocumentsViewModel(project, new ProjectSettingsStore(paths), paths, () => false, new UiContext(), new FakeUiServices());
        _main = new MainVmHarness();
        Scene.Fill(_main);

        var dashboard = new DashboardView { DataContext = _main.Vm };
        var documents = new DocumentsView { DataContext = _docs };
        var window = new Window { Width = 1280, Height = 900, Content = new Grid { Children = { dashboard, documents } } };
        window.Show();
        _docs.Start();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (_docs.IsScanning && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        _docs.OpenFromOutside("docs/a.md");
        Flush();
        return (window, dashboard, documents);
    }

    private static void PressEscape(Window w)
    {
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Flush();
    }

    [AvaloniaFact]
    public void Escape_collapses_an_expanded_document()
    {
        var (w, dashboard, documents) = Open();
        dashboard.IsVisible = false;
        _docs!.ToggleExpandedCommand.Execute(null);
        Flush();
        Assert.True(_docs.IsExpanded);

        PressEscape(w);

        Assert.False(_docs.IsExpanded);
        Assert.True(documents.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Escape_without_an_expansion_is_not_handled()
    {
        var (w, dashboard, _) = Open();
        dashboard.IsVisible = false;
        var handled = false;
        w.KeyDown += (_, e) => handled = e.Handled;
        w.AddHandler(InputElement.KeyDownEvent, (_, e) => handled = e.Handled, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        PressEscape(w);

        Assert.False(_docs!.IsExpanded);
        Assert.False(handled);                         // 通常のときの Esc を、ほかの処理から奪わない
    }

    [AvaloniaFact]
    public void Escape_does_not_collapse_a_document_in_a_hidden_tab()
    {
        var (w, dashboard, documents) = Open();
        _docs!.ToggleExpandedCommand.Execute(null);
        Flush();
        documents.IsVisible = false;                   // ダッシュボードを見ている

        PressEscape(w);

        Assert.True(_docs.IsExpanded);
    }

    [AvaloniaFact]
    public void Escape_does_not_collapse_the_timeline_in_a_hidden_tab()
    {
        var (w, dashboard, documents) = Open();
        _main!.Vm.ToggleTimelineExpandedCommand.Execute(null);
        Flush();
        Assert.True(_main.Vm.IsTimelineExpanded);
        dashboard.IsVisible = false;                   // ドキュメントを見ている

        PressEscape(w);

        Assert.True(_main.Vm.IsTimelineExpanded);
    }

    [AvaloniaFact]
    public void With_both_expanded_only_the_visible_tab_collapses()
    {
        var (w, dashboard, documents) = Open();
        _main!.Vm.ToggleTimelineExpandedCommand.Execute(null);
        _docs!.ToggleExpandedCommand.Execute(null);
        Flush();

        dashboard.IsVisible = false;                   // ドキュメントを見ている
        PressEscape(w);
        Assert.False(_docs.IsExpanded);
        Assert.True(_main.Vm.IsTimelineExpanded);

        _docs.ToggleExpandedCommand.Execute(null);
        dashboard.IsVisible = true;
        documents.IsVisible = false;                   // ダッシュボードを見ている
        PressEscape(w);
        Assert.False(_main.Vm.IsTimelineExpanded);
        Assert.True(_docs.IsExpanded);
    }
}
