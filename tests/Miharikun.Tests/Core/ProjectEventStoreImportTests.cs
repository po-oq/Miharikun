using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>Phase 9：導入前の過去セッション（transcript）の取り込みと、hook のイベントとの切り替え。</summary>
public sealed class ProjectEventStoreImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-import-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ProjectEventStore _store;
    private const string ProjectSlug = "c-work-proj";

    public ProjectEventStoreImportTests()
    {
        _paths = new AppPaths(Path.Combine(_dir, "data"));
        _store = new ProjectEventStore([new CursorSessionSource(new CursorAgent(), _paths, Root,
            importer: new CursorTranscriptImporter(Path.Combine(_dir, "cursor")))]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static SessionKey Key(string conv) => new("cursor", conv);

    private void WriteTranscript(string conv, string prompt)
    {
        var dir = Path.Combine(_dir, "cursor", "projects", ProjectSlug, "agent-transcripts", conv);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, conv + ".jsonl"),
            "{\"role\":\"user\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"<user_query>\\n" + prompt + "\\n</user_query>\"}]}}\n" +
            "{\"role\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"返事\"}]}}\n");
    }

    private void WriteHook(string conv, params string[] lines)
    {
        var path = _paths.EventFile("cursor", conv);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n")));
    }

    [Fact]
    public void Past_sessions_appear_as_imported_with_a_title_and_searchable_prompts()
    {
        WriteTranscript("old", "過去の依頼");

        var changed = _store.Refresh();

        Assert.Equal([Key("old")], changed);
        Assert.Equal([Key("old")], _store.Sessions);
        var s = _store.GetSummary(Key("old"))!;
        Assert.Equal(SessionState.Imported, s.State);
        Assert.Equal("過去の依頼", s.AutoTitle);
        Assert.Equal(2, _store.GetEvents(Key("old")).Count);
    }

    [Fact]
    public void Sessions_that_already_have_hook_events_are_not_imported()
    {
        WriteTranscript("both", "依頼");
        WriteHook("both", Line("beforeSubmitPrompt", "\"prompt\":\"依頼\"", 0, "both"));

        _store.Refresh();

        Assert.Equal(SessionState.Running, _store.GetSummary(Key("both"))!.State);
        Assert.Single(_store.GetEvents(Key("both")));
    }

    [Fact]
    public void A_transcript_is_imported_once_and_not_reported_again()
    {
        WriteTranscript("old", "依頼");
        _store.Refresh();

        Assert.Empty(_store.Refresh());
    }

    [Fact]
    public void Hook_events_appearing_later_replace_the_imported_session()
    {
        WriteTranscript("old", "依頼");
        _store.Refresh();

        WriteHook("old", Line("beforeSubmitPrompt", "\"prompt\":\"再開\"", 0, "old"));
        var changed = _store.Refresh();

        Assert.Equal([Key("old")], changed);
        Assert.Equal(SessionState.Running, _store.GetSummary(Key("old"))!.State);
        Assert.Equal([Key("old")], _store.Sessions);
    }

    [Fact]
    public void Imported_session_is_dropped_when_hook_events_for_it_belong_to_another_project()
    {
        WriteTranscript("old", "依頼");
        _store.Refresh();

        WriteHook("old", Line("beforeSubmitPrompt", "\"prompt\":\"x\"", 0, "old", root: @"C:\elsewhere"));
        var changed = _store.Refresh();

        Assert.Equal([Key("old")], changed);
        Assert.Null(_store.GetSummary(Key("old")));
        Assert.Empty(_store.Sessions);
    }

    [Fact]
    public void Without_an_importer_nothing_is_imported()
    {
        WriteTranscript("old", "依頼");
        var plain = new ProjectEventStore([new CursorSessionSource(new CursorAgent(), _paths, Root)]);

        Assert.Empty(plain.Refresh());
    }
}
