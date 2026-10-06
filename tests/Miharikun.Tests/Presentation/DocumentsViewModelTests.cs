using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.ViewModels;

namespace Miharikun.Tests.Presentation;

/// <summary>28-3：ドキュメントタブの、画面の仕組みを使う所（200ms のタイマー・既定のアプリ・ファイラー）。</summary>
public sealed class DocumentsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-docvm-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly FakeUiServices _ui = new();
    private readonly DocumentsViewModel _vm;

    public DocumentsViewModelTests()
    {
        _project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(Path.Combine(_project, "docs"));
        File.WriteAllText(Path.Combine(_project, "docs", "a.md"), "# a");
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        _vm = new DocumentsViewModel(_project, new ProjectSettingsStore(paths), paths, () => false,
            new ImmediateSynchronizationContext(), _ui);
    }

    public void Dispose()
    {
        _vm.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FullA => Path.Combine(_project, "docs", "a.md");

    private void SelectA()
    {
        _vm.Start();
        Assert.True(SpinWait.SpinUntil(() => !_vm.IsScanning, 10000), "走査が終わらない");
        _vm.OpenFromOutside("docs/a.md");
        Assert.NotNull(_vm.CurrentTarget);
    }

    [Fact]
    public void The_screen_refresh_is_a_200ms_ui_timer_that_is_stopped_on_Dispose()
    {
        var timer = _ui.TimerOf(TimeSpan.FromMilliseconds(200));
        timer.Start();

        _vm.Dispose();

        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public void Open_folder_reveals_the_selected_file_in_the_file_manager()
    {
        SelectA();

        _vm.OpenFolderCommand.Execute(null);

        Assert.Equal([FullA], _ui.Revealed);
    }

    [Fact]
    public void Open_external_opens_the_selected_file_with_the_default_app()
    {
        SelectA();

        _vm.OpenExternalCommand.Execute(null);

        Assert.Equal([FullA], _ui.Opened);
    }

    [Fact]
    public void Open_folder_and_open_external_do_nothing_without_a_selected_file()
    {
        _vm.OpenFolderCommand.Execute(null);
        _vm.OpenExternalCommand.Execute(null);

        Assert.Empty(_ui.Revealed);
        Assert.Empty(_ui.Opened);
    }

    [Fact]
    public void A_link_to_an_existing_file_outside_the_documents_is_opened_with_the_default_app_and_a_missing_one_is_ignored()
    {
        var other = Path.Combine(_dir, "other.txt");
        File.WriteAllText(other, "x");

        _vm.OpenLocalLink(other);
        _vm.OpenLocalLink(Path.Combine(_dir, "ない.txt"));

        Assert.Equal([other], _ui.Opened);
    }

    [Fact]
    public void Reload_raises_PreviewChanged_with_reload_set()
    {
        SelectA();
        var raised = new List<(Miharikun.ViewModels.PreviewSource? Source, bool Reload)>();
        _vm.PreviewChanged += (s, r) => raised.Add((s, r));

        _vm.ReloadPreviewCommand.Execute(null);

        var (source, reload) = Assert.Single(raised);
        Assert.True(reload);
        Assert.Equal(FullA, Assert.IsType<PreviewSource.File>(source).FullPath);
    }
}
