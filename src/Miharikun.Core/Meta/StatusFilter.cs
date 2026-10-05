using Miharikun.Core.Sessions;

namespace Miharikun.Core.Meta;

/// <summary>一覧のステータス絞り込みチップ（要件 12.2）。All 以外は複数選べる（OR）。</summary>
public enum StatusTab
{
    All,
    Unset,
    Working,
    Paused,
    Done,
}

public static class StatusFilter
{
    public static bool Matches(StatusTab tab, SessionStatus? status) => tab switch
    {
        StatusTab.All => true,
        StatusTab.Unset => status is null,
        StatusTab.Working => status == SessionStatus.Working,
        StatusTab.Paused => status == SessionStatus.Paused,
        StatusTab.Done => status == SessionStatus.Done,
        _ => true,
    };

    /// <summary>選んだチップのどれかに該当するか（OR）。何も選んでいない（All を含む）なら全て通す。</summary>
    public static bool MatchesAny(IReadOnlySet<StatusTab> selected, SessionStatus? status) =>
        selected.Count == 0 || selected.Contains(StatusTab.All) || selected.Any(t => Matches(t, status));

    /// <summary>タブに該当するセッション数。他のフィルタ・検索は掛けず、全セッションの値から数える。</summary>
    public static int Count(StatusTab tab, IEnumerable<SessionStatus?> statuses) => statuses.Count(s => Matches(tab, s));

    public static string Name(StatusTab tab) => tab switch
    {
        StatusTab.All => "全て",
        StatusTab.Unset => SessionText.StatusName(null),
        StatusTab.Working => SessionText.StatusName(SessionStatus.Working),
        StatusTab.Paused => SessionText.StatusName(SessionStatus.Paused),
        StatusTab.Done => SessionText.StatusName(SessionStatus.Done),
        _ => tab.ToString(),
    };
}
