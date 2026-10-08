using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Core.Memo;
using Miharikun.Core.Storage;
using Miharikun.Tests.Presentation;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>メモタブ（31-3）：編集の入れ替え・フォーカス・保存のキー・キャンセル。プレビューの中身（WebView）は実機で確かめる。</summary>
public sealed class MemoViewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-memoview-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly FakeUiServices _services = new();
    private readonly ProjectMemoStore _store;
    private MemoViewModel? _vm;

    public MemoViewTests()
    {
        _project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(_project);
        _store = new ProjectMemoStore(new AppPaths(_dir));
        _store.Save(_project, "# 保存済み");
    }

    public void Dispose()
    {
        _vm?.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (Window Window, MemoViewModel Vm) Open()
    {
        _vm = new MemoViewModel(_project, _store, new AppPaths(_dir), () => false, new ImmediateSynchronizationContext(),
            _ => null, _services);
        var window = new Window { Width = 800, Height = 500, Content = new MemoView { DataContext = _vm } };
        window.Show();
        _vm.Start();
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
    public void Edit_swaps_the_preview_for_the_editor_and_focuses_it()
    {
        var (w, _) = Open();
        Assert.False(Find<TextBox>(w, "MemoEditor").IsVisible);
        Assert.True(Find<Button>(w, "MemoEditButton").IsVisible);
        Assert.False(Find<Button>(w, "MemoSaveButton").IsVisible);

        Find<Button>(w, "MemoEditButton").Command!.Execute(null);
        Flush();

        var editor = Find<TextBox>(w, "MemoEditor");
        Assert.True(editor.IsVisible);
        Assert.Equal("# 保存済み", editor.Text);
        Assert.True(editor.IsFocused);
        Assert.False(Find<Button>(w, "MemoEditButton").IsVisible);
        Assert.True(Find<Button>(w, "MemoSaveButton").IsVisible);
        Assert.True(Find<Button>(w, "MemoCancelButton").IsVisible);
    }

    [AvaloniaFact]
    public void Typing_makes_the_memo_dirty_and_the_save_key_saves_it()
    {
        var (w, vm) = Open();
        Find<Button>(w, "MemoEditButton").Command!.Execute(null);
        Flush();

        Find<TextBox>(w, "MemoEditor").Text = "書き換えた";
        Flush();
        Assert.True(vm.IsDirty);

        var command = Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
        w.KeyPress(Key.S, command == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control, PhysicalKey.S, "s");
        Flush();

        Assert.False(vm.IsEditing);
        Assert.False(vm.IsDirty);
        Assert.Equal("書き換えた", _store.Load(_project).Text);
    }

    [AvaloniaFact]
    public async Task Cancel_without_changes_goes_back_to_the_preview()
    {
        var (w, vm) = Open();
        Find<Button>(w, "MemoEditButton").Command!.Execute(null);
        Flush();

        await vm.CancelCommand.ExecuteAsync(null);
        Flush();

        Assert.False(Find<TextBox>(w, "MemoEditor").IsVisible);
        Assert.True(Find<Button>(w, "MemoEditButton").IsVisible);
    }
}
