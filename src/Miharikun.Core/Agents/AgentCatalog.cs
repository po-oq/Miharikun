namespace Miharikun.Core.Agents;

/// <summary>AgentId から表示名・Capabilities を引く（App 用。Hook を持たないエージェントも含む）。固定の switch。</summary>
public static class AgentCatalog
{
    /// <summary>画面に出すエージェントの一覧（絞り込みのチップの並び）。</summary>
    public static IReadOnlyList<IAgentInfo> All { get; } = [new CursorAgent(), new ClaudeCodeAgent()];

    public static IAgentInfo? Find(string? agentId) => agentId switch
    {
        CursorAgent.AgentId => new CursorAgent(),
        ClaudeCodeAgent.AgentId => new ClaudeCodeAgent(),
        _ => null,
    };
}
