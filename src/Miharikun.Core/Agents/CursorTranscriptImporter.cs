using System.Text;
using System.Text.Json.Nodes;
using Miharikun.Core.Projects;

namespace Miharikun.Core.Agents;

/// <summary>transcript から読み込んだ、導入前の過去セッション1つ分（要件 11章）。</summary>
public sealed record ImportedSession(SessionKey Key, string TranscriptPath, IReadOnlyList<AgentEvent> Events);

/// <summary>
/// Cursor が保存している会話ログ <c>%USERPROFILE%\.cursor\projects\&lt;slug&gt;\agent-transcripts\</c> から、
/// hook を入れる前のセッションを共通イベントとして取り込む。入力と返事の本文だけを取り出す（ツール呼び出しは対象外）。
/// transcript には行ごとの時刻がないので、すべてファイルの更新日時にする。
/// </summary>
public sealed class CursorTranscriptImporter(string cursorDir, Action<string>? log = null)
{
    private const string AgentId = "cursor";

    private readonly string _cursorDir = cursorDir;

    /// <summary>
    /// プロジェクトのパスから、projects 配下のフォルダ名（slug）に当たる形を作る。実機では
    /// <c>C:\zDev\repo\Miharikun</c> → <c>c-zDev-repo-Miharikun</c>（区切りと「:」は 1 つの「-」）。
    /// 英数字以外をまとめて「-」にし、大文字小文字は比較時に無視する（記号や日本語の扱いは Cursor 側の規則が未確認なので、両側に同じ変換をかけて合わせる）。
    /// </summary>
    public static string SlugFor(string projectPath)
    {
        return Collapse(ProjectPath.Normalize(projectPath) ?? projectPath);
    }

    /// <summary>英数字以外の連なりを 1 つの「-」にし、前後の「-」は落とす。フォルダ名の比較用（パスの正規化はしない）。</summary>
    private static string Collapse(string path)
    {
        var sb = new StringBuilder(path.Length);
        foreach (var c in path)
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().TrimEnd('-');
    }

    /// <param name="skip">取り込まない conversation_id（hook のイベントがあるもの）なら true を返す。</param>
    public IReadOnlyList<ImportedSession> Scan(string projectFolder, Func<string, bool>? skip)
    {
        var result = new List<ImportedSession>();
        var wanted = SlugFor(projectFolder);

        foreach (var transcriptsDir in FindTranscriptDirs(wanted))
        {
            foreach (var (id, path) in ListTranscripts(transcriptsDir))
            {
                if (skip?.Invoke(id) == true)
                    continue;

                try
                {
                    var events = ReadEvents(new SessionKey(AgentId, id), path);
                    if (events.Count > 0)
                        result.Add(new ImportedSession(new SessionKey(AgentId, id), path, events));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.Invoke($"{path} の読み込みに失敗: {ex.Message}");
                }
            }
        }
        return result;
    }

    private IEnumerable<string> FindTranscriptDirs(string wantedSlug)
    {
        var projects = Path.Combine(_cursorDir, "projects");
        string[] dirs;
        try
        {
            dirs = Directory.Exists(projects) ? Directory.GetDirectories(projects) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{projects} の一覧取得に失敗: {ex.Message}");
            yield break;
        }

        foreach (var dir in dirs)
        {
            if (!Collapse(Path.GetFileName(dir)).Equals(wantedSlug, StringComparison.OrdinalIgnoreCase))
                continue;
            var transcripts = Path.Combine(dir, "agent-transcripts");
            if (Directory.Exists(transcripts))
                yield return transcripts;
        }
    }

    /// <summary><c>&lt;uuid&gt;\&lt;uuid&gt;.jsonl</c> と旧形式 <c>&lt;uuid&gt;.jsonl</c> の両方。subagents\ 配下は見ない。</summary>
    private IEnumerable<(string Id, string Path)> ListTranscripts(string transcriptsDir)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.GetFiles(transcriptsDir, "*.jsonl"))
                found[Path.GetFileNameWithoutExtension(file)] = file;

            foreach (var dir in Directory.GetDirectories(transcriptsDir))
            {
                var id = Path.GetFileName(dir);
                var file = Path.Combine(dir, id + ".jsonl");
                if (File.Exists(file))
                    found[id] = file;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{transcriptsDir} の一覧取得に失敗: {ex.Message}");
        }
        return found.Select(kv => (kv.Key, kv.Value));
    }

    private List<AgentEvent> ReadEvents(SessionKey key, string path)
    {
        var at = new DateTimeOffset(File.GetLastWriteTime(path));
        var events = new List<AgentEvent>();

        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;   // 壊れた行は飛ばす
            }

            var role = node?["role"]?.GetValue<string>();
            var text = JoinText(node?["message"]?["content"]);
            if (role is null || string.IsNullOrWhiteSpace(text))
                continue;

            var kind = role switch
            {
                "user" => AgentEventKind.PromptSubmitted,
                "assistant" => AgentEventKind.AssistantMessage,
                _ => (AgentEventKind?)null,
            };
            if (kind is null)
                continue;

            if (kind == AgentEventKind.PromptSubmitted)
            {
                text = ExtractUserQuery(text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;
            }

            events.Add(new AgentEvent(key, events.Count + 1, at, kind.Value, Text: text.Trim(),
                TranscriptPath: path, Imported: true));
        }
        return events;
    }

    private static string? JoinText(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var plain))
            return plain;
        if (content is not JsonArray blocks)
            return null;

        var parts = new List<string>();
        foreach (var block in blocks)
        {
            if (block?["type"]?.GetValue<string>() == "text" && block["text"]?.GetValue<string>() is { Length: > 0 } t)
                parts.Add(t);
        }
        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    /// <summary>user の本文は <c>&lt;timestamp&gt;…&lt;/timestamp&gt;&lt;user_query&gt;…&lt;/user_query&gt;</c> で包まれている。依頼文だけを取り出す。</summary>
    private static string ExtractUserQuery(string text)
    {
        const string open = "<user_query>", close = "</user_query>";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += open.Length;
            var end = text.LastIndexOf(close, StringComparison.Ordinal);
            return (end > start ? text[start..end] : text[start..]).Trim();
        }

        // 包まれていないときは、時刻のタグだけ取り除く
        var stamp = text.IndexOf("</timestamp>", StringComparison.Ordinal);
        return text.StartsWith("<timestamp>", StringComparison.Ordinal) && stamp >= 0
            ? text[(stamp + "</timestamp>".Length)..].Trim()
            : text.Trim();
    }
}
