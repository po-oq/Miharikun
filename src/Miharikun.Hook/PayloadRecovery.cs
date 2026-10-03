using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Miharikun.Hook;

/// <summary>どのイベントの入力かを示す、ASCII だけの手がかり（文字化けの影響を受けない項目）。</summary>
internal readonly record struct RecoveryHint(string? Event, string? ConversationId, string? GenerationId, string? ToolUseId)
{
    public static RecoveryHint From(JsonNode? payload) => new(
        Str(payload, "hook_event_name"), Str(payload, "conversation_id"), Str(payload, "generation_id"), Str(payload, "tool_use_id"));

    private static string? Str(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// 日本語環境の Windows では、Cursor が hook に渡す JSON の日本語が壊れる（実機で確認）。
///
/// Cursor は Windows で次のように hook を起動する：
///   $OutputEncoding = [System.Text.Encoding]::UTF8; Get-Content -LiteralPath '一時ファイル' -Raw | &amp; { $input | コマンド }
/// 一時ファイル（%TEMP%\cursor-hook-payload-*.json）は BOM なしの UTF-8 だが、Windows PowerShell 5.1 の Get-Content は
/// -Encoding なしだと既定のコードページ（日本語環境では CP932）で読む。その結果が UTF-8（BOM 付き）で渡ってくるので、
/// 日本語が化け、文字列中の「\」や「"」が失われて JSON として読めなくなることもある。
///
/// 元の一時ファイルは hook の実行中だけ存在するので、それを探して正しい内容を読み直す。
/// 取り違えないよう、「受け取った文字列 = そのファイルを CP932 で読んだもの」と一致するか、
/// 一致を確かめられないときはイベント名・会話 ID などが一意に合うものだけを採用する。
/// </summary>
internal static class PayloadRecovery
{
    private const int MaxCandidateBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private sealed record Candidate(string Text, byte[] Bytes, JsonObject Payload);

    /// <summary>この入力は、PowerShell の変換を受けて壊れている可能性があるか（BOM 付き、かつ非 ASCII を含む）。</summary>
    public static bool MaybeMangled(byte[] bytes, string text) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && text.Any(c => c > 0x7F);

    /// <returns>元の（正しい）JSON 文字列。見つからない・確かめられないときは null。</returns>
    public static string? TryFindOriginal(string tempDir, string received, RecoveryHint hint)
    {
        var candidates = LoadCandidates(tempDir);
        if (candidates.Count == 0)
            return null;

        // 1) 受け取った文字列が、候補を CP932 で読み替えたものと一致すれば確実
        if (TryGetCp932() is { } cp932)
        {
            var wanted = Normalize(received);
            foreach (var c in candidates)
            {
                if (Normalize(cp932.GetString(c.Bytes)) == wanted)
                    return c.Text;
            }
        }

        // 2) 一致を確かめられないときは、手がかりが合う候補が1つ（または内容が同じ）のときだけ
        var matching = candidates.Where(c => Matches(c.Payload, hint)).ToList();
        if (matching.Count == 0)
            return null;
        return matching.Select(c => c.Text).Distinct().Count() == 1 ? matching[0].Text : null;
    }

    private static List<Candidate> LoadCandidates(string tempDir)
    {
        var result = new List<Candidate>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(tempDir, "cursor-hook-payload-*.json"))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxCandidateBytes || DateTime.UtcNow - info.LastWriteTimeUtc > MaxAge)
                        continue;

                    byte[] bytes;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        bytes = ms.ToArray();
                    }

                    var text = StrictUtf8.GetString(bytes);
                    if (JsonNode.Parse(text) is JsonObject payload)
                        result.Add(new Candidate(text, bytes, payload));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or JsonException)
                {
                    // 実行中に消えた、書き込み途中、など。その候補は使わない。
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
        return result;
    }

    // 手がかりのうち、両方にあるものはすべて一致していること。イベント名と会話 ID は必須。
    private static bool Matches(JsonObject candidate, RecoveryHint hint)
    {
        var c = RecoveryHint.From(candidate);
        if (hint.Event is null || hint.ConversationId is null)
            return false;
        return c.Event == hint.Event
               && c.ConversationId == hint.ConversationId
               && (hint.GenerationId is null || c.GenerationId is null || c.GenerationId == hint.GenerationId)
               && (hint.ToolUseId is null || c.ToolUseId is null || c.ToolUseId == hint.ToolUseId);
    }

    // 先頭の BOM と、パイプが足す末尾の改行・空白は比べない
    private static string Normalize(string s) => s.TrimStart('\uFEFF').TrimEnd('\r', '\n', ' ', '\t');

    private static Encoding? TryGetCp932()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}