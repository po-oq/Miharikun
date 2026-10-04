using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

/// <summary>タイムラインのフィルタ種別（要件 12.4）。Other（セッション開始・終了）はフィルタ対象外で常に表示する。</summary>
public enum TimelineKind { Input, Response, Thought, Tool, Compaction, Other }

/// <summary>
/// タイムライン1行。ツールは開始（preToolUse）の位置に置き、結果（post*）が来たら同じ行を完了表示に更新する。
/// そのため Seq は開始側、EndSeq は結果側のイベント番号。
/// </summary>
public sealed record TimelineItem(
    long Seq, long? EndSeq, DateTimeOffset At, TimelineKind Kind, string Text, bool IsRunning = false, bool HasTime = true);

public static class TimelineBuilder
{
    public static List<TimelineItem> Build(IReadOnlyList<AgentEvent> events)
    {
        var items = new List<TimelineItem>(events.Count);
        var open = new Dictionary<string, int>();   // tool_use_id → items のインデックス

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case AgentEventKind.SessionStarted:
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Other, "セッション開始"));
                    break;

                case AgentEventKind.SessionEnded:
                    CloseOpenTools(items, open);
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Other,
                        e.Reason is null ? "セッション終了" : $"セッション終了（{e.Reason}）"));
                    break;

                case AgentEventKind.TurnEnded:
                    CloseOpenTools(items, open);
                    break;

                case AgentEventKind.PromptSubmitted:
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Input, TextOr(e.Text, "（依頼文を取得できませんでした）"), HasTime: !e.Imported));
                    break;

                case AgentEventKind.AssistantMessage:
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Response, TextOr(e.Text, MissingText), HasTime: !e.Imported));
                    break;

                case AgentEventKind.AssistantThought:
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Thought, TextOr(e.Text, MissingText), HasTime: !e.Imported));
                    break;

                case AgentEventKind.Compacted:
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Compaction, CompactionText(e)));
                    break;

                case AgentEventKind.ToolStarted:
                    items.Add(new(e.Seq, null, e.At, TimelineKind.Tool,
                        SessionText.ToolLabel(e.ToolName, e.Command) + "（実行中）", IsRunning: true));
                    if (e.ToolUseId is not null)
                        open[e.ToolUseId] = items.Count - 1;
                    break;

                case AgentEventKind.ToolSucceeded:
                case AgentEventKind.ToolFailed:
                    var text = FinishedToolText(e);
                    if (e.ToolUseId is not null && open.Remove(e.ToolUseId, out var index))
                        items[index] = items[index] with { EndSeq = e.Seq, Text = text, IsRunning = false };
                    else
                        items.Add(new(e.Seq, e.Seq, e.At, TimelineKind.Tool, text));   // pre が記録されていない場合
                    break;
            }
        }
        return items;
    }

    /// <summary>3行サマリーなどのイベント番号から、タイムライン上の行を探す。</summary>
    public static TimelineItem? Find(IEnumerable<TimelineItem> items, long seq) =>
        items.FirstOrDefault(i => i.Seq == seq || i.EndSeq == seq);

    /// <summary>
    /// イベント番号と種類から行を探す。<b>番号と種類が両方一致する行を優先</b>し、無ければ番号だけで探す。
    /// Claude Code は 1 行のログから複数のイベントが出て、同じ番号になることがある（セッション開始と最初の依頼など）。
    /// </summary>
    public static TimelineItem? Find(IEnumerable<TimelineItem> items, long seq, TimelineKind kind)
    {
        var list = items as IReadOnlyList<TimelineItem> ?? items.ToList();
        return list.FirstOrDefault(i => i.Kind == kind && (i.Seq == seq || i.EndSeq == seq)) ?? Find(list, seq);
    }

    // 入力が壊れて本文を取れなかったイベントが、空白の行にならないように
    private const string MissingText = "（本文を取得できませんでした）";

    private static string TextOr(string? text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text;

    private static void CloseOpenTools(List<TimelineItem> items, Dictionary<string, int> open)
    {
        foreach (var index in open.Values)
        {
            var item = items[index];
            items[index] = item with { Text = item.Text.Replace("（実行中）", "（終了・結果不明）"), IsRunning = false };
        }
        open.Clear();
    }

    private static string FinishedToolText(AgentEvent e)
    {
        var parts = new List<string>();
        if (e.Kind == AgentEventKind.ToolFailed)
            parts.Add(e.Reason is null ? "失敗" : $"失敗: {e.Reason}");
        if (e.Duration is { } d)
            parts.Add(d < TimeSpan.FromSeconds(1) ? "1秒未満" : $"{(int)d.TotalSeconds}秒");
        if (e.ExitCode is { } code)
            parts.Add($"exit {code}");

        var label = SessionText.ToolLabel(e.ToolName, e.Command);
        return parts.Count == 0 ? label : $"{label}（{string.Join("・", parts)}）";
    }

    private static string CompactionText(AgentEvent e)
    {
        var parts = new List<string>();
        if (e.Compaction?.Trigger is { } trigger) parts.Add(trigger);
        if (e.Compaction?.ContextUsagePercent is { } pct) parts.Add($"{pct}%");
        return parts.Count == 0 ? "圧縮" : $"圧縮（{string.Join("・", parts)}）";
    }
}

/// <summary>3行サマリーの各行がジャンプする先（要件 12.3）。該当するイベントがなければ null。</summary>
public static class SummaryJump
{
    public static (long Seq, TimelineKind Kind)? Prompt(SessionSummary s) =>
        s.LastPrompt is { } e ? (e.Seq, TimelineKind.Input) : null;

    /// <summary>実行中ツールがあればその開始、なければ最後のツール結果。</summary>
    public static (long Seq, TimelineKind Kind)? Tool(SessionSummary s)
    {
        if (s.RunningTools.Count > 0)
            return (s.RunningTools.MaxBy(t => t.Seq)!.Seq, TimelineKind.Tool);
        return s.LastToolResult is { } e ? (e.Seq, TimelineKind.Tool) : null;
    }

    public static (long Seq, TimelineKind Kind)? Response(SessionSummary s) =>
        s.LastResponse is { } e ? (e.Seq, TimelineKind.Response) : null;
}