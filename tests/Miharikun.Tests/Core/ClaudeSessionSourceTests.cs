using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Phase 20-2：Claude Code の会話ログを、追記分だけ読んで差分にする（要件 9.1・計画 8.1）。</summary>
public sealed class ClaudeSessionSourceTests : IDisposable
{
    private const string Project = @"C:\work\proj";
    private const string Main = "C--work-proj";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "miharikun-claudesrc-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = [];
    private readonly ClaudeSessionSource _source;

    public ClaudeSessionSourceTests()
    {
        _source = new ClaudeSessionSource(Project, ClaudeDir, _logs.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string ClaudeDir => Path.Combine(_root, ".claude");
    private string ProjectsDir => Path.Combine(ClaudeDir, "projects");

    private string Folder(string name)
    {
        var dir = Path.Combine(ProjectsDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string File_(string folder, string session) => Path.Combine(Folder(folder), session + ".jsonl");

    private void Append(string folder, string session, params string[] lines) =>
        File.AppendAllText(File_(folder, session), string.Concat(lines.Select(l => l + "\n")));

    private static ClaudeLogBuilder B(string session, string cwd = Project) => new(session, cwd);

    private static SessionKey Key(string session) => new("claude", session);

    private static SessionDelta Only(IReadOnlyList<SessionDelta> deltas) => Assert.Single(deltas);

    // ---- 初回・追記 ----

    [Fact]
    public void The_first_read_gives_every_existing_session_as_an_append_keyed_by_the_file_name()
    {
        var a = B("sess-a");
        Append(Main, "sess-a", a.User("依頼A"), a.AssistantText("返事A", stopReason: "end_turn"));
        var b = B("sess-b");
        Append(Main, "sess-b", b.User("依頼B"));

        var deltas = _source.ReadNew();

        Assert.Equal(2, deltas.Count);
        Assert.All(deltas, d => Assert.Equal(SessionDeltaKind.Append, d.Kind));
        var da = deltas.Single(d => d.Key == Key("sess-a"));
        Assert.Equal([AgentEventKind.SessionStarted, AgentEventKind.PromptSubmitted, AgentEventKind.AssistantMessage, AgentEventKind.TurnEnded],
            da.Events.Select(e => e.Kind));
        Assert.Equal([1L, 1L, 2L, 2L], da.Events.Select(e => e.Seq));   // 行番号
        Assert.Equal(Key("sess-a"), da.Events[0].Session);
    }

    [Fact]
    public void Nothing_is_returned_when_nothing_changed()
    {
        Append(Main, "s", B("s").User("a"));
        _source.ReadNew();

        Assert.Empty(_source.ReadNew());
    }

    [Fact]
    public void Appended_lines_come_as_an_append_and_the_converter_keeps_its_state_between_reads()
    {
        var b = B("s");
        Append(Main, "s", b.User("a"), b.Bash("t1", "dotnet test"));
        _source.ReadNew();

        Append(Main, "s", b.ToolResult("t1", "Exit code 1\nFailed", isError: true));
        var delta = Only(_source.ReadNew());

        Assert.Equal(SessionDeltaKind.Append, delta.Kind);
        var e = Assert.Single(delta.Events);
        Assert.Equal(AgentEventKind.ToolSucceeded, e.Kind);
        Assert.Equal("dotnet test", e.Command);     // 前の読み込みの呼び出しと対応が付く
        Assert.Equal(1, e.ExitCode);
        Assert.Equal(3, e.Seq);
        Assert.NotNull(e.Duration);
    }

    [Fact]
    public void A_partly_written_last_line_is_read_once_it_is_finished()
    {
        var b = B("s");
        var line = b.User("途中まで");
        var path = File_(Main, "s");
        File.WriteAllText(path, line[..40]);

        Assert.Empty(_source.ReadNew());

        File.AppendAllText(path, line[40..] + "\n");
        var delta = Only(_source.ReadNew());
        Assert.Contains(delta.Events, e => e.Kind == AgentEventKind.PromptSubmitted && e.Text == "途中まで");
    }

    [Fact]
    public void Broken_lines_are_skipped_and_logged_and_numbering_continues()
    {
        var b = B("s");
        Append(Main, "s", b.User("a"), "{ broken", b.User("b"));

        var delta = Only(_source.ReadNew());

        Assert.Equal([1L, 3L], delta.Events.Where(e => e.Kind == AgentEventKind.PromptSubmitted).Select(e => e.Seq));
        Assert.Contains(_logs, l => l.Contains("s.jsonl:2"));
    }

    [Fact]
    public void The_format_logs_are_flushed_after_each_read()
    {
        var b = B("s");
        var bad = """{"type":"user","timestamp":"2026-10-03T02:00:02.000Z","message":{"content":42},"cwd":"C:\\work\\proj"}""";
        Append(Main, "s", b.User("a"), bad, bad, bad, b.Other("brand-new-kind"));

        _source.ReadNew();

        Assert.Contains(_logs, l => l.Contains("ほかに 2 件"));
        Assert.Contains(_logs, l => l.Contains("brand-new-kind"));
        Assert.Contains(_logs, l => l.Contains("初めて見る version"));
    }

    // ---- 作り直し・消えた ----

    [Fact]
    public void A_recreated_file_replaces_the_session_with_only_the_new_content_and_a_fresh_converter()
    {
        var b = B("s");
        Append(Main, "s", b.User("古い"), b.AssistantText("古い返事"), b.User("もう一つ"));
        _source.ReadNew();

        var fresh = B("s");
        File.WriteAllText(File_(Main, "s"), fresh.User("新しい") + "\n");
        var delta = Only(_source.ReadNew());

        Assert.Equal(SessionDeltaKind.Replace, delta.Kind);
        Assert.Equal([AgentEventKind.SessionStarted, AgentEventKind.PromptSubmitted], delta.Events.Select(e => e.Kind));   // SessionStarted が出し直される
        Assert.Equal("新しい", delta.Events[1].Text);
        Assert.All(delta.Events, e => Assert.Equal(1, e.Seq));
    }

    [Fact]
    public void A_file_recreated_empty_or_for_another_project_removes_the_session()
    {
        Append(Main, "e", B("e").User("a"));
        var o = B("o");
        Append(Main, "o", o.User("a"), o.User("b"), o.User("c"));   // 作り直したあとの内容より長い（短くなったことで、作り直しに気づく）
        _source.ReadNew();

        File.WriteAllText(File_(Main, "e"), "");
        File.WriteAllText(File_(Main, "o"), B("o", @"C:\work\elsewhere").User("別のプロジェクト") + "\n");
        var deltas = _source.ReadNew();

        Assert.Equal(2, deltas.Count);
        Assert.All(deltas, d => Assert.Equal(SessionDeltaKind.Remove, d.Kind));
        Assert.Empty(_source.ReadNew());   // 1 回だけ
    }

    [Fact]
    public void A_deleted_file_removes_the_session_once()
    {
        Append(Main, "s", B("s").User("a"));
        _source.ReadNew();

        File.Delete(File_(Main, "s"));
        var delta = Only(_source.ReadNew());

        Assert.Equal((SessionDeltaKind.Remove, Key("s")), (delta.Kind, delta.Key));
        Assert.Empty(_source.ReadNew());
    }

    [Fact]
    public void A_deleted_folder_removes_its_sessions()
    {
        Append("C--work-proj--claude-worktrees-w1", "s", B("s", Project + @"\.claude\worktrees\w1").User("a"));
        _source.ReadNew();

        Directory.Delete(Path.Combine(ProjectsDir, "C--work-proj--claude-worktrees-w1"), true);

        Assert.Equal(SessionDeltaKind.Remove, Only(_source.ReadNew()).Kind);
    }

    [Fact]
    public void A_deleted_session_that_came_back_is_read_from_the_start()
    {
        Append(Main, "s", B("s").User("a"));
        _source.ReadNew();
        File.Delete(File_(Main, "s"));
        _source.ReadNew();

        Append(Main, "s", B("s").User("戻ってきた"));
        var delta = Only(_source.ReadNew());

        Assert.Equal(SessionDeltaKind.Append, delta.Kind);
        Assert.Contains(delta.Events, e => e.Text == "戻ってきた");
    }

    [Fact]
    public void A_file_that_is_locked_right_now_is_logged_and_read_next_time_without_removing_others()
    {
        Append(Main, "locked", B("locked").User("a"));
        Append(Main, "free", B("free").User("b"));
        _source.ReadNew();
        Append(Main, "locked", B("locked").User("追記"));

        using (new FileStream(File_(Main, "locked"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(_source.ReadNew());   // 例外を外に出さない・何も消えない
            Assert.Contains(_logs, l => l.Contains("locked.jsonl"));
        }

        Assert.Contains(Only(_source.ReadNew()).Events, e => e.Text == "追記");
    }

    // ---- 照合 ----

    [Fact]
    public void A_session_of_another_project_is_never_returned_even_when_it_grows()
    {
        var other = B("o", @"C:\work\elsewhere");
        Append(Main, "o", other.User("よそ"));
        Assert.Empty(_source.ReadNew());

        Append(Main, "o", other.User("もっと"));
        Assert.Empty(_source.ReadNew());
        Assert.Empty(_logs);   // 他のプロジェクトの会話は、壊れた行もログに出さない
    }

    [Fact]
    public void Lines_before_the_first_cwd_are_kept_and_converted_once_the_cwd_appears()
    {
        var path = File_(Main, "s");
        File.WriteAllText(path, """{"type":"file-history-snapshot","messageId":"m"}""" + "\n");
        Assert.Empty(_source.ReadNew());

        File.AppendAllText(path, B("s").User("やっと") + "\n");
        var delta = Only(_source.ReadNew());

        Assert.Equal(SessionDeltaKind.Append, delta.Kind);
        Assert.Equal(2, delta.Events[0].Seq);   // 行番号は、先頭の行も数える
        Assert.Contains(delta.Events, e => e.Text == "やっと");
    }

    [Fact]
    public void A_file_without_any_cwd_for_a_long_time_is_given_up_on()
    {
        var path = File_(Main, "s");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("""{"type":"attachment"}""" + "\n", 250)));
        Assert.Empty(_source.ReadNew());

        File.AppendAllText(path, B("s").User("遅すぎ") + "\n");

        Assert.Empty(_source.ReadNew());
    }

    [Fact]
    public void Files_in_a_worktree_folder_match_when_their_cwd_is_under_the_projects_worktrees()
    {
        var wt = Project + @"\.claude\worktrees\w1";
        Append("C--work-proj--claude-worktrees-w1", "wt", B("wt", wt).User("作業ツリーで"));
        Append("C--work-proj--claude-worktrees-w1", "bad", B("bad", @"C:\work\other\.claude\worktrees\w1").User("別"));

        var delta = Only(_source.ReadNew());

        Assert.Equal(Key("wt"), delta.Key);
    }

    [Fact]
    public void A_worktree_folder_that_appears_later_is_picked_up()
    {
        Append(Main, "s", B("s").User("a"));
        _source.ReadNew();
        Assert.Empty(_source.ReadNew());

        Append("C--work-proj--claude-worktrees-w1", "w", B("w", Project + @"\.claude\worktrees\w1").User("あとから"));

        Assert.Equal(Key("w"), Only(_source.ReadNew()).Key);
    }

    [Fact]
    public void Only_the_files_directly_in_the_folder_are_read()
    {
        Append(Main, "s", B("s").User("a"));
        var sub = Path.Combine(Folder(Main), "s", "subagents");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "agent-1.jsonl"), B("agent-1").User("サブエージェント") + "\n");

        var delta = Only(_source.ReadNew());

        Assert.Equal(Key("s"), delta.Key);
    }

    [Fact]
    public void Other_extensions_and_unrelated_folders_are_not_read()
    {
        Append(Main, "s", B("s").User("a"));
        File.WriteAllText(Path.Combine(Folder(Main), "agent-name.meta.json"), "{}");
        Append("C--work-proj-extra", "x", B("x").User("名前が似ているだけ"));
        Append("C--work-other", "y", B("y").User("別"));

        Assert.Equal(Key("s"), Only(_source.ReadNew()).Key);
    }

    // ---- .claude には何も書かない ----

    private List<string> Snapshot() =>
        !Directory.Exists(_root)
            ? []
            : [.. Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
                .Select(p => p + "|" + (File.Exists(p) ? new FileInfo(p).Length + "|" + File.GetLastWriteTimeUtc(p).Ticks : "dir"))
                .Order()];

    [Fact]
    public void Nothing_is_ever_created_or_changed_under_the_claude_dir()
    {
        var before = Snapshot();
        Assert.Empty(before);   // .claude が無い状態から

        _source.ReadNew();
        _ = _source.WatchTargets;
        Assert.False(Directory.Exists(ClaudeDir));

        Append(Main, "s", B("s").User("a"));
        var written = Snapshot();
        _source.ReadNew();
        _ = _source.WatchTargets;
        Assert.Equal(written, Snapshot());
    }

    // ---- 監視先 ----

    [Fact]
    public void Watch_targets_are_the_candidate_folders_plus_the_expected_one_and_never_create_folders()
    {
        var targets = _source.WatchTargets;
        var expected = Assert.Single(targets);
        Assert.Equal(Path.Combine(ProjectsDir, Main), expected.Directory);
        Assert.Equal("*.jsonl", expected.Filter);
        Assert.False(expected.CreateIfMissing);

        Folder("C--work-proj--claude-worktrees-w1");
        Folder("C--work-other");

        var after = _source.WatchTargets.Select(t => Path.GetFileName(t.Directory)).Order().ToList();
        Assert.Equal([Main, "C--work-proj--claude-worktrees-w1"], after);
        Assert.All(_source.WatchTargets, t => Assert.False(t.CreateIfMissing));
    }

    // ---- 候補が無いときの探索 ----

    [Fact]
    public void When_there_are_no_candidates_the_folders_are_searched_by_cwd_once()
    {
        Append("odd-name", "s", B("s").User("名前の規則が違うフォルダ"));

        var delta = Only(_source.ReadNew());

        Assert.Equal(Key("s"), delta.Key);
        Assert.Contains(_logs, l => l.Contains("フォルダ名の規則"));
        Assert.Contains(_source.WatchTargets, t => Path.GetFileName(t.Directory) == "odd-name");   // 見つけたフォルダは監視する

        Append("odd-name", "s", B("s").User("追記も読まれる"));
        Assert.Contains(Only(_source.ReadNew()).Events, e => e.Text == "追記も読まれる");

        Append("odd-name-2", "t", B("t").User("2 回目の探索はしない"));
        Assert.Empty(_source.ReadNew());
    }

    [Fact]
    public void Asking_for_watch_targets_does_not_run_the_search_by_cwd()
    {
        // 監視先の取り直しは UI スレッドからも呼ばれる。ファイルを読む探索は、ReadNew だけで行う。
        Append("odd-name", "s", B("s").User("a"));

        Assert.DoesNotContain(_source.WatchTargets, t => Path.GetFileName(t.Directory) == "odd-name");
        Assert.Empty(_logs);

        _source.ReadNew();
        Assert.Contains(_source.WatchTargets, t => Path.GetFileName(t.Directory) == "odd-name");
    }

    [Fact]
    public void The_search_by_cwd_does_not_run_when_there_is_a_candidate_folder()
    {
        Folder(Main);
        Append("odd-name", "s", B("s").User("a"));

        Assert.Empty(_source.ReadNew());
        Assert.Empty(_logs);
    }

    [Fact]
    public void The_search_by_cwd_is_not_used_up_while_the_projects_folder_does_not_exist()
    {
        Assert.Empty(_source.ReadNew());   // .claude\projects が無い（Claude Code を使っていない）

        Append("odd-name", "s", B("s").User("あとからできた"));

        Assert.Equal(Key("s"), Only(_source.ReadNew()).Key);
    }

    // ---- Store を通して ----

    [Fact]
    public void Through_the_store_a_claude_session_gets_its_summary_and_updates()
    {
        var b = B("s");
        Append(Main, "s", b.User("テストして"), b.Bash("t1", "dotnet test"));
        var store = new ProjectEventStore([_source], log: _logs.Add);

        Assert.Equal([Key("s")], store.Refresh());
        var running = store.GetSummary(Key("s"))!;
        Assert.Equal(SessionState.Running, running.State);
        Assert.Equal("テストして", running.AutoTitle);
        Assert.Equal("main", running.Branch);

        Append(Main, "s", b.ToolResult("t1", "合格"), b.AssistantText("通りました", stopReason: "end_turn"));
        Assert.Equal([Key("s")], store.Refresh());
        var done = store.GetSummary(Key("s"))!;
        Assert.Equal(SessionState.YourTurn, done.State);
        Assert.Equal(0, Assert.Single(done.TestRuns).ExitCode);
    }

    [Fact]
    public void Subagent_events_are_not_available_yet()
    {
        Assert.Empty(_source.GetSubagentEvents(Key("s"), "agent-1"));
    }
}
