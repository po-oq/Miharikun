using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.ClaudeLogBuilder;

namespace Miharikun.Tests.Core;

/// <summary>Phase 19-2：ターンの終わり・中断・エラー・ユーザーの返事待ち（要件 5.1 の対応表・計画 8.2）。</summary>
public sealed class ClaudeTurnTests
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

    private static List<AgentEvent> Turns(List<AgentEvent> events) => events.Where(e => e.Kind == AgentEventKind.TurnEnded).ToList();

    private static SessionSummary Analyze(List<AgentEvent> events) =>
        SessionAnalyzer.Analyze(new SessionKey("claude", "sess-1"), events);

    // ---- end_turn ----

    [Fact]
    public void End_turn_on_a_text_line_gives_TurnEnded_after_the_text()
    {
        var events = Run(_b.User("a"), _b.AssistantText("おわり", stopReason: "end_turn"));

        var kinds = events.Where(e => e.Seq == 2).Select(e => e.Kind).ToList();
        Assert.Equal([AgentEventKind.AssistantMessage, AgentEventKind.TurnEnded], kinds);
        var turn = Assert.Single(Turns(events));
        Assert.Equal(TurnOutcome.Completed, turn.Outcome);
    }

    [Fact]
    public void End_turn_on_both_the_thinking_line_and_the_text_line_gives_one_TurnEnded_on_the_text_line()
    {
        var events = Run(
            _b.User("a"),
            _b.AssistantThinking("考える", stopReason: "end_turn", messageId: "m1"),
            _b.AssistantText("こたえ", stopReason: "end_turn", messageId: "m1"));

        var turn = Assert.Single(Turns(events));
        Assert.Equal(3, turn.Seq);   // 本文の行
    }

    [Fact]
    public void The_same_message_id_ends_the_turn_only_once()
    {
        var events = Run(
            _b.User("a"),
            _b.AssistantText("前半", stopReason: "end_turn", messageId: "m1"),
            _b.AssistantText("後半", stopReason: "end_turn", messageId: "m1"));

        Assert.Single(Turns(events));
    }

    [Fact]
    public void Different_message_ids_end_different_turns()
    {
        var events = Run(
            _b.User("a"), _b.AssistantText("1", stopReason: "end_turn", messageId: "m1"),
            _b.User("b"), _b.AssistantText("2", stopReason: "end_turn", messageId: "m2"));

        Assert.Equal(2, Turns(events).Count);
    }

    [Theory]
    [InlineData("tool_use")]
    [InlineData("max_tokens")]
    [InlineData(null)]
    public void Other_stop_reasons_do_not_end_the_turn(string? stopReason)
    {
        var events = Run(_b.User("a"), _b.AssistantText("途中", stopReason: stopReason));

        Assert.Empty(Turns(events));
    }

    [Fact]
    public void End_turn_on_a_thinking_only_line_does_not_end_the_turn()
    {
        var events = Run(_b.User("a"), _b.AssistantThinking("考える", stopReason: "end_turn"));

        Assert.Empty(Turns(events));
    }

    // ---- 中断 ----

    [Theory]
    [InlineData("[Request interrupted by user]")]
    [InlineData("[Request interrupted by user for tool use]")]
    public void Interrupt_marker_ends_the_turn_as_aborted(string marker)
    {
        var events = Run(_b.User("a"), _b.UserBlocks(marker), _b.User(marker, Origin.None));

        var turns = Turns(events);
        Assert.Equal(2, turns.Count);
        Assert.All(turns, t => Assert.Equal(TurnOutcome.Aborted, t.Outcome));
        Assert.Equal(1, events.Count(e => e.Kind == AgentEventKind.PromptSubmitted));   // 入力は「a」だけ
    }

    [Fact]
    public void Interrupt_marker_from_a_task_notification_is_ignored()
    {
        var events = Run(_b.User("[Request interrupted by user]", Origin.TaskNotification));

        Assert.Empty(Turns(events));
    }

    // ---- API エラー ----

    [Fact]
    public void Api_error_reply_ends_the_turn_as_error_and_is_not_a_message()
    {
        var events = Run(_b.User("a"), _b.AssistantApiError("API Error: 529 overloaded"));

        var turn = Assert.Single(Turns(events));
        Assert.Equal(TurnOutcome.Error, turn.Outcome);
        Assert.DoesNotContain(events, e => e.Kind == AgentEventKind.AssistantMessage);
    }

    [Fact]
    public void Api_error_reply_is_an_error_even_when_its_stop_reason_is_end_turn()
    {
        var events = Run(_b.User("a"), _b.AssistantApiError("API Error", stopReason: "end_turn"));

        var turn = Assert.Single(Turns(events));
        Assert.Equal(TurnOutcome.Error, turn.Outcome);
    }

    // ---- 質問待ち・計画の承認待ち ----

    [Theory]
    [InlineData("AskUserQuestion")]
    [InlineData("ExitPlanMode")]
    public void A_question_or_plan_approval_call_hands_the_turn_to_the_user(string tool)
    {
        var events = Run(_b.User("a"), _b.ToolUse("q1", tool, new JsonObject { ["plan"] = "…" }));

        var turn = Assert.Single(Turns(events));
        Assert.Equal(TurnOutcome.Completed, turn.Outcome);
        Assert.DoesNotContain(events, e => e.Kind == AgentEventKind.ToolStarted);   // 実行中のツールにしない
    }

    [Theory]
    [InlineData("AskUserQuestion", false)]
    [InlineData("ExitPlanMode", false)]
    [InlineData("ExitPlanMode", true)]   // 計画を断った（結果が is_error）ときも、返事として扱う
    public void The_answer_becomes_a_prompt_with_the_answer_text(string tool, bool isError)
    {
        var events = Run(
            _b.User("a"),
            _b.ToolUse("q1", tool, new JsonObject()),
            _b.ToolResult("q1", "User has answered your questions: \"どちら？\"=\"Aにする\"", isError: isError));

        var answer = events.Last(e => e.Kind == AgentEventKind.PromptSubmitted);
        Assert.Equal(3, answer.Seq);
        Assert.StartsWith("（回答）", answer.Text);
        Assert.Contains("Aにする", answer.Text);
        Assert.DoesNotContain(events, e => e.Kind is AgentEventKind.ToolSucceeded or AgentEventKind.ToolFailed);
    }

    [Fact]
    public void A_normal_tool_result_after_a_question_is_still_a_normal_tool_result()
    {
        var events = Run(
            _b.ToolUse("q1", "AskUserQuestion", new JsonObject()),
            _b.ToolUse("t1", "Read", new JsonObject { ["file_path"] = "a" }),
            _b.ToolResult("t1", "x"));

        Assert.Contains(events, e => e.Kind == AgentEventKind.ToolSucceeded && e.ToolUseId == "t1");
    }

    // ---- 状態（SessionAnalyzer を通して） ----

    [Fact]
    public void A_finished_turn_is_your_turn()
    {
        var s = Analyze(Run(_b.User("a"), _b.AssistantText("おわり", stopReason: "end_turn")));

        Assert.Equal(SessionState.YourTurn, s.State);
        Assert.Equal(1, s.TurnCount);
    }

    [Fact]
    public void A_turn_in_progress_is_running()
    {
        var s = Analyze(Run(_b.User("a"), _b.Bash("t1", "dotnet build")));

        Assert.Equal(SessionState.Running, s.State);
        Assert.Single(s.RunningTools);
    }

    [Fact]
    public void Waiting_for_an_answer_is_your_turn_and_the_answer_counts_as_a_request()
    {
        var events = Run(_b.User("a"), _b.ToolUse("q1", "AskUserQuestion", new JsonObject()));
        var waiting = Analyze(events);
        Assert.Equal(SessionState.YourTurn, waiting.State);
        Assert.Empty(waiting.RunningTools);

        events.AddRange(_normalizer.NormalizeLine(3, _b.ToolResult("q1", "回答です")));
        var answered = Analyze(events);
        Assert.Equal(SessionState.Running, answered.State);
        Assert.Equal(2, answered.PromptCount);   // 回答も依頼数に数える
        Assert.StartsWith("（回答）", answered.LastPrompt!.Text);

        events.AddRange(_normalizer.NormalizeLine(4, _b.AssistantText("了解", stopReason: "end_turn")));
        var done = Analyze(events);
        Assert.Equal(SessionState.YourTurn, done.State);
        Assert.Equal(2, done.TurnCount);
    }

    [Fact]
    public void An_interrupted_turn_is_aborted()
    {
        var s = Analyze(Run(_b.User("a"), _b.AssistantText("途中"), _b.UserBlocks("[Request interrupted by user]")));

        Assert.Equal(SessionState.Aborted, s.State);
    }

    [Fact]
    public void An_api_error_is_an_error_state()
    {
        var s = Analyze(Run(_b.User("a"), _b.AssistantApiError("API Error")));

        Assert.Equal(SessionState.Error, s.State);
    }

    [Fact]
    public void A_new_input_after_an_interrupt_runs_again()
    {
        var s = Analyze(Run(_b.User("a"), _b.UserBlocks("[Request interrupted by user]"), _b.User("続けて")));

        Assert.Equal(SessionState.Running, s.State);
        Assert.Equal(2, s.PromptCount);
    }

    [Fact]
    public void A_failed_test_run_is_recorded_with_its_exit_code_and_the_command()
    {
        var s = Analyze(Run(
            _b.User("テストして"),
            _b.Bash("t1", "dotnet test"),
            _b.ToolResult("t1", "Exit code 1\nFailed!", isError: true),
            _b.AssistantText("失敗しました", stopReason: "end_turn")));

        var run = Assert.Single(s.TestRuns);
        Assert.Equal("dotnet test", run.Command);
        Assert.Equal(1, run.ExitCode);
        Assert.False(run.Succeeded);
    }

    [Fact]
    public void A_test_run_in_the_background_has_an_unknown_result()
    {
        var s = Analyze(Run(
            _b.User("テストして"),
            _b.Bash("t1", "dotnet test", background: true),
            _b.ToolResult("t1", "Command running in background")));

        var run = Assert.Single(s.TestRuns);
        Assert.Null(run.ExitCode);
        Assert.Null(run.Succeeded);
    }

    [Fact]
    public void Edited_files_and_the_branch_reach_the_summary()
    {
        var s = Analyze(Run(
            _b.User("直して"),
            _b.ToolUse("t1", "Edit", new JsonObject { ["file_path"] = @"C:\work\proj\a.cs" }),
            _b.ToolResult("t1", "ok")));

        Assert.Equal([@"C:\work\proj\a.cs"], s.ChangedFiles);
        Assert.Equal("main", s.Branch);
        Assert.Equal("claude-x", Analyze(Run(_b.User("a"), _b.AssistantText("b"))).Model);
    }
}
