using System.Text;
using System.Text.Json.Nodes;
using Miharikun.Core.Install;
using Miharikun.Core.Projects;

namespace Miharikun.Core.Agents;

/// <summary>transcript から読み込んだ、導入前の過去セッション1つ分（要件 11章）。</summary>
public sealed record ImportedSession(SessionKey Key, string TranscriptPath, IReadOnlyList<AgentEvent> Events);

/// <summary>
/// Cursor が保存している会話ログ <c>%USERPROFILE%\.cursor\projects\&lt;slug&gt;\agent-transcripts\</c> から、
/// hook を入れる前のセッションを共通イベントとして取り込む。入力と返事の本文だけを取り出す（ツール呼び出しは対象外）。
/// transcript には行ごとの時刻がないので、すべてファイルの更新日時にする。
/// <paramref name="registration"/> があるとき、transcript の最後の更新が Hook の登録より後なら、そのイベントを
/// <c>HookMissing</c>（Hook が記録していない）にする（Issue #17。null なら常に false）。
/// </summary>
public class CursorTranscriptImporter(string cursorDir, Action<string>? log = null, HookRegistration? registration = null)
{
    private const string AgentId = "cursor";

    private readonly string _cursorDir = cursorDir;

    /// <summary>Hook の登録の時刻（hooks.json の更新日時が変わるまで使い回す）。</summary>
    private DateTimeOffset? _since;
    private DateTimeOffset? _stamp;
    private bool _stampKnown;

    /// <summary>読み済みの transcript の（長さ, 更新日時）。conversation_id ごと。</summary>
    private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _seen = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// 前回の Scan から変わった transcript だけを読み、そのセッションを丸ごと返す（初回は全部。要件 11章・計画 7.3）。
    /// 長さと更新日時をファイルごとに覚えて比べる。覚える値は開く前に取り、読み終わったあとにもう一度取って違えば
    /// （読んでいる間に追記された）覚えない＝次の Scan でまた読む。読めなかったときも覚えない。
    /// </summary>
    /// <param name="skip">取り込まない conversation_id（hook のイベントがあるもの）なら true を返す。覚えた値も消す（hook のファイルが後で消えたとき、取り込みに戻れるように）。</param>
    public virtual IReadOnlyList<ImportedSession> Scan(string projectFolder, Func<string, bool>? skip)
    {
        var result = new List<ImportedSession>();
        RefreshRegistration();

        foreach (var transcriptsDir in TranscriptDirs(projectFolder))
        {
            foreach (var (id, path) in ListTranscripts(transcriptsDir))
            {
                if (skip?.Invoke(id) == true)
                {
                    _seen.Remove(id);
                    continue;
                }

                try
                {
                    var before = new FileInfo(path);
                    if (!before.Exists)
                        continue;
                    var stamp = (before.Length, before.LastWriteTimeUtc);
                    if (_seen.TryGetValue(id, out var known) && known == stamp)
                        continue;

                    var hookMissing = _since is { } since && before.LastWriteTimeUtc > since.UtcDateTime;
                    var events = ReadEvents(new SessionKey(AgentId, id), path, new DateTimeOffset(before.LastWriteTime), hookMissing);
                    OnTranscriptRead(path);

                    var after = new FileInfo(path);
                    if (after.Exists && (after.Length, after.LastWriteTimeUtc) == stamp)
                        _seen[id] = stamp;
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

    /// <summary>
    /// hooks.json の更新日時（stat だけ）が前回と違えば、登録の時刻を取り直して、覚えた値を全部消す
    /// （＝全 transcript を読み直して、Hook なしかどうかを判定し直す）。導入・削除・手での書き換えがすぐ反映される。
    /// 変わらない回は hooks.json を読まない。最初の Scan でも取る。
    /// </summary>
    private void RefreshRegistration()
    {
        if (registration is null)
            return;

        var stamp = registration.Stamp();
        if (_stampKnown && stamp == _stamp)
            return;

        _stamp = stamp;
        _stampKnown = true;
        _since = registration.Since();
        _seen.Clear();
    }

    /// <summary>transcript を読み終えた直後（もう一度大きさを見る前）に呼ばれる。テストで「読んでいる最中の追記」を作るための差し込み口。</summary>
    protected virtual void OnTranscriptRead(string path)
    {
    }

    /// <summary>プロジェクトに当たる <c>agent-transcripts\</c> フォルダ（実在するものだけ）。監視先にも使う。</summary>
    public IReadOnlyList<string> TranscriptDirs(string projectFolder) => [.. FindTranscriptDirs(SlugFor(projectFolder))];

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

    /// <summary>
    /// Cursor が書き込み中でも開けるよう共有して読む。最後の行は、改行で終わる、または JSON として最後まで読めるなら使い、
    /// どちらでもない（書きかけ）なら飛ばす（次に長さが変わったときに読み直す）。
    /// </summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        string text;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            text = reader.ReadToEnd();

        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                var last = text[start..];
                if (IsCompleteJson(last))
                    yield return last;
                yield break;   // 書きかけの最後の行は使わない
            }
            yield return text[start..end].TrimEnd('\r');
            start = end + 1;
        }
    }

    private static bool IsCompleteJson(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        try
        {
            JsonNode.Parse(line);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private List<AgentEvent> ReadEvents(SessionKey key, string path, DateTimeOffset at, bool hookMissing)
    {
        var events = new List<AgentEvent>();

        foreach (var line in ReadLines(path))
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
                TranscriptPath: path, Imported: true, HookMissing: hookMissing));
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
