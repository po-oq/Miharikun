using System.Text.Json.Nodes;

namespace Miharikun.Core.Agents;

public interface IAgent
{
    string Id { get; }
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }

    // Hook exe 側（NativeAOT 互換で実装すること）
    string? GetEventName(JsonNode payload);
    string? GetSessionId(JsonNode payload);
    bool NeedsGitSnapshot(string eventName);
    HookResponse RespondToHook(string eventName, JsonNode payload);

    // App 側
    IReadOnlyList<string> GetWorkspaceRoots(RawEventRecord raw);
    IEnumerable<AgentEvent> Normalize(RawEventRecord raw);
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