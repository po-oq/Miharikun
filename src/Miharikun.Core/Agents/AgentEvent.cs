namespace Miharikun.Core.Agents;

public sealed record SessionKey(string AgentId, string SessionId);

public enum AgentEventKind
{
    SessionStarted, SessionEnded, PromptSubmitted, TurnEnded,
    ToolStarted, ToolSucceeded, ToolFailed,
    AssistantMessage, AssistantThought,
    SubagentStarted, SubagentStopped, FileEdited, Compacted,
}

public enum TurnOutcome { Completed, Aborted, Error, Unknown }

public sealed record GitSnapshot(string? Branch, string? Head);

public sealed record CompactionInfo(string? Trigger, int? ContextUsagePercent);

public sealed record AgentEvent(
    SessionKey Session, long Seq, DateTimeOffset At, AgentEventKind Kind,
    string? Text = null, string? ToolName = null, string? ToolUseId = null,
    string? Command = null, int? ExitCode = null, TurnOutcome? Outcome = null,
    string? FilePath = null, CompactionInfo? Compaction = null,
    GitSnapshot? Git = null, string? Model = null);