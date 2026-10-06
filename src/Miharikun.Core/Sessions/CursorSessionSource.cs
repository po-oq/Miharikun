using Miharikun.Core.Agents;
using Miharikun.Core.Projects;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Sessions;

/// <summary>
/// Cursor の読み込み（要件 9章）。events\cursor\*.jsonl（Hook が書く生イベント）を追記分だけ読み、
/// 対象プロジェクトに一致するセッションの共通イベントの差分を返す。
/// Hook の記録が無いセッションは transcript から取り込み（変わった分を毎回。Issue #17）、同じ ID の Hook のイベントが現れたらそちらに切り替える。
/// 差分の出し方は計画 8.1 の表のとおり。スレッドセーフではない（呼び出しは ProjectEventStore の排他の中）。
/// </summary>
public sealed class CursorSessionSource : ISessionSource
{
    private sealed class FileState(string sessionId)
    {
        public string SessionId { get; } = sessionId;
        public JsonlTail Tail { get; } = new();
        /// <summary>workspace_roots が対象プロジェクトに一致したか。false の間はイベントを保持しない。</summary>
        public bool Matched { get; set; }
        /// <summary>一致前に読んだ行を捨てたことがあるか。ある場合は一致した時点で先頭から読み直す。</summary>
        public bool DroppedLines { get; set; }
        /// <summary>Store に渡したイベントの数（作り直されたときに、消す差分が要るかの判断に使う）。</summary>
        public int EventCount { get; set; }
    }

    private readonly CursorAgent _agent;
    private readonly AppPaths _paths;
    private readonly string _projectFolder;
    private readonly Action<string>? _log;
    private readonly CursorTranscriptImporter? _importer;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>transcript から取り込んで Store に渡した導入前のセッション（hook のイベントがあるものは持たない）。</summary>
    private readonly Dictionary<string, SessionKey> _imported = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>直前の Scan の失敗のログの文。同じ文を毎回（3 秒ごとに）出さないために覚える。成功したら忘れる。</summary>
    private string? _lastScanFailure;

    public CursorSessionSource(CursorAgent agent, AppPaths paths, string projectFolder, Action<string>? log = null,
        CursorTranscriptImporter? importer = null)
    {
        _agent = agent;
        _paths = paths;
        _projectFolder = projectFolder;
        _log = log;
        _importer = importer;
    }

    public string AgentId => _agent.Id;

    /// <summary>events（Hook の記録。無ければ作る）と、transcript のフォルダ（サブフォルダ込み。読むだけで作らない）。</summary>
    public IReadOnlyList<WatchTarget> WatchTargets
    {
        get
        {
            var targets = new List<WatchTarget> { new(_paths.EventsDir(_agent.Id), "*.jsonl", CreateIfMissing: true) };
            if (_importer is not null)
            {
                foreach (var dir in _importer.TranscriptDirs(_projectFolder))
                    targets.Add(new WatchTarget(dir, "*.jsonl", CreateIfMissing: false, IncludeSubdirectories: true));
            }
            return targets;
        }
    }

    public IReadOnlyList<SessionDelta> ReadNew()
    {
        var deltas = new List<SessionDelta>();
        var paths = ListHookFiles();

        foreach (var path in paths)
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (sessionId != AppPaths.AppSessionId && !_files.ContainsKey(path))
                _files[path] = new FileState(sessionId);
        }

        // hook のイベントがあるセッション（別プロジェクトのものも含む）は、transcript から取り込んだものを捨てる。
        // 同じキーの hook の差分（Replace）が後ろに来るので、先に消しておく。
        var switching = new List<string>();
        if (_importer is not null)
        {
            foreach (var (id, key) in _imported.ToList())
            {
                if (_files.Values.Any(f => f.SessionId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                {
                    switching.Add(id);
                    deltas.Add(new SessionDelta(key, SessionDeltaKind.Remove, []));
                }
            }
        }

        foreach (var path in paths)
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (sessionId == AppPaths.AppSessionId)
                continue;

            try
            {
                ReadFile(path, _files[path], deltas);
            }
            catch (IOException ex)
            {
                _log?.Invoke($"{path} の読み込みに失敗: {ex.Message}");
            }
            catch (Exception ex)
            {
                // UnauthorizedAccessException など。ほかのファイルの差分は返す（Store の catch に任せると、積んだ差分が全部捨てられる）。
                _log?.Invoke($"{path} の読み込みに失敗: {ex}");
            }
        }

        foreach (var id in switching)
            _imported.Remove(id);

        ImportTranscripts(deltas);
        return deltas;
    }

    private string[] ListHookFiles()
    {
        var dir = _paths.EventsDir(_agent.Id);
        if (!Directory.Exists(dir))
            return [];
        try
        {
            return Directory.GetFiles(dir, "*.jsonl");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"events 一覧の取得に失敗: {ex.Message}");
            return [];
        }
    }

    private void ReadFile(string path, FileState state, List<SessionDelta> deltas)
    {
        var key = new SessionKey(_agent.Id, state.SessionId);
        var lines = state.Tail.ReadNewLines(path, out var truncated);
        var hadEvents = state.EventCount > 0;
        if (truncated)
        {
            // 作り直されたファイル。保持していた内容は捨てる（Store には、あとで Replace か Remove で伝える）。
            state.EventCount = 0;
            state.Matched = false;
            state.DroppedLines = false;
        }
        if (lines.Count == 0)
        {
            if (truncated && hadEvents)
                deltas.Add(new SessionDelta(key, SessionDeltaKind.Remove, []));
            return;
        }

        var (records, errors) = Parse(state, lines);

        if (!state.Matched && records.Any(MatchesProject))
        {
            state.Matched = true;
            if (state.DroppedLines)
            {
                // 一致するまでに捨てた行も必要なので、先頭から読み直す。
                state.Tail.Reset();
                (records, errors) = Parse(state, state.Tail.ReadNewLines(path, out _));
            }
            // 何も捨てていない（初回読み込み）なら、パース済みの行をそのまま使う。
        }
        if (!state.Matched)
        {
            // 他プロジェクトのセッションなので、壊れた行もログに出さない。
            state.DroppedLines = true;
            if (truncated && hadEvents)
                deltas.Add(new SessionDelta(key, SessionDeltaKind.Remove, []));
            return;
        }

        foreach (var (lineNumber, error) in errors)
            _log?.Invoke($"{path}:{lineNumber} をスキップ: {error}");
        if (records.Count == 0)
        {
            if (truncated && hadEvents)
                deltas.Add(new SessionDelta(key, SessionDeltaKind.Remove, []));
            return;   // 壊れた行だけが追記された
        }

        var events = new List<AgentEvent>();
        foreach (var raw in records)
            events.AddRange(_agent.Normalize(raw));
        state.EventCount += events.Count;

        // 作り直しは、Store の持つ内容を置き換える（Append にすると混ざる）。
        // 取り込み済みからの切り替えは、ReadNew が先に取り込み分の Remove を出しているので、ふつうの差分でよい。
        deltas.Add(new SessionDelta(key, truncated ? SessionDeltaKind.Replace : SessionDeltaKind.Append, events));
    }

    /// <summary>
    /// transcript から取り込む（Hook の記録が無いセッション）。毎回 Scan を呼ぶ：importer が、前回から変わった transcript だけを返す
    /// （Issue #17。Hook が動かない環境でも、会話の進みが起動中に見えるように）。
    /// </summary>
    private void ImportTranscripts(List<SessionDelta> deltas)
    {
        if (_importer is null)
            return;

        IReadOnlyList<ImportedSession> sessions;
        try
        {
            sessions = _importer.Scan(_projectFolder, id => File.Exists(_paths.EventFile(_agent.Id, id)));
        }
        catch (Exception ex)
        {
            // 同じ回の Hook の差分は返す。次の回にまた試す。失敗が続くときは、同じ文のログを 1 回しか出さない。
            var failure = $"transcript の取り込みに失敗: {ex}";
            if (failure != _lastScanFailure)
                _log?.Invoke(failure);
            _lastScanFailure = failure;
            return;
        }
        _lastScanFailure = null;

        foreach (var session in sessions)
        {
            if (_files.Values.Any(f => f.SessionId == session.Key.SessionId))
                continue;
            _imported[session.Key.SessionId] = session.Key;
            deltas.Add(new SessionDelta(session.Key, SessionDeltaKind.Replace, session.Events));
        }
    }

    private (List<RawEventRecord> Records, List<(long LineNumber, string Error)> Errors) Parse(
        FileState state, IReadOnlyList<(long LineNumber, string Text)> lines)
    {
        var records = new List<RawEventRecord>(lines.Count);
        var errors = new List<(long, string)>();
        foreach (var (lineNumber, text) in lines)
        {
            var raw = RawEventReader.TryParse(_agent.Id, state.SessionId, lineNumber, text, out var error);
            if (raw is null)
                errors.Add((lineNumber, error ?? "不明なエラー"));
            else
                records.Add(raw);
        }
        return (records, errors);
    }

    private bool MatchesProject(RawEventRecord raw) =>
        ProjectPath.Matches(_projectFolder, _agent.GetWorkspaceRoots(raw));
}
