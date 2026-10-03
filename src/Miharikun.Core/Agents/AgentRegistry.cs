namespace Miharikun.Core.Agents;

/// <summary>--agent の値から IAgent 実装を選ぶ。リフレクションを使わないよう固定の switch にしている。</summary>
public static class AgentRegistry
{
    public static IAgent? Find(string? id) => id switch
    {
        CursorAgent.AgentId => new CursorAgent(),
        _ => null,
    };
}