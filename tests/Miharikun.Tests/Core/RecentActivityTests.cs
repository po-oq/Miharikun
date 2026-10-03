using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class RecentActivityTests
{
    private static SessionSummary Session(string id, params (string Evt, string Fields, int Sec)[] items)
    {
        var agent = new CursorAgent();
        var events = new List<AgentEvent>();
        for (var i = 0; i < items.Length; i++)
            events.AddRange(agent.Normalize(Raw(items[i].Evt, items[i].Fields, i + 1, items[i].Sec, id)));
        return SessionAnalyzer.Analyze(new SessionKey("cursor", id), events);
    }

    private static (string, string, int) Prompt(string text, int sec) => ("beforeSubmitPrompt", $"\"prompt\":\"{text}\"", sec);
    private static (string, string, int) Stop(int sec) => ("stop", "\"status\":\"completed\"", sec);
    private static (string, string, int) End(int sec) => ("sessionEnd", "\"reason\":\"user_close\"", sec);

    [Fact]
    public void Inputs_come_from_every_session_newest_first_and_are_capped()
    {
        var a = Session("a", Prompt("a1", 10), Stop(11), Prompt("a2", 50), Stop(51));
        var b = Session("b", Prompt("b1", 30), Stop(31), Prompt("b2", 70));

        var inputs = RecentActivity.Inputs([a, b]);

        Assert.Equal(["b2", "a2", "b1", "a1"], inputs.Select(i => i.Text));
        Assert.Equal(["b", "a", "b", "a"], inputs.Select(i => i.Key.SessionId));

        var many = Session("m", Enumerable.Range(0, 15).SelectMany(i => new[] { Prompt("p" + i, i * 10), Stop(i * 10 + 1) }).ToArray());
        var capped = RecentActivity.Inputs([many]);
        Assert.Equal(10, capped.Count);
        Assert.Equal("p14", capped[0].Text);
        Assert.Equal("p5", capped[^1].Text);
        Assert.Equal(3, RecentActivity.Inputs([many], count: 3).Count);
    }

    [Fact]
    public void Inputs_carry_the_seq_of_the_prompt_event_so_the_timeline_can_jump_to_it()
    {
        // 同じセッションに連続して入力した場合でも、行ごとに別の位置を指せる
        var s = Session("s", Prompt("first", 10), Stop(11), Prompt("second", 20), Stop(21));

        var inputs = RecentActivity.Inputs([s]);

        Assert.Equal(["second", "first"], inputs.Select(i => i.Text));
        Assert.Equal(s.Turns.Select(t => t.StartSeq).Reverse().ToArray(), inputs.Select(i => i.Seq).ToArray());
        Assert.NotEqual(inputs[0].Seq, inputs[1].Seq);
    }

    [Fact]
    public void Inputs_ignore_empty_prompts_and_sessions_without_prompts()
    {
        var s = Session("s", ("beforeSubmitPrompt", "\"prompt\":\"  \"", 1), Prompt("real", 2));
        var none = Session("n", ("sessionStart", "", 1));

        Assert.Equal(["real"], RecentActivity.Inputs([s, none]).Select(i => i.Text));
        Assert.Empty(RecentActivity.Inputs([]));
    }

    [Fact]
    public void Closed_lists_currently_closed_sessions_by_end_time_newest_first_and_capped()
    {
        var sessions = Enumerable.Range(0, 7)
            .Select(i => Session("c" + i, Prompt("p", i * 100), Stop(i * 100 + 1), End(i * 100 + 2)))
            .ToList();

        var closed = RecentActivity.Closed(sessions);

        Assert.Equal(["c6", "c5", "c4", "c3", "c2"], closed.Select(c => c.Key.SessionId));
        Assert.Equal(T0.AddSeconds(602), closed[0].ClosedAt);
    }

    [Fact]
    public void Closed_excludes_open_sessions_and_ones_resumed_after_closing()
    {
        var open = Session("open", Prompt("p", 1), Stop(2));
        var resumed = Session("resumed", Prompt("p", 1), Stop(2), End(3), Prompt("again", 4));
        var closed = Session("closed", Prompt("p", 1), Stop(2), End(3));

        var result = RecentActivity.Closed([open, resumed, closed]);

        Assert.Equal(["closed"], result.Select(c => c.Key.SessionId));
        Assert.Equal(SessionState.Running, resumed.State);
        Assert.Equal(T0.AddSeconds(3), resumed.LastSessionEndAt);   // 終了時刻自体は記録されている
    }

    [Fact]
    public void Last_session_end_is_the_latest_one()
    {
        var s = Session("s", Prompt("p", 1), End(5), Prompt("q", 10), End(20));

        Assert.Equal(T0.AddSeconds(20), s.LastSessionEndAt);
        Assert.Null(Session("t", Prompt("p", 1)).LastSessionEndAt);
    }

    [Fact]
    public void Inputs_skip_imported_sessions_because_they_have_no_real_time()
    {
        var key = new SessionKey("cursor", "imp");
        var imported = SessionAnalyzer.Analyze(key,
            [new AgentEvent(key, 1, T0, AgentEventKind.PromptSubmitted, Text: "古い依頼", Imported: true)]);
        var live = Session("live", Prompt("新しい依頼", 5));

        Assert.Equal(["新しい依頼"], RecentActivity.Inputs([imported, live]).Select(i => i.Text));
    }
}
