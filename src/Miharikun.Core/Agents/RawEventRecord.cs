using System.Text.Json.Nodes;

namespace Miharikun.Core.Agents;

/// <summary>events\{agentId}\{sessionId}.jsonl の1行。LineNumber は1始まりで AgentEvent.Seq になる。</summary>
public sealed record RawEventRecord(
    string AgentId, string SessionId, long LineNumber,
    DateTimeOffset ReceivedAt, string EventName,
    GitSnapshot? Git, JsonNode Payload);