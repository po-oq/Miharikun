using Miharikun.Core.Agents;

namespace Miharikun.Tests.Core;

public sealed class AgentCatalogTests
{
    [Fact]
    public void Claude_code_is_in_the_catalog_with_its_capabilities()
    {
        var agent = AgentCatalog.Find("claude");

        Assert.NotNull(agent);
        Assert.Equal("claude", agent.Id);
        Assert.Equal("Claude Code", agent.DisplayName);
        const AgentCapabilities expected =
            AgentCapabilities.ToolEvents | AgentCapabilities.AssistantText | AgentCapabilities.Thinking |
            AgentCapabilities.Subagents | AgentCapabilities.FileEdits | AgentCapabilities.TurnStatus | AgentCapabilities.Transcript;
        Assert.Equal(expected, agent.Capabilities);
        Assert.False(agent.Capabilities.HasFlag(AgentCapabilities.RealtimeHooks));
        Assert.False(agent.Capabilities.HasFlag(AgentCapabilities.SessionEnd));
        Assert.False(agent.Capabilities.HasFlag(AgentCapabilities.Compaction));
    }

    [Fact]
    public void Cursor_is_in_the_catalog_and_unknown_ids_are_not()
    {
        Assert.Equal("Cursor", AgentCatalog.Find("cursor")?.DisplayName);
        Assert.Null(AgentCatalog.Find("codex"));
        Assert.Null(AgentCatalog.Find(null));
    }

    [Fact]
    public void Claude_code_has_no_hook_agent()
    {
        // Hook exe は Cursor 専用。Claude Code は会話ログだけで読む。
        Assert.Null(AgentRegistry.Find("claude"));
        Assert.NotNull(AgentRegistry.Find("cursor"));
    }
}
