namespace Miharikun.Core.Agents;

/// <summary>
/// Claude Code（CLI と Code タブ）。会話ログだけで読む（Hook は使わない）ので、IAgentInfo だけを実装する（要件 5.1）。
/// 会話ログの 1 行 → 共通イベントの変換は <see cref="ClaudeTranscriptNormalizer"/>、読み込みは ClaudeSessionSource（Phase 20）。
/// </summary>
public sealed class ClaudeCodeAgent : IAgentInfo
{
    public const string AgentId = "claude";

    public string Id => AgentId;
    public string DisplayName => "Claude Code";

    /// <summary>RealtimeHooks・SessionEnd・Compaction は無し（Compaction は実機で確認できたら追加。TokenUsage は扱わない）。</summary>
    public AgentCapabilities Capabilities =>
        AgentCapabilities.ToolEvents | AgentCapabilities.AssistantText | AgentCapabilities.Thinking |
        AgentCapabilities.Subagents | AgentCapabilities.FileEdits | AgentCapabilities.TurnStatus |
        AgentCapabilities.Transcript;
}
