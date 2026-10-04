using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

/// <summary>絞り込みのチップ 1 つ。Key が空文字なら「全て」。</summary>
public sealed record AgentFilterOption(string Key, string Name);

/// <summary>
/// 一覧のエージェント絞り込み（全て / Cursor / Claude Code。要件 12.8）。1 つだけ選ぶ。
/// ステータスの絞り込み（StatusFilter）と同じ流儀で、件数は他のフィルタ・検索を掛けずに、全カードから数える。
/// </summary>
public static class AgentFilter
{
    /// <summary>「全て」を表すキー。</summary>
    public const string All = "";

    /// <summary>全て、そのあとに AgentCatalog の並び。名前は表示名。</summary>
    public static IReadOnlyList<AgentFilterOption> Options { get; } =
        [new AgentFilterOption(All, "全て"), .. AgentCatalog.All.Select(a => new AgentFilterOption(a.Id, a.DisplayName))];

    /// <summary>選んだキー（All なら全部）に、そのカードのエージェントが当てはまるか。</summary>
    public static bool Matches(string selectedKey, string agentId) =>
        selectedKey.Length == 0 || string.Equals(selectedKey, agentId, StringComparison.Ordinal);

    /// <summary>チップに出す件数。「全て」は全カード、それ以外はそのエージェントのカード。</summary>
    public static int Count(string key, IEnumerable<string> agentIds) => agentIds.Count(id => Matches(key, id));
}
