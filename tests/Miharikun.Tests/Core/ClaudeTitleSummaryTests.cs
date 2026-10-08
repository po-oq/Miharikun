using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Issue #23（計画 7.2〜7.4）：Claude のタイトル（TitleChanged）が AutoTitle になり、ほかの値を変えないこと。</summary>
public sealed class ClaudeTitleSummaryTests
{
    private static readonly SessionKey Key = new("claude", "sess-1");

    private static List<AgentEvent> Normalize(params string[] lines)
    {
        var n = new ClaudeTranscriptNormalizer("sess-1", "sess-1.jsonl");
        var events = new List<AgentEvent>();
        for (var i = 0; i < lines.Length; i++)
            events.AddRange(n.NormalizeLine(i + 1, lines[i]));
        return events;
    }

    private static SessionSummary Analyze(List<AgentEvent> events) => SessionAnalyzer.Analyze(Key, events);

    [Fact]
    public void Without_a_title_the_first_prompt_is_used()
    {
        var b = new ClaudeLogBuilder();
        Assert.Equal("最初の依頼", Analyze(Normalize(b.User("最初の依頼"))).AutoTitle);
    }

    [Fact]
    public void The_agent_title_wins_over_the_first_prompt_and_is_not_cut()
    {
        var b = new ClaudeLogBuilder();
        var title = new string('あ', 60);

        var s = Analyze(Normalize(b.User("最初の依頼"), b.CustomTitle(title)));

        Assert.Equal(title, s.AutoTitle);
    }

    [Fact]
    public void The_last_title_is_used()
    {
        var b = new ClaudeLogBuilder();
        Assert.Equal("B", Analyze(Normalize(b.User("a"), b.CustomTitle("A"), b.CustomTitle("B"))).AutoTitle);
    }

    [Fact]
    public void A_title_alone_without_any_prompt_is_used()
    {
        var b = new ClaudeLogBuilder();
        Assert.Equal("A", Analyze(Normalize(b.Other("queue-operation"), b.User("x", ClaudeLogBuilder.Origin.TaskNotification), b.CustomTitle("A"))).AutoTitle);
    }

    [Fact]
    public void A_blank_title_event_is_ignored()
    {
        var key = Key;
        var at = ClaudeLogBuilder.Start;
        var events = new List<AgentEvent>
        {
            new(key, 1, at, AgentEventKind.SessionStarted),
            new(key, 2, at, AgentEventKind.PromptSubmitted, Text: "依頼"),
            new(key, 3, at, AgentEventKind.TitleChanged, Text: "  "),
            new(key, 4, at, AgentEventKind.TitleChanged),
        };

        Assert.Equal("依頼", Analyze(events).AutoTitle);
    }

    [Fact]
    public void No_prompt_and_no_title_is_null()
    {
        Assert.Null(Analyze(Normalize(new ClaudeLogBuilder().Other("queue-operation"))).AutoTitle);
    }

    [Fact]
    public void A_title_changes_nothing_else_in_the_summary_or_the_timeline()
    {
        string[] Log(bool withTitle)
        {
            var b = new ClaudeLogBuilder { Step = 1 };
            var lines = new List<string> { b.User("依頼"), b.Bash("t1", "dotnet test") };
            if (withTitle) lines.Add(b.CustomTitle("タイトル"));
            lines.AddRange([b.ToolResult("t1", "合格"), b.AssistantText("終わり", stopReason: "end_turn")]);
            if (withTitle) lines.AddRange([b.CustomTitle("タイトル"), b.CustomTitle("別の名前")]);   // 最後のイベントが TitleChanged になるログ
            return [.. lines];
        }

        var plainEvents = Normalize(Log(false));
        var titledEvents = Normalize(Log(true));
        var plain = Analyze(plainEvents);
        var titled = Analyze(titledEvents);

        Assert.Equal(AgentEventKind.TitleChanged, titledEvents[^1].Kind);
        Assert.Equal("別の名前", titled.AutoTitle);
        Assert.Equal(plain.State, titled.State);
        Assert.Equal(plain.TurnInProgress, titled.TurnInProgress);
        Assert.Equal(plain.PromptCount, titled.PromptCount);
        Assert.Equal(plain.TurnCount, titled.TurnCount);
        Assert.Equal(plain.StartedAt, titled.StartedAt);
        Assert.Equal(plain.LastActivityAt, titled.LastActivityAt);
        Assert.Equal(plain.Duration, titled.Duration);
        Assert.Equal(plain.Branch, titled.Branch);
        Assert.Equal(plain.ChangedFiles, titled.ChangedFiles);
        Assert.Equal(plain.Turns.Count, titled.Turns.Count);
        Assert.Equal(plain.Model, titled.Model);
        // 行番号（Seq）はタイトルの行の分だけずれるので比べない
        Assert.Equal(TimelineBuilder.Build(plainEvents).Select(i => (i.Kind, i.Text)),
            TimelineBuilder.Build(titledEvents).Select(i => (i.Kind, i.Text)));
    }

    [Fact]
    public void The_search_text_contains_the_title()
    {
        var b = new ClaudeLogBuilder();
        var events = Normalize(b.User("依頼"), b.CustomTitle("みつけて"));
        var summary = Analyze(events);

        Assert.True(SessionSearch.Matches(SessionSearch.BuildSearchText(summary, events), "みつけて"));
    }
}
