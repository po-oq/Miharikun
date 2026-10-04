using System.Text.Json.Nodes;

namespace Miharikun.Core.Agents;

/// <summary>エージェントの表示用の情報。App はこれだけを見る（AgentCatalog から引く）。</summary>
public interface IAgentInfo
{
    string Id { get; }
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }
}

/// <summary>
/// Hook exe が使う側（AgentRegistry から引く。NativeAOT 互換で実装すること）。
/// 生イベントの正規化など App 側の処理は、各エージェントの具体クラス（と ISessionSource）が持つ。
/// </summary>
public interface IHookAgent : IAgentInfo
{
    string? GetEventName(JsonNode payload);
    string? GetSessionId(JsonNode payload);
    bool NeedsGitSnapshot(string eventName);
    HookResponse RespondToHook(string eventName, JsonNode payload);
}

[Flags]
public enum AgentCapabilities
{
    None = 0,
    RealtimeHooks = 1,
    ToolEvents = 2,
    AssistantText = 4,
    Thinking = 8,
    Compaction = 16,
    Subagents = 32,
    FileEdits = 64,
    SessionEnd = 128,
    TurnStatus = 256,
    Transcript = 512,
    TokenUsage = 1024,
}

/// <summary>Hook exe が返す stdout と終了コード。Stdout が null なら何も出力しない。</summary>
public sealed record HookResponse(string? Stdout, int ExitCode);
