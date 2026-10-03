using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class SessionAnalyzerTests
{
    private static readonly SessionKey Key = new("cursor", "conv-1");

    private static SessionSummary Analyze(params (string, string, int)[] items) =>
        SessionAnalyzer.Analyze(Key, Events(items));

    private static string Prompt(string text) => $"\"prompt\":\"{text}\"";
    private static string Stop(string status) => $"\"status\":\"{status}\"";
    private static string Tool(string id, string name = "Shell", string? cmd = null) =>
        $"\"tool_use_id\":\"{id}\",\"tool_name\":\"{name}\"" + (cmd is null ? "" : $",\"tool_input\":{{\"command\":\"{cmd}\"}}");

    [Fact]
    public void Only_session_start_is_your_turn()
    {
        Assert.Equal(SessionState.YourTurn, Analyze(E("sessionStart")).State);
    }

    [Fact]
    public void Prompt_without_stop_is_running()
    {
        Assert.Equal(SessionState.Running, Analyze(E("sessionStart"), E("beforeSubmitPrompt", 1, Prompt("a"))).State);
    }

    [Theory]
    [InlineData("completed", SessionState.YourTurn)]
    [InlineData("aborted", SessionState.Aborted)]
    [InlineData("error", SessionState.Error)]
    [InlineData("future-status", SessionState.YourTurn)]   // Unknown は 🟢
    public void Stop_status_decides_state(string status, SessionState expected)
    {
        var s = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("stop", 1, Stop(status)));

        Assert.Equal(expected, s.State);
    }

    [Fact]
    public void Session_end_is_closed_and_resume_returns_to_turn_state()
    {
        var closed = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("stop", 1, Stop("completed")),
            E("sessionEnd", 2, "\"reason\":\"user_close\""));
        Assert.Equal(SessionState.Closed, closed.State);
        Assert.Equal("user_close", closed.ClosedReason);

        var resumedByPrompt = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("stop", 1, Stop("completed")),
            E("sessionEnd", 2), E("beforeSubmitPrompt", 3, Prompt("b")));
        Assert.Equal(SessionState.Running, resumedByPrompt.State);

        var resumedByOther = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("stop", 1, Stop("aborted")),
            E("sessionEnd", 2), E("sessionStart", 3));
        Assert.Equal(SessionState.Aborted, resumedByOther.State);
    }

    [Fact]
    public void Missing_stop_before_next_prompt_keeps_running_and_marks_turn_unknown()
    {
        var s = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("beforeSubmitPrompt", 1, Prompt("b")));

        Assert.Equal(SessionState.Running, s.State);
        Assert.Equal([TurnStatus.Unknown, TurnStatus.Running], s.Turns.Select(t => t.Status));
    }

    [Fact]
    public void Turn_list_has_status_and_prompt()
    {
        var s = Analyze(
            E("beforeSubmitPrompt", 0, Prompt("one")), E("stop", 1, Stop("completed")),
            E("beforeSubmitPrompt", 2, Prompt("two")), E("stop", 3, Stop("aborted")),
            E("beforeSubmitPrompt", 4, Prompt("three")), E("stop", 5, Stop("error")),
            E("beforeSubmitPrompt", 6, Prompt("four")));

        Assert.Equal([TurnStatus.Completed, TurnStatus.Aborted, TurnStatus.Error, TurnStatus.Running], s.Turns.Select(t => t.Status));
        Assert.Equal(["one", "two", "three", "four"], s.Turns.Select(t => t.Prompt));
        Assert.Equal([1, 2, 3, 4], s.Turns.Select(t => t.Number));
        Assert.Equal(3, s.TurnCount);
        Assert.Equal(4, s.PromptCount);
    }

    [Fact]
    public void Unfinished_turn_of_a_closed_session_is_unknown()
    {
        var s = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("sessionEnd", 1));

        Assert.Equal(TurnStatus.Unknown, s.Turns[0].Status);
    }

    [Fact]
    public void Running_tools_are_added_and_removed_by_id()
    {
        var s = Analyze(
            E("beforeSubmitPrompt", 0, Prompt("a")),
            E("preToolUse", 1, Tool("t1", cmd: "npm test")), E("preToolUse", 2, Tool("t2", "Read")),
            E("postToolUse", 3, Tool("t2", "Read")));

        var running = Assert.Single(s.RunningTools);
        Assert.Equal(("t1", "Shell", "npm test"), (running.ToolUseId, running.ToolName, running.Command));
        Assert.Equal(T0.AddSeconds(1), running.StartedAt);
    }

    [Fact]
    public void Failure_also_removes_running_tool()
    {
        var s = Analyze(E("preToolUse", 0, Tool("t1")), E("postToolUseFailure", 1, Tool("t1")));

        Assert.Empty(s.RunningTools);
        Assert.Equal(1, s.ToolCallCount);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("sessionEnd")]
    public void Stop_and_session_end_clear_leftover_tools(string clearing)
    {
        var s = Analyze(E("beforeSubmitPrompt", 0, Prompt("a")), E("preToolUse", 1, Tool("t1")),
            E(clearing, 2, clearing == "stop" ? Stop("aborted") : ""));

        Assert.Empty(s.RunningTools);
    }

    [Fact]
    public void Pre_tool_without_id_is_ignored()
    {
        var s = Analyze(E("preToolUse", 0, "\"tool_name\":\"Shell\""));

        Assert.Empty(s.RunningTools);
    }

    [Fact]
    public void Derived_title_is_first_prompt_40_chars_without_newlines()
    {
        var long50 = new string('あ', 50);
        var s = Analyze(E("beforeSubmitPrompt", 0, Prompt(long50)), E("beforeSubmitPrompt", 1, Prompt("later")));
        Assert.Equal(new string('あ', 40), s.AutoTitle);

        var multi = Analyze(E("beforeSubmitPrompt", 0, Prompt("line1\\nline2\\r\\nline3")));
        Assert.Equal("line1 line2 line3", multi.AutoTitle);

        Assert.Null(Analyze(E("sessionStart")).AutoTitle);
    }

    [Fact]
    public void Title_does_not_split_a_surrogate_pair()
    {
        var s = Analyze(E("beforeSubmitPrompt", 0, Prompt(new string('a', 39) + "\\ud83d\\ude00tail")));

        Assert.Equal(new string('a', 39), s.AutoTitle);
    }

    [Fact]
    public void Times_duration_model_and_branch()
    {
        var events = Events(
            E("sessionStart", 10, "\"model\":\"m1\""),
            E("beforeSubmitPrompt", 20, Prompt("a")),
            E("afterAgentResponse", 70, "\"model_id\":\"m2\",\"text\":\"hi\""));
        events[0] = events[0] with { Git = new GitSnapshot("feature/x", "h1") };
        var s = SessionAnalyzer.Analyze(Key, events);

        Assert.Equal(T0.AddSeconds(10), s.StartedAt);
        Assert.Equal(T0.AddSeconds(70), s.LastActivityAt);
        Assert.Equal(TimeSpan.FromSeconds(60), s.Duration);
        Assert.Equal("m2", s.Model);
        Assert.Equal("feature/x", s.Branch);
        Assert.Equal("h1", s.StartHead);
    }

    [Fact]
    public void Model_default_from_tool_events_does_not_replace_the_real_model()
    {
        var s = Analyze(
            E("beforeSubmitPrompt", 0, "\"prompt\":\"a\",\"model\":\"grok-4.7-high\",\"model_id\":\"grok-4.7\""),
            E("preToolUse", 1, Tool("t1") + ",\"model\":\"default\""),
            E("stop", 2, "\"status\":\"completed\",\"model\":\"default\""));

        Assert.Equal("grok-4.7", s.Model);
        Assert.Null(Analyze(E("stop", 0, "\"model\":\"default\"")).Model);
    }

    [Fact]
    public void Start_falls_back_to_first_event_and_session_end_duration_wins()
    {
        var s = Analyze(E("beforeSubmitPrompt", 5, Prompt("a")), E("sessionEnd", 100, "\"duration_ms\":42000"));

        Assert.Equal(T0.AddSeconds(5), s.StartedAt);
        Assert.Equal(TimeSpan.FromSeconds(42), s.Duration);
    }

    [Fact]
    public void Start_and_latest_head_come_from_first_and_last_git_snapshots()
    {
        var events = Events(E("sessionStart"), E("beforeSubmitPrompt", 1, Prompt("a")), E("stop", 2, Stop("completed")),
            E("stop", 3, Stop("completed")));
        events[0] = events[0] with { Git = new GitSnapshot("main", "h1") };
        events[2] = events[2] with { Git = new GitSnapshot("main", "h2") };
        events[3] = events[3] with { Git = new GitSnapshot("dev", "h3") };

        var s = SessionAnalyzer.Analyze(Key, events);

        Assert.Equal(("h1", "h3", "dev"), (s.StartHead, s.LatestHead, s.Branch));
    }

    [Fact]
    public void Counts_tools_subagents_and_compactions()
    {
        var s = Analyze(
            E("postToolUse", 0, Tool("a")), E("postToolUse", 1, Tool("b")), E("postToolUseFailure", 2, Tool("c")),
            E("subagentStart", 3), E("subagentStart", 4), E("subagentStop", 5),
            E("preCompact", 6, "\"trigger\":\"manual\",\"context_usage_percent\":70"),
            E("preCompact", 7, "\"trigger\":\"auto\",\"context_usage_percent\":85"));

        Assert.Equal(3, s.ToolCallCount);
        Assert.Equal((1, 2), (s.SubagentsRunning, s.SubagentsTotal));
        Assert.Equal(2, s.CompactionCount);
        Assert.Equal(new CompactionInfo("auto", 85), s.LastCompaction);
    }

    [Fact]
    public void Subagent_running_never_goes_negative()
    {
        Assert.Equal(0, Analyze(E("subagentStop", 0)).SubagentsRunning);
    }

    [Fact]
    public void Changed_files_are_distinct_in_first_seen_order()
    {
        string F(string p) => $"\"file_path\":\"{p}\"";
        var s = Analyze(E("afterFileEdit", 0, F("a.cs")), E("afterFileEdit", 1, F("b.cs")), E("afterFileEdit", 2, F("A.CS")));

        Assert.Equal(["a.cs", "b.cs"], s.ChangedFiles);
    }

    [Fact]
    public void Test_runs_match_shell_commands_and_read_exit_code()
    {
        string Out(int code) => $"\"tool_output\":{{\"exitCode\":{code}}},\"duration\":2000";
        var s = Analyze(
            E("postToolUse", 0, Tool("1", cmd: "dotnet test --no-build") + "," + Out(0)),
            E("postToolUse", 1, Tool("2", cmd: "npx playwright test") + "," + Out(1)),
            E("postToolUse", 2, Tool("3", cmd: "ls -la") + "," + Out(0)),
            E("postToolUse", 3, Tool("4", "Write", "dotnet test") + "," + Out(0)),
            E("postToolUse", 4, Tool("5", cmd: "PYTEST -q")));

        Assert.Equal(["dotnet test --no-build", "npx playwright test", "PYTEST -q"], s.TestRuns.Select(t => t.Command));
        Assert.Equal([true, false, null], s.TestRuns.Select(t => t.Succeeded));
        Assert.Equal(TimeSpan.FromSeconds(2), s.TestRuns[0].Duration);
    }

    [Fact]
    public void Test_patterns_come_from_settings()
    {
        var events = Events(E("postToolUse", 0, Tool("1", cmd: "make check") + ",\"tool_output\":{\"exitCode\":0}"));

        var s = SessionAnalyzer.Analyze(Key, events, new AnalyzerSettings { TestCommandPatterns = ["make check"] });

        Assert.Single(s.TestRuns);
    }

    [Fact]
    public void Last_prompt_tool_and_response_are_exposed_for_the_three_line_summary()
    {
        var s = Analyze(
            E("beforeSubmitPrompt", 0, Prompt("p1")), E("afterAgentResponse", 1, "\"text\":\"r1\""),
            E("postToolUse", 2, Tool("t")), E("beforeSubmitPrompt", 3, Prompt("p2")),
            E("afterAgentResponse", 4, "\"text\":\"r2\""));

        Assert.Equal(("p2", 5L, 4L), (s.LastPrompt!.Text, s.LastResponse!.Seq, s.LastPrompt.Seq));
        Assert.Equal(3L, s.LastToolResult!.Seq);
    }

    [Fact]
    public void Last_prompt_and_response_skip_events_without_text()
    {
        var s = Analyze(
            E("beforeSubmitPrompt", 0, Prompt("読めた依頼")), E("afterAgentResponse", 1, "\"text\":\"読めた返事\""),
            E("beforeSubmitPrompt", 2), E("afterAgentResponse", 3));   // 入力が壊れて本文なし

        Assert.Equal("読めた依頼", s.LastPrompt!.Text);
        Assert.Equal("読めた返事", s.LastResponse!.Text);
        Assert.Equal(2, s.PromptCount);   // 依頼があったこと自体は数える
    }

    [Fact]
    public void Transcript_path_comes_from_the_latest_event_that_has_one()
    {
        var s = Analyze(E("sessionStart", 0, "\"transcript_path\":\"C:\\\\t.jsonl\""), E("stop", 1, Stop("completed")));

        Assert.Equal(@"C:\t.jsonl", s.TranscriptPath);
    }

    [Fact]
    public void Empty_events_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => SessionAnalyzer.Analyze(Key, []));
    }
}