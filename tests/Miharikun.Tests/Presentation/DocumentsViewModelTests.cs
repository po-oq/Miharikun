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
    public void A_link_to_an_openable_file_outside_the_documents_is_opened_a_folder_is_revealed_and_a_missing_one_is_ignored()
    {
        var other = Path.Combine(_dir, "other.txt");
        File.WriteAllText(other, "x");
        var folder = Path.Combine(_dir, "folder");
        Directory.CreateDirectory(folder);

        _vm.OpenLocalLink(other);
        _vm.OpenLocalLink(folder);
        _vm.OpenLocalLink(Path.Combine(_dir, "ない.txt"));

        Assert.Equal([other], _ui.Opened);
        Assert.Equal([folder], _ui.Revealed);
    }

    [Fact]
    public void Files_that_could_start_a_program_are_only_revealed_never_opened()
    {
        // 中身はただの文字（拡張子だけの試験用）
        foreach (var name in new[] { "run.bat", "run.command", "run.exe" })
            File.WriteAllText(Path.Combine(_dir, name), "echo hi");

        foreach (var name in new[] { "run.bat", "run.command", "run.exe" })
            _vm.OpenLocalLink(Path.Combine(_dir, name));

        Assert.Empty(_ui.Opened);
        Assert.Equal(3, _ui.Revealed.Count);
    }

    [Theory]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData("//server/share/x.txt")]
    public void A_network_form_of_path_is_dropped_before_anything_is_looked_at(string path)
    {
        _vm.OpenLocalLink(path);

        Assert.Empty(_ui.Opened);
        Assert.Empty(_ui.Revealed);
    }

    [MacFact]
    public void An_md_outside_the_index_that_is_a_link_to_a_command_file_is_revealed_when_asked_from_the_memo()
    {
        // 索引は、リンクのフォルダの中に入らない。<対象>/linkdir/x.md → run.command が、索引に無いまま開かれてはならない
        var real = Path.Combine(_dir, "real");
        Directory.CreateDirectory(real);
        var command = Path.Combine(real, "run.command");
        File.WriteAllText(command, "echo hi");
        File.CreateSymbolicLink(Path.Combine(real, "x.md"), command);                       // 名前は .md、中身は実行形式へのリンク
        Directory.CreateSymbolicLink(Path.Combine(_project, "linkdir"), real);              // リンクのフォルダ（索引は中に入らない）
        _vm.Start();
        Assert.True(SpinWait.SpinUntil(() => !_vm.IsScanning, 10000), "走査が終わらない");

        _vm.OpenFromOutside("linkdir/x.md");

        Assert.Empty(_ui.Opened);
        Assert.Equal([Path.Combine(_project, "linkdir", "x.md")], _ui.Revealed);
    }

    [MacFact]
    public void The_open_external_button_reveals_instead_of_opening_when_the_selected_md_links_to_a_command_file()
    {
        var command = Path.Combine(_dir, "run.command");
        File.WriteAllText(command, "echo hi");
        File.CreateSymbolicLink(Path.Combine(_project, "docs", "evil.md"), command);
        _vm.Start();
        Assert.True(SpinWait.SpinUntil(() => !_vm.IsScanning, 10000), "走査が終わらない");
        _vm.OpenFromOutside("docs/evil.md");
        Assert.NotNull(_vm.CurrentTarget);

        _vm.OpenExternalCommand.Execute(null);

        Assert.Empty(_ui.Opened);
        Assert.Equal([Path.Combine(_project, "docs", "evil.md")], _ui.Revealed);
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
