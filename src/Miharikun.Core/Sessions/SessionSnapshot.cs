using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

/// <summary>画面へ渡す、スレッド間で安全に共有できる不変のセッション情報。</summary>
/// <param name="SearchText">全文検索の対象（タイトル・全プロンプト・変更ファイル名）。メモ・概要は Phase 6 で App 側が足す。</param>
public sealed record SessionSnapshot(SessionSummary Summary, string SearchText);

public sealed record SessionUpdate(IReadOnlyList<SessionSnapshot> Upserts, IReadOnlyList<SessionKey> Removed);

public static class SessionSearch
{
    /// <summary>インクリメンタル検索。大文字小文字は無視し、空のクエリは全件一致。</summary>
    public static bool Matches(string searchText, string? query)
    {
        var q = query?.Trim();
        return string.IsNullOrEmpty(q) || searchText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildSearchText(SessionSummary summary, IEnumerable<AgentEvent> events)
    {
        var parts = new List<string>();
        if (summary.AutoTitle is not null)
            parts.Add(summary.AutoTitle);
        parts.AddRange(events.Where(e => e.Kind == AgentEventKind.PromptSubmitted && !string.IsNullOrEmpty(e.Text)).Select(e => e.Text!));
        parts.AddRange(summary.ChangedFiles);
        return string.Join('\n', parts);
    }
}