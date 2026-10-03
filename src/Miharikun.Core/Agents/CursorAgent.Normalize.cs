using System.Text.Json;
using System.Text.Json.Nodes;

namespace Miharikun.Core.Agents;

public sealed partial class CursorAgent
{
    private const string ShellToolName = "Shell";

    /// <summary>要件 5.1 の対応表。未知のイベントは何も返さない。</summary>
    public IEnumerable<AgentEvent> Normalize(RawEventRecord raw)
    {
        var e = NormalizeOne(raw);
        return e is null ? [] : [e];
    }

    private static AgentEvent? NormalizeOne(RawEventRecord raw)
    {
        if (raw.Payload is not JsonObject p)
            return null;

        var b = new AgentEvent(
            new SessionKey(raw.AgentId, raw.SessionId), raw.LineNumber, raw.ReceivedAt, AgentEventKind.SessionStarted,
            Model: Str(p, "model_id") ?? Str(p, "model"),
            ModelParams: p["model_params"]?.ToJsonString(),
            TranscriptPath: Str(p, "transcript_path"));

        switch (raw.EventName)
        {
            case "sessionStart":
                return b with { Kind = AgentEventKind.SessionStarted, Git = raw.Git };

            case "sessionEnd":
                return b with
                {
                    Kind = AgentEventKind.SessionEnded,
                    Reason = Str(p, "reason"),
                    Duration = Millis(p, "duration_ms"),
                };

            case "beforeSubmitPrompt":
                return b with { Kind = AgentEventKind.PromptSubmitted, Text = Str(p, "prompt") };

            case "stop":
                return b with { Kind = AgentEventKind.TurnEnded, Outcome = ParseOutcome(Str(p, "status")), Git = raw.Git };

            case "preToolUse":
                return b with
                {
                    Kind = AgentEventKind.ToolStarted,
                    ToolUseId = Str(p, "tool_use_id"),
                    ToolName = Str(p, "tool_name"),
                    Command = ToolCommand(p),
                };

            case "postToolUse":
            {
                var toolName = Str(p, "tool_name");
                var (output, exitCode) = ReadToolOutput(p["tool_output"]);
                return b with
                {
                    Kind = AgentEventKind.ToolSucceeded,
                    ToolUseId = Str(p, "tool_use_id"),
                    ToolName = toolName,
                    Command = ToolCommand(p),
                    ExitCode = toolName == ShellToolName ? exitCode : null,
                    Output = output,
                    Duration = Millis(p, "duration"),
                };
            }

            case "postToolUseFailure":
                return b with
                {
                    Kind = AgentEventKind.ToolFailed,
                    ToolUseId = Str(p, "tool_use_id"),
                    ToolName = Str(p, "tool_name"),
                    Command = ToolCommand(p),
                    Output = Str(p, "error_message"),
                    Duration = Millis(p, "duration"),
                    Reason = Str(p, "failure_type"),
                };

            case "afterAgentResponse":
                return b with { Kind = AgentEventKind.AssistantMessage, Text = Str(p, "text") };

            case "afterAgentThought":
                return b with { Kind = AgentEventKind.AssistantThought, Text = Str(p, "text"), Duration = Millis(p, "duration_ms") };

            case "subagentStart":
                return b with { Kind = AgentEventKind.SubagentStarted, Text = Str(p, "task"), ToolName = Str(p, "subagent_type") };

            case "subagentStop":
                return b with { Kind = AgentEventKind.SubagentStopped, Text = Str(p, "task"), ToolName = Str(p, "subagent_type"), Duration = Millis(p, "duration_ms") };

            case "afterFileEdit":
                return b with { Kind = AgentEventKind.FileEdited, FilePath = Str(p, "file_path") };

            case "preCompact":
                return b with
                {
                    Kind = AgentEventKind.Compacted,
                    Compaction = new CompactionInfo(Str(p, "trigger"), RoundedInt(p, "context_usage_percent")),
                };

            default:
                return null;
        }
    }

    private static TurnOutcome ParseOutcome(string? status) => status switch
    {
        "completed" => TurnOutcome.Completed,
        "aborted" => TurnOutcome.Aborted,
        "error" => TurnOutcome.Error,
        _ => TurnOutcome.Unknown,
    };

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static int? RoundedInt(JsonObject o, string name) =>
        Num(o, name) is { } d ? (int)Math.Round(d, MidpointRounding.AwayFromZero) : null;

    private static TimeSpan? Millis(JsonObject o, string name) =>
        Num(o, name) is { } d && d >= 0 ? TimeSpan.FromMilliseconds(d) : null;

    private static string? ToolCommand(JsonObject p) =>
        p["tool_input"] is JsonObject input ? Str(input, "command") : null;

    /// <summary>
    /// tool_output の形は Step 0 で確定する（公式資料では string）。
    /// オブジェクト、または JSON オブジェクトを文字列化したもののどちらでも exitCode を読めるようにしている。
    /// </summary>
    private static (string? Output, int? ExitCode) ReadToolOutput(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                return (Str(o, "output") ?? o.ToJsonString(), ExitCodeOf(o));

            case JsonValue v when v.TryGetValue<string>(out var s):
                return (s, ExitCodeOfJsonText(s));

            default:
                return (null, null);
        }
    }

    private static int? ExitCodeOf(JsonObject o) => RoundedInt(o, "exitCode") ?? RoundedInt(o, "exit_code");

    private static int? ExitCodeOfJsonText(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return null;

        try
        {
            return JsonNode.Parse(text) is JsonObject o ? ExitCodeOf(o) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}