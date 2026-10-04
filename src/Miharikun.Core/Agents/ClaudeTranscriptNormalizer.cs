using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Miharikun.Core.Agents;

/// <summary>
/// Claude Code の会話ログ（JSONL）の 1 行を、共通イベントに直す（要件 5.1 の対応表・計画 8.2）。
/// <b>状態を持つ</b>（ツール呼び出しと結果の対応・経過時間のため）。1 ファイルにつき 1 つ作り、行を順に渡す。
/// 本体のログにも、サブエージェントのログにもそのまま使える。
/// 値は TryGetValue だけで取り出す（欄の型が違う行で止まらない。AOT 互換）。読めない行は、その行だけ飛ばしてログに残す（行の中身は書かない）。
/// </summary>
public sealed class ClaudeTranscriptNormalizer
{
    private const int OutputTailLength = 2000;
    private const string SyntheticModel = "<synthetic>";
    private const string UnreadableJson = "JSON として読めない";

    /// <summary>ユーザーの返事待ち（質問・計画の承認）になるツール。呼び出しでボスの番、結果（回答）で依頼として実行中に戻す。</summary>
    private static readonly HashSet<string> AskTools = new(StringComparer.Ordinal) { "AskUserQuestion", "ExitPlanMode" };

    private static readonly HashSet<string> EditTools = new(StringComparer.Ordinal) { "Edit", "Write", "MultiEdit", "NotebookEdit" };

    private sealed record PendingTool(string CommonName, string? Command, string? FilePath, DateTimeOffset StartedAt, bool RunInBackground);

    /// <summary>形が想定と違う行（欄が無い・型が違う）。メッセージは自分で書いたもので、行の中身は含まない。</summary>
    private sealed class LogFormatException(string message) : Exception(message);

    private readonly SessionKey _key;
    private readonly string _fileName;
    private readonly string? _transcriptPath;
    private readonly ClaudeFormatLog? _formatLog;
    private readonly Dictionary<string, PendingTool> _pending = new(StringComparer.Ordinal);
    /// <summary>まだ終わっていないサブエージェント（Agent ツールの呼び出し）の tool_use の id。</summary>
    private readonly HashSet<string> _runningAgents = new(StringComparer.Ordinal);
    /// <summary>返事待ちの tool_use の id。</summary>
    private readonly HashSet<string> _awaitingAnswer = new(StringComparer.Ordinal);
    /// <summary>TurnEnded を出した message.id（思考の行と本文の行の両方に end_turn が付くため、1 回だけにする）。</summary>
    private readonly HashSet<string> _endedMessages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _suppressedErrors = new(StringComparer.Ordinal);
    private bool _started;
    /// <summary>最後に Git として伝えたブランチ。変わったときだけ、その行の最初のイベントに載せる。</summary>
    private string? _lastBranch;

    /// <param name="sessionId">セッション ID（SessionKey の元）。</param>
    /// <param name="fileName">ログに書くファイル名（行の中身ではなく、場所の手がかり）。</param>
    /// <param name="formatLog">形式の変化のログ。起動ごとに 1 つを、全ファイルで共有する。null ならログを出さない。</param>
    /// <param name="transcriptPath">会話ログのファイルのパス（SessionStarted に載せる。画面の transcript サイズ用）。</param>
    public ClaudeTranscriptNormalizer(string sessionId, string fileName, ClaudeFormatLog? formatLog = null, string? transcriptPath = null)
    {
        _key = new SessionKey(ClaudeCodeAgent.AgentId, sessionId);
        _fileName = fileName;
        _transcriptPath = transcriptPath;
        _formatLog = formatLog;
    }

    /// <summary>1 行（行番号は 1 始まり。Seq になる。壊れた行・空行も数える）を共通イベントにする。1 行から複数のイベントが出ることがある。</summary>
    public IReadOnlyList<AgentEvent> NormalizeLine(long lineNumber, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return [];

        var type = "（不明）";
        string? version = null;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject root)
                throw new LogFormatException("1 行が JSON のオブジェクトではない");

            type = Str(root, "type") ?? throw new LogFormatException("type が無い、または文字ではない");
            version = Str(root, "version");
            if (version is not null)
                _formatLog?.NoteVersion(version, _fileName);

            var events = new List<AgentEvent>();
            Convert(root, type, lineNumber, events);
            _started |= events.Count > 0 && events[0].Kind == AgentEventKind.SessionStarted;
            AttachBranch(root, events);
            return events;
        }
        catch (JsonException)
        {
            // System.Text.Json のメッセージには、行の一部が入ることがあるので使わない。
            ReportError(lineNumber, type, version, nameof(JsonException), UnreadableJson);
        }
        catch (LogFormatException ex)
        {
            ReportError(lineNumber, type, version, "形式が違う", ex.Message);
        }
        catch (Exception ex)
        {
            ReportError(lineNumber, type, version, ex.GetType().Name, "想定外の例外");
        }
        return [];
    }

    /// <summary>
    /// 同じ種類のエラーが、最初の 1 回のあとに何件あったかを書く（ログが膨れないように、件数だけ）。
    /// 1 回の読み込みの終わりに呼ぶ。
    /// </summary>
    public void FlushLog()
    {
        foreach (var (key, count) in _suppressedErrors)
        {
            if (count > 0)
                _formatLog?.Write($"{_fileName}: 読めない行（{key.Replace('|', ' ')}）が、ほかに {count} 件");
        }
        _suppressedErrors.Clear();
    }

    private void ReportError(long lineNumber, string type, string? version, string kind, string message)
    {
        if (_formatLog is null)
            return;

        var key = $"type={type}|{kind}|{message}";
        if (_loggedErrors.Add(key))
            _formatLog.Write($"{_fileName}:{lineNumber} をスキップ: type={type} version={version ?? "不明"} {kind}: {message}");
        else
            _suppressedErrors[key] = _suppressedErrors.GetValueOrDefault(key) + 1;
    }

    // ---- 変換 ----

    private void Convert(JsonObject root, string type, long seq, List<AgentEvent> events)
    {
        var at = Timestamp(root);

        if (type is not ("user" or "assistant"))
        {
            // 読み飛ばす記録でも、最初の timestamp のある記録は SessionStarted にする。
            AddStarted(root, at, seq, events);
            if (!ClaudeFormatLog.IsKnownSkipped(type))
                _formatLog?.NoteUnknownType(type);
            return;
        }

        if (at is null)
            throw new LogFormatException("timestamp が無い");
        AddStarted(root, at, seq, events);

        if (type == "user")
            ConvertUser(root, at.Value, seq, events);
        else
            ConvertAssistant(root, at.Value, seq, events);
    }

    /// <summary>
    /// ブランチ（<c>gitBranch</c>）が、最後に伝えたものと違うとき、その行の最初のイベントに載せる。
    /// 最初の記録（実ログでは queue-operation）には <c>gitBranch</c> が無いことがあり、SessionStarted だけでは取れないため。
    /// ブランチを切り替えたセッションは、変わった行で伝わる。イベントの出ない行（読み飛ばす記録）は、次の行に回す。
    /// </summary>
    private void AttachBranch(JsonObject root, List<AgentEvent> events)
    {
        if (events.Count == 0 || Str(root, "gitBranch") is not { Length: > 0 } branch || branch == _lastBranch)
            return;
        _lastBranch = branch;
        if (events[0].Git?.Branch is null)
            events[0] = events[0] with { Git = new GitSnapshot(branch, events[0].Git?.Head) };
    }

    private void AddStarted(JsonObject root, DateTimeOffset? at, long seq, List<AgentEvent> events)
    {
        if (_started || at is null)
            return;
        events.Add(new AgentEvent(_key, seq, at.Value, AgentEventKind.SessionStarted,
            Git: new GitSnapshot(Str(root, "gitBranch"), null), TranscriptPath: _transcriptPath));
    }

    private void ConvertUser(JsonObject root, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        var message = root["message"] as JsonObject ?? throw new LogFormatException("message が無い、またはオブジェクトではない");
        var content = message["content"];

        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            AddHumanInput(root, text, at, seq, events);
            return;
        }
        if (content is not JsonArray blocks)
            throw new LogFormatException("message.content が文字でも配列でもない");

        var hasResult = false;
        var texts = new List<string>();
        foreach (var node in blocks)
        {
            if (node is not JsonObject block)
                throw new LogFormatException("content の要素がオブジェクトではない");

            switch (Str(block, "type"))
            {
                case "tool_result":
                    hasResult = true;
                    AddToolResult(root, block, at, seq, events);
                    break;
                case "text" when Str(block, "text") is { } t:
                    texts.Add(t);
                    break;
            }
        }

        if (!hasResult && texts.Count > 0)
            AddHumanInput(root, string.Join("\n", texts), at, seq, events);
    }

    /// <summary>
    /// 人の入力（B11）：<c>isMeta</c> でない・<c>origin</c> が無いか <c>origin.kind == human</c>・<c>[Request interrupted</c> で始まらない。
    /// 古い版は <c>origin</c> 欄そのものが無い。<c>task-notification</c> は入力ではない。
    /// <c>[Request interrupted…]</c> は入力ではなく、ターンの中断（Aborted）。
    /// </summary>
    private void AddHumanInput(JsonObject root, string text, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        if (Bool(root, "isMeta") == true)
            return;
        if (root["origin"] is { } origin && !(origin is JsonObject o && Str(o, "kind") == "human"))
        {
            // 裏の作業の終わりの通知。入力にはしない。動いているサブエージェントのものなら、その終わりにする。
            if (origin is JsonObject n && Str(n, "kind") == "task-notification")
                AddSubagentStoppedByNotification(text, at, seq, events);
            return;
        }
        if (text.StartsWith("[Request interrupted", StringComparison.Ordinal))
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.TurnEnded, Outcome: TurnOutcome.Aborted));
        else
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.PromptSubmitted, Text: text));
    }

    /// <summary>通知の本文の <c>&lt;tool-use-id&gt;</c> が、動いているサブエージェントと一致したとき、その終わりにする（今は SendMessage のものが多く、一致しない）。</summary>
    private void AddSubagentStoppedByNotification(string text, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        const string Open = "<tool-use-id>";
        const string Close = "</tool-use-id>";
        var start = text.IndexOf(Open, StringComparison.Ordinal);
        if (start < 0)
            return;
        start += Open.Length;
        var end = text.IndexOf(Close, start, StringComparison.Ordinal);
        if (end < 0)
            return;

        var id = text[start..end].Trim();
        if (_runningAgents.Remove(id))
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.SubagentStopped, ToolUseId: id));
    }

    private void ConvertAssistant(JsonObject root, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        var message = root["message"] as JsonObject ?? throw new LogFormatException("message が無い、またはオブジェクトではない");

        // API エラーの返答（合成）。返答としては出さず、ターンのエラー終了にする。system の api_error は再試行されるので使わない。
        if (Bool(root, "isApiErrorMessage") == true)
        {
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.TurnEnded, Outcome: TurnOutcome.Error));
            return;
        }

        var model = Str(message, "model");
        if (model == SyntheticModel)
            model = null;   // 合成の返答。Cursor の "default" と同じ扱い

        var content = message["content"];
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.AssistantMessage, Text: text, Model: model));
            AddTurnEndedIfDone(message, hasText: true, at, seq, events);
            return;
        }
        if (content is not JsonArray blocks)
            throw new LogFormatException("message.content が文字でも配列でもない");

        var hasText = false;
        foreach (var node in blocks)
        {
            if (node is not JsonObject block)
                throw new LogFormatException("content の要素がオブジェクトではない");

            switch (Str(block, "type"))
            {
                case "text" when Str(block, "text") is { } t:
                    hasText = true;
                    events.Add(new AgentEvent(_key, seq, at, AgentEventKind.AssistantMessage, Text: t, Model: model));
                    break;
                case "thinking" when Str(block, "thinking") is { } th:
                    events.Add(new AgentEvent(_key, seq, at, AgentEventKind.AssistantThought, Text: th));
                    break;
                case "tool_use":
                    AddToolStarted(block, at, seq, events);
                    break;
            }
        }

        AddTurnEndedIfDone(message, hasText, at, seq, events);
    }

    /// <summary>
    /// <c>stop_reason == end_turn</c> は、<b>本文（text）を含む行で 1 回だけ</b>（思考の行と本文の行の両方に付くことがある）。
    /// 念のため、同じ <c>message.id</c> では 1 回だけ。本文のイベントの後に出す。
    /// </summary>
    private void AddTurnEndedIfDone(JsonObject message, bool hasText, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        if (!hasText || Str(message, "stop_reason") != "end_turn")
            return;
        if (Str(message, "id") is { } id && !_endedMessages.Add(id))
            return;
        events.Add(new AgentEvent(_key, seq, at, AgentEventKind.TurnEnded, Outcome: TurnOutcome.Completed));
    }

    private void AddToolStarted(JsonObject block, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        var id = Str(block, "id") ?? throw new LogFormatException("tool_use に id が無い");
        var name = Str(block, "name") ?? throw new LogFormatException("tool_use に name が無い");
        var input = block["input"] as JsonObject;

        if (name == "Agent")
        {
            // サブエージェントの呼び出し。ツールとしては数えず（Started のみ）、結果か通知で終わりにする。
            _runningAgents.Add(id);
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.SubagentStarted,
                Text: input is null ? null : Str(input, "description"),
                ToolName: input is null ? null : Str(input, "subagent_type"),
                ToolUseId: id));
            return;
        }

        if (AskTools.Contains(name))
        {
            // ユーザーの返事待ち＝ボスの番。実行中のツールにはしない（結果が来るまで、ずっと実行中に見えてしまう）。
            _awaitingAnswer.Add(id);
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.TurnEnded, Outcome: TurnOutcome.Completed));
            return;
        }

        var commonName = CommonToolName(name);
        var command = input is null ? null : Str(input, "command");
        var filePath = input is null || !EditTools.Contains(name)
            ? null
            : Str(input, name == "NotebookEdit" ? "notebook_path" : "file_path");

        _pending[id] = new PendingTool(commonName, command, filePath, at, input is not null && Bool(input, "run_in_background") == true);
        events.Add(new AgentEvent(_key, seq, at, AgentEventKind.ToolStarted, ToolName: commonName, ToolUseId: id, Command: command));
    }

    private void AddToolResult(JsonObject root, JsonObject block, DateTimeOffset at, long seq, List<AgentEvent> events)
    {
        var id = Str(block, "tool_use_id") ?? throw new LogFormatException("tool_result に tool_use_id が無い");
        var isError = block["is_error"] switch
        {
            null => false,
            JsonValue v when v.TryGetValue<bool>(out var b) => b,
            _ => throw new LogFormatException("is_error が真偽値ではない"),
        };

        if (_runningAgents.Contains(id))
        {
            // 結果の status が completed のときだけ終わり。それ以外（裏で動かした）は動いているままで、通知（task-notification）を待つ。
            // エラーの結果（拒否・中断など）は、もう動いていないので終わりにする。
            var agentResult = root["toolUseResult"] as JsonObject;
            if (isError || (agentResult is not null && Str(agentResult, "status") == "completed"))
            {
                _runningAgents.Remove(id);
                events.Add(new AgentEvent(_key, seq, at, AgentEventKind.SubagentStopped,
                    ToolUseId: id, SubagentId: agentResult is null ? null : Str(agentResult, "agentId")));
            }
            return;
        }

        if (_awaitingAnswer.Remove(id))
        {
            // 回答（承認・断りを含む）は依頼として数え、実行中に戻す。
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.PromptSubmitted, Text: "（回答）" + ContentText(block["content"])));
            return;
        }

        _pending.Remove(id, out var pending);
        var toolName = pending?.CommonName;
        var isShell = toolName == CommonTools.Shell;
        var text = ContentText(block["content"]);
        var output = isShell ? Tail(text) : null;
        TimeSpan? duration = pending is not null && at >= pending.StartedAt ? at - pending.StartedAt : null;

        AgentEvent Result(AgentEventKind kind, int? exitCode) => new(_key, seq, at, kind,
            ToolName: toolName, ToolUseId: id, Command: pending?.Command, ExitCode: exitCode, Output: output, Duration: duration);

        if (isError)
        {
            // コマンドは実行できて、終了コードが 0 でなかった（失敗したテストを成果に出すため、ToolFailed にしない）。
            events.Add(ExitCodeOf(text) is { } code
                ? Result(AgentEventKind.ToolSucceeded, code)
                : Result(AgentEventKind.ToolFailed, null));
            return;
        }

        // 裏で動かした Bash は、すぐ返るので終了コードは不明（0 とは言えない）。
        var background = pending?.RunInBackground == true ||
                         (root["toolUseResult"] is JsonObject tur && tur["backgroundTaskId"] is not null);
        events.Add(Result(AgentEventKind.ToolSucceeded, isShell && !background ? 0 : null));

        if (pending?.FilePath is { } path)
            events.Add(new AgentEvent(_key, seq, at, AgentEventKind.FileEdited, FilePath: path));
    }

    // ---- 部品 ----

    /// <summary>Bash と PowerShell は共通名 Shell。ほかは元の名前（Read・Edit・Write・Grep・mcp__…）。</summary>
    private static string CommonToolName(string name) => name is "Bash" or "PowerShell" ? CommonTools.Shell : name;

    /// <summary>内容が <c>Exit code N</c> で始まるなら N。</summary>
    private static int? ExitCodeOf(string? text)
    {
        const string Prefix = "Exit code ";
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var end = Prefix.Length;
        if (end < text.Length && text[end] == '-')
            end++;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
            end++;
        return int.TryParse(text.AsSpan(Prefix.Length, end - Prefix.Length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;
    }

    /// <summary>tool_result の内容。文字、または text ブロックの配列。それ以外は null。</summary>
    private static string? ContentText(JsonNode? content)
    {
        switch (content)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                return s;

            case JsonArray arr:
                var parts = new List<string>();
                foreach (var node in arr)
                {
                    if (node is JsonObject o && Str(o, "type") == "text" && Str(o, "text") is { } t)
                        parts.Add(t);
                }
                return parts.Count == 0 ? null : string.Join("\n", parts);

            default:
                return null;
        }
    }

    /// <summary>末尾の 2000 字（メモリのため。要件 5.1）。</summary>
    private static string? Tail(string? text) =>
        text is null || text.Length <= OutputTailLength ? text : text[^OutputTailLength..];

    private static DateTimeOffset? Timestamp(JsonObject root)
    {
        if (root["timestamp"] is null)
            return null;
        if (root["timestamp"] is JsonValue v && v.TryGetValue<string>(out var s) &&
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
            return at;
        throw new LogFormatException("timestamp が時刻として読めない");
    }

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? Bool(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
}
