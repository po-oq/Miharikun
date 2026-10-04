namespace Miharikun.Core.Agents;

/// <summary>--agent の値から IHookAgent 実装を選ぶ（Hook exe 用）。リフレクションを使わないよう固定の switch にしている。</summary>
public static class AgentRegistry
{
    public static IHookAgent? Find(string? id) => id switch
    {
        CursorAgent.AgentId => new CursorAgent(),
        _ => null,
    };
}
