using System.Text.Json.Nodes;
using Miharikun.Core.Documents;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

public sealed class ProjectSettingsTests : IDisposable
{
    private const string Project = @"C:\work\proj";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-ps-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<string> _logs = [];
    private readonly ProjectSettingsStore _store;

    public ProjectSettingsTests()
    {
        _paths = new AppPaths(_dir);
        _store = new ProjectSettingsStore(_paths, _logs.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FileOf(string project) => _paths.ProjectSettingsFile(project);

    [Fact]
    public void File_name_is_a_slug_plus_an_8_digit_hash_under_projects()
    {
        var file = FileOf(Project);
        Assert.Equal(Path.Combine(_dir, "projects"), Path.GetDirectoryName(file));
        Assert.Matches(@"^c-work-proj-[0-9a-f]{8}\.json$", Path.GetFileName(file));
    }

    [Fact]
    public void Case_and_trailing_separator_do_not_change_the_file()
    {
        Assert.Equal(FileOf(Project), FileOf(@"c:\WORK\Proj\"));
        Assert.Equal(FileOf(Project), FileOf("C:/work/proj"));
    }

    [Fact]
    public void Different_paths_get_different_files_even_when_the_slug_is_empty_or_equal()
    {
        Assert.NotEqual(FileOf(@"C:\work\日本語A"), FileOf(@"C:\work\日本語B"));       // slug が同じ「c-work」になる
        Assert.NotEqual(FileOf(@"C:\work\a-b"), FileOf(@"C:\work\a\b"));
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = _store.Load(Project);
        Assert.Null(s.Ignore);
        Assert.Null(s.LastOpened);
        Assert.Equal(DefaultIgnore.Text, _store.IgnoreTextOrDefault(Project));
    }

    [Fact]
    public void Saved_ignore_text_round_trips_and_includes_the_path()
    {
        _store.SaveIgnore(Project, "a/\n*.draft.md");
        Assert.Equal("a/\n*.draft.md", _store.Load(Project).Ignore);
        Assert.Equal("a/\n*.draft.md", _store.IgnoreTextOrDefault(Project));

        var json = JsonNode.Parse(File.ReadAllText(FileOf(Project)))!.AsObject();
        Assert.Equal(1, json["v"]!.GetValue<int>());
        Assert.Equal(@"C:\work\proj", json["path"]!.GetValue<string>(), ignoreCase: true);
    }

    [Fact]
    public void An_empty_ignore_text_is_kept_and_does_not_fall_back_to_the_default()
    {
        _store.SaveIgnore(Project, "");
        Assert.Equal("", _store.Load(Project).Ignore);
        Assert.Equal("", _store.IgnoreTextOrDefault(Project));
    }

    [Fact]
    public void Saving_one_key_keeps_the_other_and_unknown_keys()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf(Project))!);
        File.WriteAllText(FileOf(Project), """{ "v": 1, "future": { "x": 1 }, "documents": { "ignore": "keep/", "extra": true } }""");

        _store.SaveLastOpened(Project, "docs/a.md");
        var json = JsonNode.Parse(File.ReadAllText(FileOf(Project)))!.AsObject();
        Assert.Equal("keep/", json["documents"]!["ignore"]!.GetValue<string>());
        Assert.Equal("docs/a.md", json["documents"]!["lastOpened"]!.GetValue<string>());
        Assert.True(json["documents"]!["extra"]!.GetValue<bool>());
        Assert.Equal(1, json["future"]!["x"]!.GetValue<int>());

        _store.SaveIgnore(Project, "new/");
        Assert.Equal("docs/a.md", _store.Load(Project).LastOpened);
    }

    [Fact]
    public void Saving_a_null_last_opened_removes_the_key()
    {
        _store.SaveLastOpened(Project, "a.md");
        _store.SaveLastOpened(Project, null);
        Assert.Null(_store.Load(Project).LastOpened);
    }

    [Fact]
    public void A_broken_file_gives_defaults_logs_it_and_can_be_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf(Project))!);
        File.WriteAllText(FileOf(Project), "{ not json");

        Assert.Null(_store.Load(Project).Ignore);
        Assert.NotEmpty(_logs);

        _store.SaveIgnore(Project, "x/");
        Assert.Equal("x/", _store.Load(Project).Ignore);
    }

    [Fact]
    public void Unexpected_value_types_are_treated_as_unset()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileOf(Project))!);
        File.WriteAllText(FileOf(Project), """{ "documents": { "ignore": 5, "lastOpened": [] } }""");
        var s = _store.Load(Project);
        Assert.Null(s.Ignore);
        Assert.Null(s.LastOpened);
    }

    [Fact]
    public void LastOpenedExisting_returns_null_when_the_file_is_gone()
    {
        var project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(Path.Combine(project, "docs"));
        File.WriteAllText(Path.Combine(project, "docs", "a.md"), "x");

        _store.SaveLastOpened(project, "docs/a.md");
        Assert.Equal("docs/a.md", _store.LastOpenedExisting(project));

        File.Delete(Path.Combine(project, "docs", "a.md"));
        Assert.Null(_store.LastOpenedExisting(project));
    }
}
