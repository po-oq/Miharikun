using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Issue #17 Phase 34-3：SessionState.NoHook（transcript だけから作ったセッションのうち、Hook が記録していないもの）。</summary>
public sealed class NoHookStateTests
{
    private static readonly SessionKey Key = new("cursor", "nohook");
    private static readonly DateTimeOffset At = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static AgentEvent Ev(long seq, AgentEventKind kind, string text, bool hookMissing, bool imported = true) =>
        new(Key, seq, At, kind, Text: text, Imported: imported, HookMissing: hookMissing);

    private static SessionSummary Analyze(bool hookMissing) => SessionAnalyzer.Analyze(Key,
    [
        Ev(1, AgentEventKind.PromptSubmitted, "依頼", hookMissing),
        Ev(2, AgentEventKind.AssistantMessage, "返事", hookMissing),
        Ev(3, AgentEventKind.PromptSubmitted, "もう一つ", hookMissing),
    ]);

    // ---- SessionAnalyzer ----

    [Fact]
    public void Imported_events_marked_hook_missing_make_a_nohook_session_that_is_not_running()
    {
        var s = Analyze(hookMissing: true);

        Assert.Equal(SessionState.NoHook, s.State);
        Assert.False(s.TurnInProgress);
        Assert.DoesNotContain(s.Turns, t => t.Status == TurnStatus.Running);   // 開いたターンは閉じる（Imported と同じ）
        Assert.Equal("依頼", s.AutoTitle);
        Assert.Equal(2, s.PromptCount);
    }

    [Fact]
    public void Imported_events_without_the_mark_stay_imported()
    {
        Assert.Equal(SessionState.Imported, Analyze(hookMissing: false).State);
    }

    [Fact]
    public void One_marked_event_is_enough_for_nohook()
    {
        var s = SessionAnalyzer.Analyze(Key,
        [
            Ev(1, AgentEventKind.PromptSubmitted, "依頼", hookMissing: false),
            Ev(2, AgentEventKind.AssistantMessage, "返事", hookMissing: true),
        ]);

        Assert.Equal(SessionState.NoHook, s.State);
    }

    [Fact]
    public void A_hook_event_in_the_mix_keeps_the_normal_state()
    {
        var s = SessionAnalyzer.Analyze(Key,
        [
            Ev(1, AgentEventKind.PromptSubmitted, "依頼", hookMissing: true),
            Ev(2, AgentEventKind.PromptSubmitted, "hook の依頼", hookMissing: false, imported: false),
        ]);

        Assert.Equal(SessionState.Running, s.State);
    }

    // ---- SessionText ----

    [Fact]
    public void The_state_has_its_own_name_and_label()
    {
        Assert.Equal("Hook なし", SessionText.StateName(SessionState.NoHook));
        Assert.Equal("⚪ Hook なし", SessionText.StateLabel(SessionState.NoHook));
    }

    [Theory]
    [InlineData(SessionState.Imported, true)]
    [InlineData(SessionState.NoHook, true)]
    [InlineData(SessionState.Running, false)]
    [InlineData(SessionState.YourTurn, false)]
    [InlineData(SessionState.Aborted, false)]
    [InlineData(SessionState.Error, false)]
    [InlineData(SessionState.Closed, false)]
    public void IsTranscriptOnly_is_true_for_imported_and_nohook(SessionState state, bool expected) =>
        Assert.Equal(expected, SessionText.IsTranscriptOnly(state));

    [Fact]
    public void NoHook_is_added_at_the_end_of_the_enum()
    {
        var last = Enum.GetValues<SessionState>().Max();

        Assert.Equal(SessionState.NoHook, last);
    }

    // ---- RecentActivity / StalledRule ----

    [Fact]
    public void Recent_inputs_do_not_include_a_nohook_session()
    {
        Assert.Empty(RecentActivity.Inputs([Analyze(hookMissing: true)]));
    }

    [Fact]
    public void A_nohook_session_is_not_changed_by_the_stalled_rule()
    {
        var d = StalledRule.DisplayState(Analyze(hookMissing: true), At.AddDays(5), 10);

        Assert.Equal(SessionState.NoHook, d.State);
        Assert.Null(d.Note);
    }
}
