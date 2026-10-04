using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>
/// Phase 18-0：共通化（計画 18-2）の前に、旧 ProjectEventStore の挙動を固定する特性テスト。
/// 共通化のあとも、構築部分以外は変えずに通ること。
/// </summary>
public sealed class ProjectEventStoreCharacterizationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-char-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<string> _logs = [];
    private readonly ProjectEventStore _store;
    private const string ProjectSlug = "c-work-proj";

    public ProjectEventStoreCharacterizationTests()
    {
        _paths = new AppPaths(Path.Combine(_dir, "data"));
        _store = new ProjectEventStore([new CursorSessionSource(new CursorAgent(), _paths, Root, log: _logs.Add,
            importer: new CursorTranscriptImporter(Path.Combine(_dir, "cursor")))], log: _logs.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static SessionKey Key(string conv) => new("cursor", conv);

    private void WriteHook(string conv, params string[] lines)
    {
        var path = _paths.EventFile("cursor", conv);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n")));
    }

    private void WriteTranscript(string conv, string prompt)
    {
        var dir = Path.Combine(_dir, "cursor", "projects", ProjectSlug, "agent-transcripts", conv);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, conv + ".jsonl"),
            "{\"role\":\"user\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"<user_query>\\n" + prompt + "\\n</user_query>\"}]}}\n" +
            "{\"role\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"返事\"}]}}\n");
    }

    // ① 作り直されたファイル

    [Fact]
    public void Recreated_file_for_another_project_removes_the_session()
    {
        WriteHook("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        _store.Refresh();
        Assert.Equal([Key("s1")], _store.Sessions);

        File.WriteAllText(_paths.EventFile("cursor", "s1"), Line("sessionStart", "", 0, "s1", root: @"C:\elsewhere") + "\n");
        var changed = _store.Refresh();

        Assert.Equal([Key("s1")], changed);
        Assert.Null(_store.GetSummary(Key("s1")));
        Assert.Empty(_store.Sessions);
    }

    [Fact]
    public void Recreated_empty_file_removes_the_session()
    {
        WriteHook("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        _store.Refresh();

        File.WriteAllText(_paths.EventFile("cursor", "s1"), "");
        var changed = _store.Refresh();

        Assert.Equal([Key("s1")], changed);
        Assert.Null(_store.GetSummary(Key("s1")));
        Assert.Empty(_store.Sessions);
        Assert.Empty(_store.Refresh());   // 消えたことは 1 回だけ伝える
    }

    [Fact]
    public void Recreated_file_with_a_matching_line_is_reported_once_and_replaces_the_events()
    {
        WriteHook("s1",
            Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"),
            Line("stop", "\"status\":\"completed\"", 1, "s1"));
        _store.Refresh();

        File.WriteAllText(_paths.EventFile("cursor", "s1"), Line("beforeSubmitPrompt", "\"prompt\":\"b\"", 0, "s1") + "\n");
        var changed = _store.Refresh();

        Assert.Equal([Key("s1")], changed);
        Assert.Equal([1L], _store.GetEvents(Key("s1")).Select(e => e.Seq));
        Assert.Equal(SessionState.Running, _store.GetSummary(Key("s1"))!.State);
    }

    // ② 取り込み済み → Hook に切り替わるのと、Hook の追記が同じ Refresh に来る

    [Fact]
    public void Switch_from_imported_to_hook_and_a_hook_append_in_one_refresh()
    {
        WriteHook("live", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "live"));
        WriteTranscript("old", "過去の依頼");
        _store.Refresh();
        Assert.Equal(SessionState.Imported, _store.GetSummary(Key("old"))!.State);

        WriteHook("old",
            Line("beforeSubmitPrompt", "\"prompt\":\"再開\"", 0, "old"),
            Line("stop", "\"status\":\"completed\"", 1, "old"));
        WriteHook("live", Line("stop", "\"status\":\"completed\"", 2, "live"));
        var changed = _store.Refresh();

        Assert.Equal(2, changed.Count);
        Assert.Contains(Key("old"), changed);
        Assert.Contains(Key("live"), changed);
        var old = _store.GetSummary(Key("old"))!;
        Assert.Equal(SessionState.YourTurn, old.State);
        Assert.Equal([1L, 2L], _store.GetEvents(Key("old")).Select(e => e.Seq));   // 取り込み分が混ざらない
        Assert.All(_store.GetEvents(Key("old")), e => Assert.False(e.Imported));
        Assert.Equal(SessionState.YourTurn, _store.GetSummary(Key("live"))!.State);
        Assert.Equal(2, _store.Sessions.Count());
    }

    // ③ 読み込み中の IOException

    [Fact]
    public void Locked_file_is_logged_and_read_again_next_time()
    {
        WriteHook("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        var path = _paths.EventFile("cursor", "s1");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var changed = _store.Refresh();   // 例外を外に出さない

            Assert.Empty(changed);
            Assert.Contains(_logs, l => l.Contains(path));
            Assert.Empty(_store.Sessions);
        }

        Assert.Equal([Key("s1")], _store.Refresh());
        Assert.Equal(SessionState.Running, _store.GetSummary(Key("s1"))!.State);
    }

    [Fact]
    public void Locked_file_does_not_stop_other_files_from_loading()
    {
        WriteHook("locked", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "locked"));
        WriteHook("free", Line("beforeSubmitPrompt", "\"prompt\":\"b\"", 0, "free"));

        using (new FileStream(_paths.EventFile("cursor", "locked"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal([Key("free")], _store.Refresh());
        }
    }

    // ④ 取り込み済みの ID と Hook のファイル名の大文字小文字が違う

    [Fact]
    public void Imported_session_is_switched_to_hook_even_when_the_id_differs_only_in_case()
    {
        WriteTranscript("ABC-1", "依頼");
        _store.Refresh();
        Assert.Equal(SessionState.Imported, _store.GetSummary(Key("ABC-1"))!.State);

        WriteHook("abc-1", Line("beforeSubmitPrompt", "\"prompt\":\"再開\"", 0, "abc-1"));
        var changed = _store.Refresh();

        // 取り込み済みの方は消え（Windows のファイル名は大文字小文字を区別しない）、Hook のセッションに切り替わる。
        Assert.Contains(Key("ABC-1"), changed);
        Assert.Contains(Key("abc-1"), changed);
        Assert.Equal([Key("abc-1")], _store.Sessions);
        Assert.Equal(SessionState.Running, _store.GetSummary(Key("abc-1"))!.State);
        Assert.Null(_store.GetSummary(Key("ABC-1")));
    }
}
