using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>Phase 19-4：表示用の状態（要件 10.1・計画 8.3）。「実行中」のまま動きがなければ「停止」と表示する。</summary>
public sealed class StalledRuleTests
{
    private static readonly SessionKey ClaudeKey = new("claude", "sess-1");

    /// <summary>Claude のログから作った要約。最後の動きは Start + 数秒。</summary>
    private static SessionSummary ClaudeSummary(Func<ClaudeLogBuilder, string[]> lines)
    {
        var normalizer = new ClaudeTranscriptNormalizer("sess-1", "sess-1.jsonl");
        var events = new List<AgentEvent>();
        var all = lines(new ClaudeLogBuilder());
        for (var i = 0; i < all.Length; i++)
            events.AddRange(normalizer.NormalizeLine(i + 1, all[i]));
        return SessionAnalyzer.Analyze(ClaudeKey, events);
    }

    private static SessionSummary Running() => ClaudeSummary(b => [b.User("a"), b.AssistantText("途中")]);

    private static DateTimeOffset After(SessionSummary s, TimeSpan idle) => s.LastActivityAt + idle;

    // ---- しきい値 ----

    [Fact]
    public void Running_stays_running_below_the_threshold()
    {
        var s = Running();

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1)), 10);

        Assert.Equal(SessionState.Running, d.State);
        Assert.Null(d.IdleMinutes);
        Assert.Null(d.Note);
    }

    [Fact]
    public void Running_becomes_stalled_at_the_threshold_with_the_idle_minutes()
    {
        var s = Running();

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromMinutes(10)), 10);

        Assert.Equal(SessionState.Aborted, d.State);   // 🟡 停止
        Assert.Equal(10, d.IdleMinutes);
        Assert.Equal("10分動きなし", d.Note);
    }

    [Fact]
    public void Idle_minutes_are_rounded_down()
    {
        var s = Running();

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromSeconds(12 * 60 + 59)), 10);

        Assert.Equal(12, d.IdleMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_threshold_of_zero_or_less_turns_it_off(int minutes)
    {
        var s = Running();

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromDays(3)), minutes);

        Assert.Equal(SessionState.Running, d.State);
        Assert.Null(d.IdleMinutes);
    }

    [Fact]
    public void A_clock_that_is_behind_the_last_activity_is_not_stalled()
    {
        var s = Running();

        var d = StalledRule.DisplayState(s, s.LastActivityAt - TimeSpan.FromMinutes(5), 10);

        Assert.Equal(SessionState.Running, d.State);
    }

    // ---- 実行中でないものは、そのまま ----

    [Fact]
    public void States_other_than_running_are_not_changed_however_long_ago()
    {
        var yourTurn = ClaudeSummary(b => [b.User("a"), b.AssistantText("おわり", stopReason: "end_turn")]);
        var aborted = ClaudeSummary(b => [b.User("a"), b.UserBlocks("[Request interrupted by user]")]);
        var error = ClaudeSummary(b => [b.User("a"), b.AssistantApiError("API Error")]);

        foreach (var s in new[] { yourTurn, aborted, error })
        {
            var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromDays(1)), 10);
            Assert.Equal(s.State, d.State);
            Assert.Null(d.Note);
        }
    }

    [Fact]
    public void An_imported_session_is_not_changed()
    {
        var events = new List<AgentEvent>
        {
            new(new SessionKey("cursor", "old"), 1, T0, AgentEventKind.PromptSubmitted, Text: "a", Imported: true),
        };
        var s = SessionAnalyzer.Analyze(new SessionKey("cursor", "old"), events);

        var d = StalledRule.DisplayState(s, T0.AddDays(5), 10);

        Assert.Equal(SessionState.Imported, d.State);
    }

    // ---- 裏で動いているものがあれば、停止にしない ----

    [Fact]
    public void A_running_subagent_keeps_it_running_with_a_note()
    {
        var s = ClaudeSummary(b => [b.User("a"), b.AgentCall("a1", "調べる")]);

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromMinutes(30)), 10);

        Assert.Equal(SessionState.Running, d.State);
        Assert.Equal(30, d.IdleMinutes);
        Assert.Equal("30分動きなし", d.Note);
    }

    [Fact]
    public void A_tool_waiting_for_its_result_keeps_it_running_with_a_note()
    {
        var s = ClaudeSummary(b => [b.User("a"), b.Bash("t1", "dotnet test")]);   // 結果がまだ来ていない

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromMinutes(15)), 10);

        Assert.Equal(SessionState.Running, d.State);
        Assert.Equal(15, d.IdleMinutes);
    }

    [Fact]
    public void Below_the_threshold_a_running_subagent_has_no_note()
    {
        var s = ClaudeSummary(b => [b.User("a"), b.AgentCall("a1", "調べる")]);

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromMinutes(2)), 10);

        Assert.Equal(SessionState.Running, d.State);
        Assert.Null(d.Note);
    }

    [Fact]
    public void A_finished_subagent_and_a_finished_tool_do_not_hold_it_running()
    {
        var s = ClaudeSummary(b => [
            b.User("a"), b.AgentCall("a1", "調べる"), b.AgentResult("a1"),
            b.Bash("t1", "ls"), b.ToolResult("t1", "ok")]);

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromMinutes(20)), 10);

        Assert.Equal(SessionState.Aborted, d.State);
    }

    [Fact]
    public void Waiting_for_an_answer_is_not_stalled()
    {
        // 質問待ちは「ボスの番」で、もともと実行中ではない。
        var s = ClaudeSummary(b => [b.User("a"), b.ToolUse("q1", "AskUserQuestion", new JsonObject())]);

        var d = StalledRule.DisplayState(s, After(s, TimeSpan.FromHours(2)), 10);

        Assert.Equal(SessionState.YourTurn, d.State);
    }

    // ---- Cursor にも効く ----

    [Fact]
    public void It_applies_to_cursor_sessions_too()
    {
        var s = SessionAnalyzer.Analyze(new SessionKey("cursor", "conv-1"), Events(E("beforeSubmitPrompt", 0, "\"prompt\":\"a\"")));

        var d = StalledRule.DisplayState(s, s.LastActivityAt.AddMinutes(11), 10);

        Assert.Equal(SessionState.Aborted, d.State);
        Assert.Equal(11, d.IdleMinutes);
    }
}
