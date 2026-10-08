using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Presentation;

/// <summary>Issue #23（計画 7.5）：カードの表示は「手動タイトル → エージェントのタイトル → 最初の依頼」。</summary>
public sealed class AgentTitleViewModelTests : IDisposable
{
    private readonly MainVmHarness _h = new();
    private static readonly DateTimeOffset T = MainVmHarness.Now;

    public void Dispose() => _h.Dispose();

    private static SessionSnapshot WithTitle(string id, string agent, string prompt, string? title)
    {
        var key = MainVmHarness.KeyOf(id, agent);
        var events = new List<AgentEvent>
        {
            new(key, 1, T.AddSeconds(-3), AgentEventKind.PromptSubmitted, Text: prompt),
            new(key, 2, T.AddSeconds(-1), AgentEventKind.AssistantMessage, Text: "返事"),
            new(key, 3, T, AgentEventKind.TurnEnded, Outcome: TurnOutcome.Completed),
        };
        if (title is not null)
            events.Add(new(key, 4, T, AgentEventKind.TitleChanged, Text: title));
        var summary = SessionAnalyzer.Analyze(key, events);
        return new SessionSnapshot(summary, SessionSearch.BuildSearchText(summary, events));
    }

    [Fact]
    public void The_agent_title_is_shown_and_a_manual_title_wins_until_it_is_emptied()
    {
        _h.Add(WithTitle("c", "claude", "最初の依頼", "Claude のつけた名前"),
               WithTitle("x", "cursor", "Cursor の依頼", null));
        var card = _h.Card("c");

        Assert.Equal("Claude のつけた名前", card.Title);
        Assert.Equal("Cursor の依頼", _h.Card("x").Title);   // Cursor は今まで通り

        card.Rename.BeginCommand.Execute(null);
        Assert.Equal("Claude のつけた名前", card.Rename.Text);   // 編集の初期値は、いま見えているタイトル
        card.Rename.Text = "手動の名前";
        card.Rename.CommitCommand.Execute(null);
        Assert.Equal("手動の名前", card.Title);

        // 新しい Claude のタイトルが来ても、手動が優先
        _h.Add(WithTitle("c", "claude", "最初の依頼", "Claude が変えた名前"));
        Assert.Equal("手動の名前", _h.Card("c").Title);

        card = _h.Card("c");
        card.Rename.BeginCommand.Execute(null);
        card.Rename.Text = "";
        card.Rename.CommitCommand.Execute(null);
        Assert.Equal("Claude が変えた名前", _h.Card("c").Title);   // 空にすると自動（Claude のタイトル）に戻る
    }

    [Fact]
    public void A_changed_agent_title_follows_when_there_is_no_manual_title()
    {
        _h.Add(WithTitle("c", "claude", "依頼", "A"));
        Assert.Equal("A", _h.Card("c").Title);

        _h.Add(WithTitle("c", "claude", "依頼", "B"));

        Assert.Equal("B", _h.Card("c").Title);
    }
}
