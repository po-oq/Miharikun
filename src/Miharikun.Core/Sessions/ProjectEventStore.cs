using Miharikun.Core.Agents;
using Miharikun.Core.Projects;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Sessions;

/// <summary>
/// events\{agent}\*.jsonl を追記分だけ読み、対象プロジェクトに一致するセッションの共通イベントを保持する（要件 9章）。
/// スレッドセーフではない。Refresh と読み出しは同じスレッド（または呼び出し側の排他）で行うこと。
/// </summary>
public sealed class ProjectEventStore
{
    private sealed class FileState(string sessionId)
    {
        public string SessionId { get; } = sessionId;
        public JsonlTail Tail { get; } = new();
        /// <summary>workspace_roots が対象プロジェクトに一致したか。false の間はイベントを保持しない。</summary>
        public bool Matched { get; set; }
        /// <summary>一致前に読んだ行を捨てたことがあるか。ある場合は一致した時点で先頭から読み直す。</summary>
        public bool DroppedLines { get; set; }
        public List<AgentEvent> Events { get; } = [];
        public SessionSummary? Summary { get; set; }
    }

    private readonly IAgent _agent;
    private readonly AppPaths _paths;
    private readonly AnalyzerSettings _settings;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public string ProjectFolder { get; }

    public ProjectEventStore(IAgent agent, AppPaths paths, string projectPath, AnalyzerSettings? settings = null, Action<string>? log = null)
    {
        _agent = agent;
        _paths = paths;
        ProjectFolder = projectPath;
        _settings = settings ?? new AnalyzerSettings();
        _log = log;
    }

    public IEnumerable<SessionKey> Sessions =>
        _files.Values.Where(f => f.Matched && f.Events.Count > 0).Select(f => new SessionKey(_agent.Id, f.SessionId));

    public IReadOnlyList<AgentEvent> GetEvents(SessionKey key) => Find(key)?.Events ?? [];

    public SessionSummary? GetSummary(SessionKey key)
    {
        var state = Find(key);
        if (state is null || state.Events.Count == 0)
            return null;
        return state.Summary ??= SessionAnalyzer.Analyze(key, state.Events, _settings);
    }

    /// <summary>新しく追記された行を取り込み、内容が変わったセッションを返す。</summary>
    public IReadOnlyCollection<SessionKey> Refresh()
    {
        var changed = new List<SessionKey>();
        var dir = _paths.EventsDir(_agent.Id);
        if (!Directory.Exists(dir))
            return changed;

        string[] paths;
        try
        {
            paths = Directory.GetFiles(dir, "*.jsonl");
        }
        catch (IOException ex)
        {
            _log?.Invoke($"events 一覧の取得に失敗: {ex.Message}");
            return changed;
        }

        foreach (var path in paths)
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (sessionId == AppPaths.AppSessionId)
                continue;

            if (!_files.TryGetValue(path, out var state))
                _files[path] = state = new FileState(sessionId);

            try
            {
                if (ReadFile(path, state))
                    changed.Add(new SessionKey(_agent.Id, sessionId));
            }
            catch (IOException ex)
            {
                _log?.Invoke($"{path} の読み込みに失敗: {ex.Message}");
            }
        }
        return changed;
    }

    private bool ReadFile(string path, FileState state)
    {
        var lines = state.Tail.ReadNewLines(path, out var truncated);
        var changed = false;
        if (truncated)
        {
            // 作り直されたファイル。保持していた内容は捨てて、見えなくなったことも変更として伝える。
            changed = state.Events.Count > 0;
            state.Events.Clear();
            state.Summary = null;
            state.Matched = false;
            state.DroppedLines = false;
        }
        if (lines.Count == 0)
            return changed;

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
            return changed;
        }

        foreach (var (lineNumber, error) in errors)
            _log?.Invoke($"{path}:{lineNumber} をスキップ: {error}");
        if (records.Count == 0)
            return changed;   // 壊れた行だけが追記された

        foreach (var raw in records)
            state.Events.AddRange(_agent.Normalize(raw));

        state.Summary = null;
        return true;
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
        ProjectPath.Matches(ProjectFolder, _agent.GetWorkspaceRoots(raw));

    private FileState? Find(SessionKey key) =>
        key.AgentId == _agent.Id
            ? _files.Values.FirstOrDefault(f => f.SessionId == key.SessionId)
            : null;
}