using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>Phase 19-3：サブエージェント（Claude の Agent ツール・Cursor の subagentStart/Stop）と、要約の履歴。</summary>
public sealed class ClaudeSubagentTests
{
    private readonly ClaudeTranscriptNormalizer _normalizer = new("sess-1", "sess-1.jsonl");
    private readonly ClaudeLogBuilder _b = new();

    private List<AgentEvent> Run(params string[] lines)
    {
        var events = new List<AgentEvent>();
        for (var i = 0; i < lines.Length; i++)
            events.AddRange(_normalizer.NormalizeLine(i + 1, lines[i]));
        return events;
    }

    private static SessionSummary Analyze(IReadOnlyList<AgentEvent> events) =>
        SessionAnalyzer.Analyze(new SessionKey("claude", "sess-1"), events);

    private static List<AgentEvent> Subagent(List<AgentEvent> events) =>
        events.Where(e => e.Kind is AgentEventKind.SubagentStarted or AgentEventKind.SubagentStopped).ToList();

    // ---- Claude の変換 ----

    [Fact]
    public void Agent_call_is_SubagentStarted_with_description_and_type_and_not_a_tool()
    {
        var events = Run(_b.User("a"), _b.AgentCall("a1", "コードを調べる", "Explore"));

        var e = Assert.Single(Subagent(events));
        Assert.Equal(AgentEventKind.SubagentStarted, e.Kind);
        Assert.Equal("a1", e.ToolUseId);
        Assert.Equal("コードを調べる", e.Text);
        Assert.Equal("Explore", e.ToolName);
        Assert.DoesNotContain(events, x => x.Kind == AgentEventKind.ToolStarted);
    }

    [Fact]
    public void Completed_result_is_SubagentStopped_with_the_agent_id_and_no_tool_event()
    {
        var events = Run(_b.AgentCall("a1", "調べる"), _b.AgentResult("a1", "completed", "ag-9"));

        var stop = Assert.Single(Subagent(events), e => e.Kind == AgentEventKind.SubagentStopped);
        Assert.Equal("a1", stop.ToolUseId);
        Assert.Equal("ag-9", stop.SubagentId);
        Assert.DoesNotContain(events, x => x.Kind is AgentEventKind.ToolSucceeded or AgentEventKind.ToolFailed);
    }

    [Fact]
    public void A_result_that_is_not_completed_keeps_the_subagent_running()
    {
        var events = Run(_b.AgentCall("a1", "裏で調べる"), _b.AgentResult("a1", "async_launched"));

        Assert.Equal([AgentEventKind.SubagentStarted], Subagent(events).Select(e => e.Kind));
    }

    [Fact]
    public void A_task_notification_with_the_matching_id_stops_the_subagent()
    {
        var events = Run(_b.AgentCall("a1", "裏で調べる"), _b.AgentResult("a1", "async_launched"), _b.TaskNotification("a1"));

        var stop = Subagent(events).Last();
        Assert.Equal(AgentEventKind.SubagentStopped, stop.Kind);
        Assert.Equal("a1", stop.ToolUseId);
        Assert.Equal(3, stop.Seq);
        Assert.DoesNotContain(events, x => x.Kind == AgentEventKind.PromptSubmitted && x.Seq == 3);   // 入力ではない
    }

    [Fact]
    public void A_task_notification_for_something_else_is_ignored()
    {
        var events = Run(_b.AgentCall("a1", "調べる"), _b.TaskNotification("not-an-agent"), _b.TaskNotification("a1"));

        Assert.Equal(1, Subagent(events).Count(e => e.Kind == AgentEventKind.SubagentStopped));   // a1 の分だけ
        Assert.DoesNotContain(events, e => e.Seq == 2 && e.Kind != AgentEventKind.SessionStarted);
    }

    [Fact]
    public void A_task_notification_after_the_agent_already_completed_does_not_stop_it_twice()
    {
        var events = Run(_b.AgentCall("a1", "調べる"), _b.AgentResult("a1"), _b.TaskNotification("a1"));

        Assert.Equal(1, Subagent(events).Count(e => e.Kind == AgentEventKind.SubagentStopped));
    }

    [Fact]
    public void An_errored_agent_result_stops_the_subagent()
    {
        var events = Run(_b.AgentCall("a1", "調べる"), _b.ToolResult("a1", "The user doesn't want to proceed", isError: true));

        Assert.Equal(AgentEventKind.SubagentStopped, Subagent(events).Last().Kind);
    }

    [Fact]
    public void Parallel_agents_are_matched_by_their_own_ids()
    {
        var events = Run(
            _b.AgentCall("a1", "一つ目"), _b.AgentCall("a2", "二つ目"),
            _b.AgentResult("a2", "completed", "ag-2"), _b.AgentResult("a1", "completed", "ag-1"));

        var stops = Subagent(events).Where(e => e.Kind == AgentEventKind.SubagentStopped).ToList();
        Assert.Equal(["a2", "a1"], stops.Select(e => e.ToolUseId));
        Assert.Equal(["ag-2", "ag-1"], stops.Select(e => e.SubagentId));
    }

    // ---- 要約の履歴 ----

    [Fact]
    public void Summary_has_the_history_with_description_type_times_and_elapsed()
    {
        var events = Run(_b.User("a"), _b.AgentCall("a1", "調べる", "Explore"), _b.AgentResult("a1", "completed", "ag-1"));

        var s = Analyze(events);

        var info = Assert.Single(s.Subagents);
        Assert.Equal("調べる", info.Description);
        Assert.Equal("Explore", info.Type);
        Assert.Equal("ag-1", info.SubagentId);
        Assert.Equal(ClaudeLogBuilder.Start.AddSeconds(2), info.StartedAt);
        Assert.Equal(ClaudeLogBuilder.Start.AddSeconds(3), info.EndedAt);
        Assert.Equal(TimeSpan.FromSeconds(1), info.Elapsed);
        Assert.False(info.Running);
        Assert.Equal((0, 1), (s.SubagentsRunning, s.SubagentsTotal));
    }

    [Fact]
    public void A_running_subagent_has_no_end_and_is_counted()
    {
        var s = Analyze(Run(_b.User("a"), _b.AgentCall("a1", "一つ目"), _b.AgentCall("a2", "二つ目"), _b.AgentResult("a1")));

        Assert.Equal((1, 2), (s.SubagentsRunning, s.SubagentsTotal));
        var running = Assert.Single(s.Subagents, i => i.Running);
        Assert.Equal("二つ目", running.Description);
        Assert.Null(running.EndedAt);
        Assert.Null(running.Elapsed);
    }

    [Fact]
    public void A_stop_without_a_start_is_ignored()
    {
        var s = Analyze(Run(_b.User("a"), _b.AgentResult("never-started")));

        Assert.Empty(s.Subagents);
        Assert.Equal((0, 0), (s.SubagentsRunning, s.SubagentsTotal));
    }

    [Fact]
    public void Finishing_the_turn_does_not_stop_a_background_subagent()
    {
        var s = Analyze(Run(
            _b.User("a"), _b.AgentCall("a1", "裏で調べる"), _b.AgentResult("a1", "async_launched"),
            _b.AssistantText("裏で動かしました", stopReason: "end_turn")));

        Assert.Equal(SessionState.YourTurn, s.State);
        Assert.Equal(1, s.SubagentsRunning);
    }

    // ---- Cursor も同じ形に ----

    [Fact]
    public void Cursor_start_and_stop_without_ids_are_paired_in_order_and_use_the_reported_duration()
    {
        var s = SessionAnalyzer.Analyze(new SessionKey("cursor", "conv-1"), Events(
            E("subagentStart", 0, "\"task\":\"一つ目\",\"subagent_type\":\"explore\""),
            E("subagentStart", 1, "\"task\":\"二つ目\",\"subagent_type\":\"shell\""),
            E("subagentStop", 5, "\"task\":\"一つ目\",\"subagent_type\":\"explore\",\"duration_ms\":4000")));

        Assert.Equal((1, 2), (s.SubagentsRunning, s.SubagentsTotal));
        var first = s.Subagents[0];
        Assert.Equal(("一つ目", "explore"), (first.Description, first.Type));
        Assert.Equal(TimeSpan.FromSeconds(4), first.Elapsed);
        Assert.False(first.Running);
        Assert.True(s.Subagents[1].Running);
    }

    [Fact]
    public void Cursor_subagent_id_pairs_stops_that_come_out_of_order()
    {
        var s = SessionAnalyzer.Analyze(new SessionKey("cursor", "conv-1"), Events(
            E("subagentStart", 0, "\"task\":\"一つ目\",\"subagent_id\":\"x1\""),
            E("subagentStart", 1, "\"task\":\"二つ目\",\"subagent_id\":\"x2\""),
            E("subagentStop", 5, "\"subagent_id\":\"x2\"")));

        Assert.True(s.Subagents[0].Running);
        Assert.False(s.Subagents[1].Running);
        Assert.Equal("二つ目", s.Subagents[1].Description);
    }

    [Fact]
    public void Cursor_normalize_keeps_the_subagent_id()
    {
        var agent = new CursorAgent();
        var start = agent.Normalize(Raw("subagentStart", "\"task\":\"t\",\"subagent_id\":\"x1\",\"subagent_type\":\"explore\"")).Single();
        var stop = agent.Normalize(Raw("subagentStop", "\"subagent_id\":\"x1\"")).Single();

        Assert.Equal("x1", start.SubagentId);
        Assert.Equal("x1", stop.SubagentId);
    }
}
