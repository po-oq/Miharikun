using System.Text.Json.Nodes;

namespace Miharikun.Tests.Core;

/// <summary>
/// Claude Code の会話ログ（JSONL）の行を、構造だけ真似て手書きで組み立てる（実ログのコピーは使わない。要件 11.1・計画 19 章）。
/// 時刻は、呼ぶたびに <see cref="Step"/> 秒ずつ進む。
/// </summary>
public sealed class ClaudeLogBuilder(string sessionId = "sess-1", string cwd = @"C:\work\proj", string? branch = "main", string version = "2.1.200")
{
    public static readonly DateTimeOffset Start = new(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);   // ログの時刻は UTC

    public enum Origin { Human, None, TaskNotification }

    private int _n;
    private int _msg;

    public int Step { get; set; } = 1;

    /// <summary>直前に作った行の時刻。</summary>
    public DateTimeOffset LastAt { get; private set; } = Start;

    private JsonObject Base(string type, bool withMeta = true)
    {
        LastAt = Start.AddSeconds(++_n * Step);
        var o = new JsonObject { ["type"] = type };
        if (withMeta)
        {
            o["uuid"] = "u-" + _n;
            o["sessionId"] = sessionId;
            o["cwd"] = cwd;
            o["timestamp"] = LastAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            if (branch is not null)
                o["gitBranch"] = branch;
            o["version"] = version;
        }
        return o;
    }

    public static string S(JsonNode node) => node.ToJsonString(new System.Text.Json.JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>人の入力（文字だけの行）。</summary>
    public string User(string text, Origin origin = Origin.Human, bool isMeta = false)
    {
        var o = Base("user");
        o["message"] = new JsonObject { ["role"] = "user", ["content"] = text };
        ApplyOrigin(o, origin);
        if (isMeta)
            o["isMeta"] = true;
        return S(o);
    }

    /// <summary>人の入力（text ブロックの配列。割り込みの記録などはこの形）。</summary>
    public string UserBlocks(string text, Origin origin = Origin.None)
    {
        var o = Base("user");
        o["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };
        ApplyOrigin(o, origin);
        return S(o);
    }

    private static void ApplyOrigin(JsonObject o, Origin origin)
    {
        switch (origin)
        {
            case Origin.Human: o["origin"] = new JsonObject { ["kind"] = "human" }; break;
            case Origin.TaskNotification: o["origin"] = new JsonObject { ["kind"] = "task-notification" }; break;
        }
    }

    /// <summary>ツールの結果（user の行。content は tool_result の配列）。</summary>
    public string ToolResult(string toolUseId, string content, bool isError = false, JsonObject? toolUseResult = null, bool contentAsBlocks = false)
    {
        var o = Base("user");
        JsonNode body = contentAsBlocks
            ? new JsonArray(new JsonObject { ["type"] = "text", ["text"] = content })
            : JsonValue.Create(content)!;
        o["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolUseId,
                ["content"] = body,
                ["is_error"] = isError,
            }),
        };
        if (toolUseResult is not null)
            o["toolUseResult"] = toolUseResult;
        return S(o);
    }

    private JsonObject Assistant(JsonObject block, string? model, string? stopReason, string? messageId)
    {
        var o = Base("assistant");
        o["message"] = new JsonObject
        {
            ["id"] = messageId ?? "msg-" + (++_msg),
            ["role"] = "assistant",
            ["model"] = model,
            ["content"] = new JsonArray(block),
            ["stop_reason"] = stopReason,
        };
        return o;
    }

    public string AssistantText(string text, string? model = "claude-x", string? stopReason = null, string? messageId = null) =>
        S(Assistant(new JsonObject { ["type"] = "text", ["text"] = text }, model, stopReason, messageId));

    public string AssistantThinking(string text, string? model = "claude-x", string? stopReason = null, string? messageId = null) =>
        S(Assistant(new JsonObject { ["type"] = "thinking", ["thinking"] = text }, model, stopReason, messageId));

    /// <summary>API エラーの返答（合成の返答。isApiErrorMessage が true）。</summary>
    public string AssistantApiError(string text, string? stopReason = "stop_sequence")
    {
        var o = Assistant(new JsonObject { ["type"] = "text", ["text"] = text }, "<synthetic>", stopReason, null);
        o["isApiErrorMessage"] = true;
        return S(o);
    }

    public string ToolUse(string id, string name, JsonObject input, string? model = "claude-x", string? messageId = null) =>
        S(Assistant(new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = name, ["input"] = input }, model, "tool_use", messageId));

    public string Bash(string id, string command, bool background = false, string name = "Bash")
    {
        var input = new JsonObject { ["command"] = command };
        if (background)
            input["run_in_background"] = true;
        return ToolUse(id, name, input);
    }

    /// <summary>サブエージェントの呼び出し（Agent ツール）。</summary>
    public string AgentCall(string id, string description, string subagentType = "general-purpose") =>
        ToolUse(id, "Agent", new JsonObject { ["description"] = description, ["subagent_type"] = subagentType, ["prompt"] = "…" });

    /// <summary>サブエージェントの結果。status が completed のとき、本体の結果として返る。</summary>
    public string AgentResult(string id, string status = "completed", string agentId = "agent-1", bool isError = false) =>
        ToolResult(id, "結果の要約", isError, new JsonObject { ["status"] = status, ["agentId"] = agentId });

    /// <summary>裏の作業の終わりの通知（origin.kind = task-notification）。</summary>
    public string TaskNotification(string toolUseId, string status = "completed") =>
        User($"<task-notification>\n<task-id>x</task-id>\n<tool-use-id>{toolUseId}</tool-use-id>\n<status>{status}</status>\n<summary>done</summary>\n</task-notification>",
            Origin.TaskNotification);

    /// <summary>メタ情報なしの行（種類だけ。attachment などの読み飛ばす行を作る）。</summary>
    public string Other(string type, JsonObject? extra = null, bool withMeta = true)
    {
        var o = Base(type, withMeta);
        if (extra is not null)
            foreach (var (k, v) in extra.ToList())
            {
                extra.Remove(k);
                o[k] = v;
            }
        return S(o);
    }
}
