using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class ProjectEventStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-store-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<string> _logs = [];
    private readonly ProjectEventStore _store;

    public ProjectEventStoreTests()
    {
        _paths = new AppPaths(_dir);
        _store = new ProjectEventStore(new CursorAgent(), _paths, Root, log: _logs.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static SessionKey Key(string conv) => new("cursor", conv);

    private void Write(string conv, params string[] lines)
    {
        var path = _paths.EventFile("cursor", conv);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n")));
    }

    [Fact]
    public void Empty_or_missing_events_dir_is_fine()
    {
        Assert.Empty(_store.Refresh());
        Assert.Empty(_store.Sessions);
    }

    [Fact]
    public void Loads_matching_sessions_and_ignores_other_projects_and_app_file()
    {
        Write("mine", Line("beforeSubmitPrompt", "\"prompt\":\"hi\"", 0, "mine"));
        Write("other", Line("beforeSubmitPrompt", "\"prompt\":\"hi\"", 0, "other", root: @"C:\elsewhere"));
        Write("_app", Line("sessionStart", "", 0, "_app"));

        var changed = _store.Refresh();

        Assert.Equal([Key("mine")], changed);
        Assert.Equal([Key("mine")], _store.Sessions);
        Assert.Equal(SessionState.Running, _store.GetSummary(Key("mine"))!.State);
        Assert.Null(_store.GetSummary(Key("other")));
    }

    [Fact]
    public void Matches_case_insensitively_and_with_multi_root()
    {
        Write("a", Line("stop", "\"status\":\"completed\"", 0, "a", root: @"c:\WORK\PROJ"));

        Assert.Single(_store.Refresh());
    }

    [Fact]
    public void Picks_up_appended_events_and_reports_only_changed_sessions()
    {
        Write("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        Write("s2", Line("beforeSubmitPrompt", "\"prompt\":\"b\"", 0, "s2"));
        _store.Refresh();
        Assert.Empty(_store.Refresh());   // 変化なし

        Write("s1", Line("stop", "\"status\":\"completed\"", 5, "s1"));
        var changed = _store.Refresh();

        Assert.Equal([Key("s1")], changed);
        Assert.Equal(SessionState.YourTurn, _store.GetSummary(Key("s1"))!.State);
        Assert.Equal(SessionState.Running, _store.GetSummary(Key("s2"))!.State);
        Assert.Equal([1L, 2L], _store.GetEvents(Key("s1")).Select(e => e.Seq));
    }

    [Fact]
    public void Broken_lines_are_skipped_logged_and_still_numbered()
    {
        Write("s1",
            Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"),
            "{ this is not json",
            "{\"v\":1}",
            "",
            Line("stop", "\"status\":\"completed\"", 5, "s1"));

        _store.Refresh();

        var events = _store.GetEvents(Key("s1"));
        Assert.Equal([1L, 5L], events.Select(e => e.Seq));
        Assert.Equal(3, _logs.Count);
        Assert.Equal(SessionState.YourTurn, _store.GetSummary(Key("s1"))!.State);
    }

    [Fact]
    public void Partially_written_last_line_is_picked_up_once_finished()
    {
        var path = _paths.EventFile("cursor", "s1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var full = Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1");
        File.WriteAllText(path, full[..30]);

        Assert.Empty(_store.Refresh());
        Assert.Empty(_logs);

        File.AppendAllText(path, full[30..] + "\n");
        Assert.Equal([Key("s1")], _store.Refresh());
    }

    [Fact]
    public void Session_resumed_after_end_returns_to_its_turn_state()
    {
        Write("s1",
            Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"),
            Line("stop", "\"status\":\"completed\"", 1, "s1"),
            Line("sessionEnd", "\"reason\":\"user_close\"", 2, "s1"));
        _store.Refresh();
        Assert.Equal(SessionState.Closed, _store.GetSummary(Key("s1"))!.State);

        Write("s1", Line("beforeSubmitPrompt", "\"prompt\":\"b\"", 3, "s1"));
        _store.Refresh();

        Assert.Equal(SessionState.Running, _store.GetSummary(Key("s1"))!.State);
    }

    [Fact]
    public void Session_whose_match_appears_later_is_loaded_from_its_first_line()
    {
        Write("s1", Line("sessionStart", "", 0, "s1", root: @"C:\elsewhere"));
        Assert.Empty(_store.Refresh());

        Write("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 1, "s1"));
        Assert.Equal([Key("s1")], _store.Refresh());
        Assert.Equal([1L, 2L], _store.GetEvents(Key("s1")).Select(e => e.Seq));
    }

    [Fact]
    public void Truncated_file_drops_old_events_and_is_reloaded()
    {
        Write("s1",
            Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"),
            Line("stop", "\"status\":\"completed\"", 1, "s1"),
            Line("beforeSubmitPrompt", "\"prompt\":\"b\"", 2, "s1"));
        _store.Refresh();

        File.WriteAllText(_paths.EventFile("cursor", "s1"), Line("sessionStart", "", 0, "s1") + "\n");
        _store.Refresh();

        Assert.Single(_store.GetEvents(Key("s1")));
        Assert.Equal(SessionState.YourTurn, _store.GetSummary(Key("s1"))!.State);
    }

    [Fact]
    public void Git_snapshot_survives_the_file_round_trip()
    {
        Write("s1", Line("stop", "\"status\":\"completed\"", 0, "s1", git: new GitSnapshot("feature/x", "abc123")));
        _store.Refresh();

        var s = _store.GetSummary(Key("s1"))!;
        Assert.Equal(("feature/x", "abc123"), (s.Branch, s.LatestHead));
    }

    [Fact]
    public void Handles_the_scale_in_the_requirements()
    {
        // 要件 13章の規模：1プロジェクト100セッション × 1セッション5,000イベントで起動3秒以内。
        for (var s = 0; s < 100; s++)
        {
            var conv = "s" + s;
            var lines = new List<string> { Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, conv) };
            for (var i = 0; i < 4998; i++)
                lines.Add(Line("postToolUse", "\"tool_use_id\":\"t" + i + "\",\"tool_name\":\"Read\"", i, conv));
            lines.Add(Line("stop", "\"status\":\"completed\"", 3000, conv));
            Write(conv, [.. lines]);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _store.Refresh();
        foreach (var key in _store.Sessions.ToList())
            _store.GetSummary(key);
        sw.Stop();

        Assert.Equal(100, _store.Sessions.Count());
        Assert.Equal(4998, _store.GetSummary(Key("s0"))!.ToolCallCount);
        Console.WriteLine($"scale: {sw.Elapsed}");   // 参考値\n        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{sw.Elapsed}");
    }
}