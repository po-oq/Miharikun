using Miharikun.Core.Memo;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

public sealed class ProjectMemoStoreTests : IDisposable
{
    private const string Project = @"C:\work\proj";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-memo-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<string> _logs = [];
    private readonly ProjectMemoStore _store;

    public ProjectMemoStoreTests()
    {
        _paths = new AppPaths(_dir);
        _store = new ProjectMemoStore(_paths, _logs.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FileOf(string project) => _paths.ProjectMemoFile(project);

    [Fact]
    public void File_name_shares_the_prefix_of_the_settings_json()
    {
        var memo = FileOf(Project);
        var json = _paths.ProjectSettingsFile(Project);
        Assert.Equal(Path.GetDirectoryName(json), Path.GetDirectoryName(memo));
        Assert.Equal(Path.GetFileNameWithoutExtension(json) + ".memo.md", Path.GetFileName(memo));
        Assert.Matches(@"^c-work-proj-[0-9a-f]{8}\.memo\.md$", Path.GetFileName(memo));
    }

    [Fact]
    public void Case_and_trailing_separator_do_not_change_the_file()
    {
        Assert.Equal(FileOf(Project), FileOf(@"c:\WORK\Proj\"));
    }

    [Fact]
    public void Different_projects_get_different_files()
    {
        Assert.NotEqual(FileOf(@"C:\work\a"), FileOf(@"C:\work\b"));
    }

    [Fact]
    public void Missing_file_loads_as_empty_with_no_timestamp()
    {
        var memo = _store.Load(Project);
        Assert.Equal("", memo.Text);
        Assert.Null(memo.LastWriteTime);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        _store.Save(Project, "# メモ\n- [ ] 1\n");
        Assert.Equal("# メモ\n- [ ] 1\n", _store.Load(Project).Text);
    }

    [Fact]
    public void Save_overwrites()
    {
        _store.Save(Project, "古い");
        _store.Save(Project, "新しい");
        Assert.Equal("新しい", _store.Load(Project).Text);
    }

    [Fact]
    public void Save_creates_the_folder()
    {
        Assert.False(Directory.Exists(_paths.ProjectsDir));
        _store.Save(Project, "x");
        Assert.True(File.Exists(FileOf(Project)));
    }

    [Fact]
    public void Save_writes_utf8_without_bom_and_keeps_newlines_as_given()
    {
        _store.Save(Project, "あ\r\nい\n");
        var bytes = File.ReadAllBytes(FileOf(Project));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("あ\r\nい\n", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void File_with_bom_is_read_without_the_bom()
    {
        Directory.CreateDirectory(_paths.ProjectsDir);
        File.WriteAllText(FileOf(Project), "あいう", new System.Text.UTF8Encoding(true));
        Assert.Equal("あいう", _store.Load(Project).Text);
    }

    [Fact]
    public void Empty_text_can_be_saved_as_an_empty_file()
    {
        _store.Save(Project, "x");
        _store.Save(Project, "");
        Assert.True(File.Exists(FileOf(Project)));
        Assert.Equal("", _store.Load(Project).Text);
        Assert.NotNull(_store.Load(Project).LastWriteTime);
    }

    [Fact]
    public void Save_returns_the_timestamp_that_load_reports()
    {
        var written = _store.Save(Project, "x");
        Assert.Equal(written, _store.Load(Project).LastWriteTime);
    }

    [Fact]
    public void Existing_but_unreadable_file_throws_instead_of_loading_as_empty()
    {
        _store.Save(Project, "大事なメモ");
        // 他から読み書きできないように開いておく
        using var _ = new FileStream(FileOf(Project), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => _store.Load(Project));
    }

    [Fact]
    public void File_being_written_by_another_with_share_is_still_readable()
    {
        _store.Save(Project, "内容");
        using var _ = new FileStream(FileOf(Project), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal("内容", _store.Load(Project).Text);
    }

    [Fact]
    public void Save_does_not_touch_the_settings_json_next_to_it()
    {
        var settings = new ProjectSettingsStore(_paths, _logs.Add);
        var json = _paths.ProjectSettingsFile(Project);
        Directory.CreateDirectory(_paths.ProjectsDir);
        File.WriteAllText(json, "{\"keep\":1}");

        _store.Save(Project, "メモ");

        Assert.Equal("{\"keep\":1}", File.ReadAllText(json));
        Assert.Equal("メモ", _store.Load(Project).Text);
    }

    [Fact]
    public void Save_failure_is_thrown_to_the_caller()
    {
        _store.Save(Project, "a");
        using var _ = new FileStream(FileOf(Project), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => _store.Save(Project, "b"));
    }
}
