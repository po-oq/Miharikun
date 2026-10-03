using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

internal static class TestData
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 3, 11, 0, 0, TimeSpan.FromHours(9));
    public const string Root = @"C:\work\proj";

    /// <summary>Cursor の生イベントを作る。fields は payload に足す JSON オブジェクトの本体（例: "\"prompt\":\"x\""）。</summary>
    public static RawEventRecord Raw(string evt, string fields = "", long line = 1, int sec = 0,
        string conv = "conv-1", GitSnapshot? git = null, string root = Root)
    {
        var rootJson = root.Replace("\\", "\\\\");
        var comma = fields.Length > 0 ? "," : "";
        var json = $$"""{"hook_event_name":"{{evt}}","conversation_id":"{{conv}}","workspace_roots":["{{rootJson}}"]{{comma}}{{fields}}}""";
        return new RawEventRecord("cursor", conv, line, T0.AddSeconds(sec), evt, git, JsonNode.Parse(json)!);
    }

    /// <summary>EventLine を JSONL 1行にする（Hook が書く形）。</summary>
    public static string Line(string evt, string fields = "", int sec = 0, string conv = "conv-1",
        GitSnapshot? git = null, string root = Root)
    {
        var r = Raw(evt, fields, 1, sec, conv, git, root);
        return EventLineJson.Serialize(new EventLine
        {
            Agent = "cursor", ReceivedAt = r.ReceivedAt, Event = evt, Git = git, Payload = r.Payload,
        });
    }

    /// <summary>生イベントの並びを Normalize して共通イベントにする。行番号は1始まりで自動採番。</summary>
    public static List<AgentEvent> Events(params (string Evt, string Fields, int Sec)[] items)
    {
        var agent = new CursorAgent();
        var list = new List<AgentEvent>();
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            list.AddRange(agent.Normalize(Raw(it.Evt, it.Fields, i + 1, it.Sec)));
        }
        return list;
    }

    public static (string, string, int) E(string evt, int sec = 0, string fields = "") => (evt, fields, sec);
}