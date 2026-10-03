using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

public sealed record RecentInput(SessionKey Key, DateTimeOffset At, string Text);

public sealed record RecentClosed(SessionKey Key, DateTimeOffset ClosedAt);

/// <summary>右ペインの「最近の入力」「最近閉じたセッション」（要件 12.4）。このプロジェクトの全セッションが対象。</summary>
public static class RecentActivity
{
    public const int InputCount = 10;
    public const int ClosedCount = 5;

    /// <summary>beforeSubmitPrompt の新しい順。ターン一覧がそのまま「依頼」の並びなので、そこから集める。</summary>
    public static IReadOnlyList<RecentInput> Inputs(IEnumerable<SessionSummary> sessions, int count = InputCount) =>
        sessions
            .SelectMany(s => s.Turns
                .Where(t => !string.IsNullOrWhiteSpace(t.Prompt) && s.State != SessionState.Imported)
                .Select(t => new RecentInput(s.Key, t.StartedAt, t.Prompt!)))
            .OrderByDescending(i => i.At)
            .Take(count)
            .ToList();

    /// <summary>
    /// 閉じたセッションを、閉じた（sessionEnd）新しい順に。閉じたあと再開して動いているセッションは
    /// 「最近閉じた」ではなくなるので含めない（一覧のカードは 🔵🟢 などに戻っている）。
    /// </summary>
    public static IReadOnlyList<RecentClosed> Closed(IEnumerable<SessionSummary> sessions, int count = ClosedCount) =>
        sessions
            .Where(s => s.State == SessionState.Closed && s.LastSessionEndAt is not null)
            .Select(s => new RecentClosed(s.Key, s.LastSessionEndAt!.Value))
            .OrderByDescending(c => c.ClosedAt)
            .Take(count)
            .ToList();
}