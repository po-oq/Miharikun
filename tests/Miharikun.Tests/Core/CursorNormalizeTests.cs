using Miharikun.Core.Agents;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class CursorNormalizeTests
{
    private static AgentEvent One(RawEventRecord raw)
    {
        var events = new CursorAgent().Normalize(raw).ToList();
        return Assert.Single(events);
    }

    [Theory]
    [InlineData("sessionStart", AgentEventKind.SessionStarted)]
    [InlineData("sessionEnd", AgentEventKind.SessionEnded)]
    [InlineData("beforeSubmitPrompt", AgentEventKind.PromptSubmitted)]
    [InlineData("stop", AgentEventKind.TurnEnded)]
    [InlineData("preToolUse", AgentEventKind.ToolStarted)]
    [InlineData("postToolUse", AgentEventKind.ToolSucceeded)]
    [InlineData("postToolUseFailure", AgentEventKind.ToolFailed)]
    [InlineData("afterAgentResponse", AgentEventKind.AssistantMessage)]
    [InlineData("afterAgentThought", AgentEventKind.AssistantThought)]
    [InlineData("subagentStart", AgentEventKind.SubagentStarted)]
    [InlineData("subagentStop", AgentEventKind.SubagentStopped)]
    [InlineData("afterFileEdit", AgentEventKind.FileEdited)]
    [InlineData("preCompact", AgentEventKind.Compacted)]
    public void Maps_event_names_to_kinds(string evt, AgentEventKind kind)
    {
        var e = One(Raw(evt, line: 7, sec: 5));

        Assert.Equal(kind, e.Kind);
        Assert.Equal(7, e.Seq);
        Assert.Equal(T0.AddSeconds(5), e.At);
        Assert.Equal(new SessionKey("cursor", "conv-1"), e.Session);
    }

    [Fact]
    public void Unknown_event_yields_nothing()
    {
        Assert.Empty(new CursorAgent().Normalize(Raw("beforeReadFile")));
    }

    [Theory]
    [InlineData("completed", TurnOutcome.Completed)]
    [InlineData("aborted", TurnOutcome.Aborted)]
    [InlineData("error", TurnOutcome.Error)]
    [InlineData("something-new", TurnOutcome.Unknown)]
    public void Stop_status_becomes_outcome(string status, TurnOutcome expected)
    {
        var git = new GitSnapshot("main", "abc");
        var e = One(Raw("stop", $"\"status\":\"{status}\"", git: git));

        Assert.Equal(expected, e.Outcome);
        Assert.Equal(git, e.Git);
    }

    [Fact]
    public void Prompt_session_end_and_edit_fields()
    {
        Assert.Equal("やって", One(Raw("beforeSubmitPrompt", "\"prompt\":\"やって\"")).Text);

        var end = One(Raw("sessionEnd", "\"reason\":\"user_close\",\"duration_ms\":90000"));
        Assert.Equal("user_close", end.Reason);
        Assert.Equal(TimeSpan.FromSeconds(90), end.Duration);

        Assert.Equal(@"C:\work\proj\a.cs", One(Raw("afterFileEdit", "\"file_path\":\"C:\\\\work\\\\proj\\\\a.cs\"")).FilePath);
    }

    [Fact]
    public void Tool_events_carry_id_name_and_command()
    {
        var f = "\"tool_use_id\":\"t1\",\"tool_name\":\"Shell\",\"tool_input\":{\"command\":\"dotnet test\"}";

        var pre = One(Raw("preToolUse", f));
        Assert.Equal(("t1", "Shell", "dotnet test"), (pre.ToolUseId, pre.ToolName, pre.Command));

        var fail = One(Raw("postToolUseFailure", f + ",\"error_message\":\"boom\""));
        Assert.Equal("boom", fail.Output);
    }

    [Theory]
    [InlineData("\"tool_output\":{\"exitCode\":0,\"output\":\"ok\"}", 0, "ok")]
    [InlineData("\"tool_output\":{\"exitCode\":2}", 2, null)]
    [InlineData("\"tool_output\":\"{\\\"exitCode\\\":1,\\\"output\\\":\\\"x\\\"}\"", 1, null)]
    [InlineData("\"tool_output\":\"plain text\"", null, "plain text")]
    [InlineData("", null, null)]
    public void Shell_exit_code_is_read_from_tool_output(string outputField, int? exitCode, string? output)
    {
        var fields = "\"tool_use_id\":\"t1\",\"tool_name\":\"Shell\",\"duration\":1500" + (outputField.Length > 0 ? "," + outputField : "");
        var e = One(Raw("postToolUse", fields));

        Assert.Equal(exitCode, e.ExitCode);
        if (output is not null) Assert.Equal(output, e.Output);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), e.Duration);
    }

    [Fact]
    public void Exit_code_is_only_read_for_shell()
    {
        var e = One(Raw("postToolUse", "\"tool_name\":\"Write\",\"tool_output\":{\"exitCode\":0}"));

        Assert.Null(e.ExitCode);
    }

    [Fact]
    public void Compaction_keeps_trigger_and_rounded_percent()
    {
        var e = One(Raw("preCompact", "\"trigger\":\"auto\",\"context_usage_percent\":84.6"));

        Assert.Equal(new CompactionInfo("auto", 85), e.Compaction);
    }

    [Fact]
    public void Model_prefers_model_id_and_keeps_params()
    {
        var withId = One(Raw("stop", "\"model\":\"auto\",\"model_id\":\"claude-x\",\"model_params\":[{\"id\":\"thinking\",\"value\":\"on\"}]"));
        Assert.Equal("claude-x", withId.Model);
        Assert.Contains("thinking", withId.ModelParams);

        Assert.Equal("auto", One(Raw("stop", "\"model\":\"auto\"")).Model);
        Assert.Null(One(Raw("stop")).Model);
    }

    [Fact]
    public void Model_default_is_normalized_to_null()
    {
        // Cursor はツール系などのイベントで model に "default" を入れてくる（実機で確認）。モデル名ではないので共通イベントには載せない。
        Assert.Null(One(Raw("postToolUse", "\"model\":\"default\",\"tool_name\":\"Read\"")).Model);
        Assert.Null(One(Raw("stop", "\"model_id\":\"default\"")).Model);
        Assert.Equal("auto", One(Raw("stop", "\"model\":\"auto\"")).Model);
    }

    [Fact]
    public void Transcript_path_is_kept()
    {
        Assert.Equal(@"C:\t\a.jsonl", One(Raw("stop", "\"transcript_path\":\"C:\\\\t\\\\a.jsonl\"")).TranscriptPath);
    }

    [Fact]
    public void Workspace_roots_are_read_from_payload()
    {
        var agent = new CursorAgent();

        Assert.Equal([Root], agent.GetWorkspaceRoots(Raw("stop")));
    }
}