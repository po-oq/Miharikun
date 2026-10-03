using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Miharikun.Core.Agents;

namespace Miharikun.Core.Storage;

/// <summary>events\*.jsonl の1行（要件 6章）。Hook が書く。git は sessionStart / stop のときだけ。</summary>
public sealed class EventLine
{
    public int V { get; set; } = 1;
    public string Agent { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public string Event { get; set; } = "";
    public GitSnapshot? Git { get; set; }
    public JsonNode? Payload { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EventLine))]
internal sealed partial class MiharikunJsonContext : JsonSerializerContext;

public static class EventLineJson
{
    // 日本語を \uXXXX にエスケープしない（ファイルサイズと可読性のため）。
    private static readonly MiharikunJsonContext Context = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 独自の options を渡すと属性側の設定は使われないため、ここにも同じ指定が必要。
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    public static string Serialize(EventLine line) => JsonSerializer.Serialize(line, Context.EventLine);

    public static EventLine? Deserialize(string json) => JsonSerializer.Deserialize(json, Context.EventLine);
}