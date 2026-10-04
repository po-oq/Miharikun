using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

public enum SessionState { Running, YourTurn, Aborted, Error, Closed, Imported }

public enum TurnStatus { Running, Completed, Aborted, Error, Unknown }

public sealed record RunningTool(string ToolUseId, string? ToolName, string? Command, DateTimeOffset StartedAt, long Seq);

public sealed record TestRun(long Seq, DateTimeOffset At, string Command, int? ExitCode, TimeSpan? Duration, string? OutputTail)
{
    /// <summary>exitCode が取れなかったときは null。</summary>
    public bool? Succeeded => ExitCode is null ? null : ExitCode == 0;
}

/// <summary>
/// サブエージェント 1 件の履歴。EndedAt が null なら動いている。
/// Elapsed は、イベントが所要時間を持っていればそれ（Cursor）、なければ終了 − 開始。
/// </summary>
public sealed record SubagentInfo(
    string? SubagentId, string? ToolUseId, string? Description, string? Type,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, TimeSpan? Elapsed)
{
    public bool Running => EndedAt is null;
}

public sealed record TurnInfo(int Number, long StartSeq, DateTimeOffset StartedAt, TurnStatus Status, string? Prompt);

/// <summary>共通イベントから算出した、セッション1つ分の状態と派生値（要件 10章）。</summary>
public sealed record SessionSummary(
    SessionKey Key,
    SessionState State,
    bool TurnInProgress,
    string? AutoTitle,
    int PromptCount,
    int TurnCount,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    TimeSpan Duration,
    string? Model,
    string? ModelParams,
    string? Branch,
    string? StartBranch,
    string? StartHead,
    string? LatestHead,
    int ToolCallCount,
    int SubagentsRunning,
    int SubagentsTotal,
    IReadOnlyList<SubagentInfo> Subagents,
    int CompactionCount,
    CompactionInfo? LastCompaction,
    string? TranscriptPath,
    string? ClosedReason,
    DateTimeOffset? LastSessionEndAt,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<TestRun> TestRuns,
    IReadOnlyList<RunningTool> RunningTools,
    IReadOnlyList<TurnInfo> Turns,
    AgentEvent? LastPrompt,
    AgentEvent? LastToolResult,
    AgentEvent? LastResponse);