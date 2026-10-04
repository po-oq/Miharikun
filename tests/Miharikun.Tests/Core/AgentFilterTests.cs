using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Phase 21-2：エージェントの絞り込み（全て / Cursor / Claude Code。要件 12.8）。</summary>
public sealed class AgentFilterTests
{
    [Fact]
    public void Options_are_all_then_each_agent_by_display_name()
    {
        Assert.Equal([("", "全て"), ("cursor", "Cursor"), ("claude", "Claude Code")],
            AgentFilter.Options.Select(o => (o.Key, o.Name)));
        Assert.Equal(AgentFilter.All, AgentFilter.Options[0].Key);
    }

    [Theory]
    [InlineData("", "cursor", true)]       // 全て
    [InlineData("", "claude", true)]
    [InlineData("", "something-new", true)]
    [InlineData("cursor", "cursor", true)]
    [InlineData("cursor", "claude", false)]
    [InlineData("claude", "claude", true)]
    [InlineData("claude", "cursor", false)]
    [InlineData("claude", "Claude", false)]   // ID は大文字小文字を区別する（ID は固定の小文字）
    public void Matches_by_the_selected_key(string selected, string agentId, bool expected)
    {
        Assert.Equal(expected, AgentFilter.Matches(selected, agentId));
    }

    [Fact]
    public void Counts_come_from_all_the_cards_for_each_chip()
    {
        string[] ids = ["cursor", "claude", "claude", "cursor", "claude"];

        Assert.Equal(5, AgentFilter.Count(AgentFilter.All, ids));
        Assert.Equal(2, AgentFilter.Count("cursor", ids));
        Assert.Equal(3, AgentFilter.Count("claude", ids));
        Assert.Equal(0, AgentFilter.Count("claude", []));
        Assert.Equal(0, AgentFilter.Count("cursor", ["claude"]));
    }
}
