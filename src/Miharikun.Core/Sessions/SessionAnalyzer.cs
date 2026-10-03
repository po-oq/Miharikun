using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

public sealed class AnalyzerSettings
{
    public static readonly string[] DefaultTestCommandPatterns =
        ["dotnet test", "npx playwright test", "npm test", "npm run test", "vitest", "jest", "pytest"];

    public IReadOnlyList<string> TestCommandPatterns { get; init; } = DefaultTestCommandPatterns;
}

/// <summary>共通イベント（AgentEvent）だけから状態判定と派生値を算出する。Cursor の生 JSON は参照しない。</summary>
public static class SessionAnalyzer
{
    private const int TitleLength = 40;
    private const int OutputTailLength = 2000;

    /// <param name="events">1セッション分。到着順（Seq 昇順）であること。1件以上。</param>
    public static SessionSummary Analyze(SessionKey key, IReadOnlyList<AgentEvent> events, AnalyzerSettings? settings = null)
    {
        if (events.Count == 0)
            throw new ArgumentException("イベントが1件もない", nameof(events));
        settings ??= new AnalyzerSettings();

        // ターン系イベントだけで決まる状態。Closed は「最後が sessionEnd」で別途判定するので、再開すると自然にここへ戻る。
        var turnState = SessionState.YourTurn;
        var running = new Dictionary<string, RunningTool>();
        var turns = new List<TurnInfo>();
        var changedFiles = new List<string>();
        var changedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var testRuns = new List<TestRun>();

        string? autoTitle = null;
        int prompts = 0, stops = 0, tools = 0, subStart = 0, subStop = 0, compactions = 0;
        string? model = null, modelParams = null, branch = null, startHead = null, latestHead = null;
        string? transcript = null, closedReason = null;
        CompactionInfo? lastCompaction = null;
        DateTimeOffset? sessionStartAt = null;
        TimeSpan? endedDuration = null;
        AgentEvent? lastPrompt = null, lastToolResult = null, lastResponse = null;
        var gitSeen = false;

        foreach (var e in events)
        {
            if (e.Model is not null) model = e.Model;
            if (e.ModelParams is not null) modelParams = e.ModelParams;
            if (e.TranscriptPath is not null) transcript = e.TranscriptPath;
            if (e.Git is { } g)
            {
                if (g.Branch is not null) branch = g.Branch;
                if (g.Head is not null)
                {
                    if (!gitSeen) startHead = g.Head;
                    latestHead = g.Head;
                    gitSeen = true;
                }
            }

            switch (e.Kind)
            {
                case AgentEventKind.SessionStarted:
                    sessionStartAt ??= e.At;
                    break;

                case AgentEventKind.SessionEnded:
                    running.Clear();
                    closedReason = e.Reason;
                    if (e.Duration is { } d) endedDuration = d;
                    break;

                case AgentEventKind.PromptSubmitted:
                    prompts++;
                    turnState = SessionState.Running;
                    lastPrompt = e;
                    autoTitle ??= MakeTitle(e.Text);
                    CloseOpenTurn(turns, TurnStatus.Unknown);
                    turns.Add(new TurnInfo(turns.Count + 1, e.Seq, e.At, TurnStatus.Running, e.Text));
                    break;

                case AgentEventKind.TurnEnded:
                    stops++;
                    running.Clear();
                    turnState = e.Outcome switch
                    {
                        TurnOutcome.Aborted => SessionState.Aborted,
                        TurnOutcome.Error => SessionState.Error,
                        _ => SessionState.YourTurn,   // Completed と Unknown は 🟢
                    };
                    CloseOpenTurn(turns, e.Outcome switch
                    {
                        TurnOutcome.Completed => TurnStatus.Completed,
                        TurnOutcome.Aborted => TurnStatus.Aborted,
                        TurnOutcome.Error => TurnStatus.Error,
                        _ => TurnStatus.Unknown,
                    });
                    break;

                case AgentEventKind.ToolStarted:
                    if (e.ToolUseId is not null)
                        running[e.ToolUseId] = new RunningTool(e.ToolUseId, e.ToolName, e.Command, e.At, e.Seq);
                    break;

                case AgentEventKind.ToolSucceeded:
                    tools++;
                    lastToolResult = e;
                    if (e.ToolUseId is not null) running.Remove(e.ToolUseId);
                    if (IsTestRun(e, settings))
                        testRuns.Add(new TestRun(e.Seq, e.At, e.Command!, e.ExitCode, e.Duration, Tail(e.Output)));
                    break;

                case AgentEventKind.ToolFailed:
                    tools++;
                    lastToolResult = e;
                    if (e.ToolUseId is not null) running.Remove(e.ToolUseId);
                    break;

                case AgentEventKind.AssistantMessage:
                    lastResponse = e;
                    break;

                case AgentEventKind.SubagentStarted:
                    subStart++;
                    break;

                case AgentEventKind.SubagentStopped:
                    subStop++;
                    break;

                case AgentEventKind.FileEdited:
                    if (e.FilePath is not null && changedSet.Add(e.FilePath))
                        changedFiles.Add(e.FilePath);
                    break;

                case AgentEventKind.Compacted:
                    compactions++;
                    lastCompaction = e.Compaction;
                    break;
            }
        }

        var last = events[^1];
        var closed = last.Kind == AgentEventKind.SessionEnded;
        var startedAt = sessionStartAt ?? events[0].At;

        if (turns.Count > 0 && turns[^1].Status == TurnStatus.Running && closed)
            CloseOpenTurn(turns, TurnStatus.Unknown);

        return new SessionSummary(
            key,
            closed ? SessionState.Closed : turnState,
            turnState == SessionState.Running,
            autoTitle,
            prompts, stops,
            startedAt, last.At,
            endedDuration ?? (last.At - startedAt),
            model, modelParams, branch, startHead, latestHead,
            tools,
            Math.Max(0, subStart - subStop), subStart,
            compactions, lastCompaction,
            transcript, closedReason,
            changedFiles, testRuns, [.. running.Values], turns,
            lastPrompt, lastToolResult, lastResponse);
    }

    private static void CloseOpenTurn(List<TurnInfo> turns, TurnStatus status)
    {
        if (turns.Count > 0 && turns[^1].Status == TurnStatus.Running)
            turns[^1] = turns[^1] with { Status = status };
    }

    private static bool IsTestRun(AgentEvent e, AnalyzerSettings settings)
    {
        if (e.ToolName != "Shell" || string.IsNullOrEmpty(e.Command))
            return false;

        foreach (var pattern in settings.TestCommandPatterns)
        {
            if (e.Command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? Tail(string? output) =>
        output is null || output.Length <= OutputTailLength ? output : output[^OutputTailLength..];

    /// <summary>最初の依頼の先頭40文字。改行は空白に。サロゲートペアは割らない。</summary>
    private static string? MakeTitle(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return null;

        var flat = prompt.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (flat.Length <= TitleLength)
            return flat;

        var cut = TitleLength;
        if (char.IsHighSurrogate(flat[cut - 1]))
            cut--;
        return flat[..cut];
    }
}