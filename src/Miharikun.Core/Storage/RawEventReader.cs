using System.Text.Json;
using Miharikun.Core.Agents;

namespace Miharikun.Core.Storage;

public static class RawEventReader
{
    /// <summary>JSONL 1行を RawEventRecord にする。壊れた行・必須項目欠落は null と理由を返す（呼び出し側でスキップ）。</summary>
    public static RawEventRecord? TryParse(string agentId, string sessionId, long lineNumber, string line, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            error = "空行";
            return null;
        }

        EventLine? parsed;
        try
        {
            parsed = EventLineJson.Deserialize(line);
        }
        catch (JsonException ex)
        {
            error = "JSON として読めない: " + ex.Message;
            return null;
        }

        if (parsed is null || parsed.Payload is null || string.IsNullOrEmpty(parsed.Event))
        {
            error = "event または payload がない";
            return null;
        }
        if (parsed.ReceivedAt == default)
        {
            error = "received_at がない";
            return null;
        }

        return new RawEventRecord(agentId, sessionId, lineNumber, parsed.ReceivedAt, parsed.Event, parsed.Git, parsed.Payload);
    }
}