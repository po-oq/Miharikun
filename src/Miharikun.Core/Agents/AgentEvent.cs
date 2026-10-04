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

/// <summary>
/// 全エージェント共通のイベント。App は生 JSON を見ず、これだけを扱う。
/// Imported は「時刻が推定のもの」（Cursor の transcript から取り込んだ導入前のセッション。時刻はファイルの更新日時で、実際の発生時刻ではない）。
/// Output / Duration / Reason / ModelParams / TranscriptPath は 5.1 の目安に追加した項目
/// （成果のテスト実行詳細、継続時間、閉じた理由、モデル表示、transcript サイズに必要）。
/// SubagentId はサブエージェントの識別（Claude Code の agentId。Issue #11）。
/// </summary>
public sealed record AgentEvent(
    SessionKey Session, long Seq, DateTimeOffset At, AgentEventKind Kind,
    string? Text = null, string? ToolName = null, string? ToolUseId = null,
    string? Command = null, int? ExitCode = null, TurnOutcome? Outcome = null,
    string? FilePath = null, CompactionInfo? Compaction = null,
    GitSnapshot? Git = null, string? Model = null,
    string? Output = null, TimeSpan? Duration = null, string? Reason = null,
    string? ModelParams = null, string? TranscriptPath = null,
    bool Imported = false,
    string? SubagentId = null);