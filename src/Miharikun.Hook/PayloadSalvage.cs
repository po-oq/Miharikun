using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Miharikun.Hook;

/// <summary>
/// JSON として読めなかった入力から、状態判定に要る項目だけを救い出す。
///
/// 実機（Windows・日本語環境）では、日本語を含む入力が Cursor から渡る途中で文字コードの変換を受けて壊れ、
/// 文字列の中の「\」や「"」が失われて JSON が崩れることがある。イベント自体を失うと状態が狂うので、
/// イベント名・セッション ID など ASCII の項目だけは正規表現で拾って記録する（payload に _salvaged: true を付ける）。
/// </summary>
internal static partial class PayloadSalvage
{
    // 文字列の値を1つ拾う。エスケープ済みの「\"」は値の一部として扱う。
    private const string StringValue = "\"((?:[^\"\\\\]|\\\\.)*)\"";

    private static readonly string[] Keys =
    [
        "hook_event_name", "conversation_id", "session_id", "generation_id", "model", "model_id",
        "status", "reason", "tool_name", "tool_use_id", "file_path", "trigger", "cursor_version", "transcript_path",
    ];

    /// <summary>イベント名が拾えなければ null（記録しようがない）。</summary>
    public static JsonObject? TryRecover(string raw, string parseError)
    {
        var payload = new JsonObject();
        foreach (var key in Keys)
        {
            var m = Regex.Match(raw, "\"" + key + "\"\\s*:\\s*" + StringValue);
            if (m.Success)
                payload[key] = Unescape(m.Groups[1].Value);
        }

        if (payload["hook_event_name"] is null)
            return null;

        var roots = WorkspaceRoots().Match(raw);
        if (roots.Success)
        {
            var array = new JsonArray();
            JsonNode first = Unescape(roots.Groups[1].Value);
            array.Add(first);   // Add<T> ではなく JsonNode 版（AOT 互換）
            payload["workspace_roots"] = array;
        }

        payload["_salvaged"] = true;
        payload["_parse_error"] = parseError;
        return payload;
    }

    /// <summary>JSON のエスケープ（\n や \\）を戻す。戻せなければ、そのまま。</summary>
    private static string Unescape(string escaped)
    {
        try
        {
            return JsonNode.Parse("\"" + escaped + "\"")?.GetValue<string>() ?? escaped;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return escaped;
        }
    }

    [GeneratedRegex("\"workspace_roots\"\\s*:\\s*\\[\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex WorkspaceRoots();
}