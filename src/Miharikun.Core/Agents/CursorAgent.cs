using System.Text.Json.Nodes;

namespace Miharikun.Core.Agents;

public sealed partial class CursorAgent : IAgent
{
    public const string AgentId = "cursor";

    private const string HookResponseEmpty = "{}";
    private const string HookResponseContinue = "{\"continue\":true}";

    public string Id => AgentId;
    public string DisplayName => "Cursor";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.RealtimeHooks | AgentCapabilities.ToolEvents | AgentCapabilities.AssistantText |
        AgentCapabilities.Thinking | AgentCapabilities.Compaction | AgentCapabilities.Subagents |
        AgentCapabilities.FileEdits | AgentCapabilities.SessionEnd | AgentCapabilities.TurnStatus;

    public string? GetEventName(JsonNode payload) => GetString(payload, "hook_event_name");

    public string? GetSessionId(JsonNode payload) => GetString(payload, "conversation_id");

    public bool NeedsGitSnapshot(string eventName) => eventName is "sessionStart" or "stop";

    /// <summary>
    /// 承認系 hook は何も出力せず exit 1（判定に参加しない）。beforeSubmitPrompt は送信を止めないよう continue:true。
    /// </summary>
    public HookResponse RespondToHook(string eventName, JsonNode payload) => eventName switch
    {
        "preToolUse" or "subagentStart" => new HookResponse(null, 1),
        "beforeSubmitPrompt" => new HookResponse(HookResponseContinue, 0),
        _ => new HookResponse(HookResponseEmpty, 0),
    };

    public IReadOnlyList<string> GetWorkspaceRoots(RawEventRecord raw) => GetWorkspaceRoots(raw.Payload);

    public static IReadOnlyList<string> GetWorkspaceRoots(JsonNode payload)
    {
        if (payload is not JsonObject obj || obj["workspace_roots"] is not JsonArray arr)
            return [];

        var roots = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                roots.Add(s);
        }
        return roots;
    }

    private static string? GetString(JsonNode payload, string name) =>
        payload is JsonObject obj && obj[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}