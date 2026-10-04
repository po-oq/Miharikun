using Miharikun.Core.Agents;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Sessions;

/// <summary>
/// Claude Code の読み込み（要件 9.1）。<c>~\.claude\projects\&lt;フォルダ名&gt;\&lt;セッションID&gt;.jsonl</c> を追記分だけ読み、
/// 対象プロジェクト（または、その作業ツリー）のセッションの共通イベントの差分を返す。会話ログだけで読む（Hook は使わない）。
/// <b>読み取りだけ</b>で、<c>.claude</c> には何も書かない（フォルダも作らない）。差分の出し方は計画 8.1。
/// スレッドセーフではない（呼び出しは ProjectEventStore の排他の中）。
/// </summary>
public sealed class ClaudeSessionSource : ISessionSource
{
    /// <summary>cwd がまだ見つからない行を溜めておく上限（<see cref="ClaudeLocations.ReadFirstCwd"/> と同じ。超えたらあきらめる）。</summary>
    private const int MaxLinesWithoutCwd = 200;

    private enum Match { Unknown, Matched, Ignored }

    private sealed class FileState(string path, string sessionId, ClaudeFormatLog? formatLog)
    {
        public string Path { get; } = path;
        public string SessionId { get; } = sessionId;
        public JsonlTail Tail { get; } = new();
        public ClaudeTranscriptNormalizer Normalizer { get; set; } = new(sessionId, System.IO.Path.GetFileName(path), formatLog);
        public Match Match { get; set; }
        /// <summary>最初の cwd が見つかるまでに読んだ行（見つかったら、まとめて変換する）。</summary>
        public List<(long LineNumber, string Text)> Pending { get; } = [];
        /// <summary>Store に渡したイベントの数（作り直されたときに、消す差分が要るかの判断に使う）。</summary>
        public int EventCount { get; set; }

        public void Reset(ClaudeFormatLog? formatLog)
        {
            Normalizer = new ClaudeTranscriptNormalizer(SessionId, System.IO.Path.GetFileName(Path), formatLog);
            Match = Match.Unknown;
            Pending.Clear();
            EventCount = 0;
        }
    }

    private readonly ClaudeLocations _locations;
    private readonly Action<string>? _log;
    private readonly ClaudeFormatLog _formatLog;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>名前の規則が合わず、cwd で見つけたフォルダ（候補が 0 件だったときの保険）。</summary>
    private List<string> _fallbackDirs = [];
    private bool _fallbackDone;

    public ClaudeSessionSource(string projectFolder, string claudeDir, Action<string>? log = null)
    {
        _locations = new ClaudeLocations(claudeDir, projectFolder, log);
        _log = log;
        _formatLog = new ClaudeFormatLog(log);
    }

    public string AgentId => ClaudeCodeAgent.AgentId;

    /// <summary>
    /// 候補のフォルダ（本体と作業ツリー）と、まだ無くてもよい本体のフォルダ。呼ぶたびに取り直す（作業ツリーのフォルダはあとからできる）。
    /// どれも <c>CreateIfMissing = false</c>（<c>.claude</c> には何も作らない）。
    /// </summary>
    public IReadOnlyList<WatchTarget> WatchTargets
    {
        get
        {
            var dirs = CurrentDirs(searchByCwd: false);   // 監視先の取り直しは UI スレッドからも呼ばれる。重い探索は ReadNew だけで行う
            if (_locations.ExpectedDir is { } expected && !dirs.Contains(expected, StringComparer.OrdinalIgnoreCase))
                dirs.Add(expected);
            return [.. dirs.Select(d => new WatchTarget(d, "*.jsonl", CreateIfMissing: false))];
        }
    }

    /// <summary>サブエージェントの詳細（<c>&lt;セッション&gt;\subagents\agent-&lt;id&gt;.jsonl</c>）。今は読まない（将来用の口）。</summary>
    public IReadOnlyList<AgentEvent> GetSubagentEvents(SessionKey key, string subagentId) => [];

    public IReadOnlyList<SessionDelta> ReadNew()
    {
        var deltas = new List<SessionDelta>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unlistable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in CurrentDirs(searchByCwd: true))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.jsonl");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"{dir} の一覧取得に失敗: {ex.Message}");
                unlistable.Add(dir);   // 一時的に見えないだけかもしれない。消えたとは扱わない
                continue;
            }

            foreach (var path in files)
            {
                seen.Add(path);
                if (!_files.TryGetValue(path, out var state))
                    _files[path] = state = new FileState(path, Path.GetFileNameWithoutExtension(path), _formatLog);

                try
                {
                    ReadFile(state, deltas);
                }
                catch (IOException ex)
                {
                    _log?.Invoke($"{path} の読み込みに失敗: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // ほかのファイルの差分は返す（Store の catch に任せると、積んだ差分が全部捨てられる）。
                    _log?.Invoke($"{path} の読み込みに失敗: {ex}");
                }
            }
        }

        // 消えたファイル（フォルダごと消えたものを含む）。
        foreach (var (path, state) in _files.ToList())
        {
            if (seen.Contains(path) || unlistable.Contains(Path.GetDirectoryName(path) ?? ""))
                continue;
            _files.Remove(path);
            if (state.EventCount > 0)
                deltas.Add(new SessionDelta(new SessionKey(AgentId, state.SessionId), SessionDeltaKind.Remove, []));
        }

        _formatLog.Flush();
        return deltas;
    }

    /// <summary>
    /// いま読むフォルダ：名前の規則で絞った候補。<b>候補が 0 件のときだけ</b>、起動ごとに 1 回、cwd で探す（保険）。
    /// <c>.claude\projects</c> が無い間は探索を使い切らない（Claude Code を使い始めたら、また試す）。
    /// </summary>
    private List<string> CurrentDirs(bool searchByCwd)
    {
        var dirs = _locations.CandidateDirs().ToList();
        if (searchByCwd && dirs.Count == 0 && !_fallbackDone && Directory.Exists(_locations.ProjectsDir))
        {
            _fallbackDone = true;
            _fallbackDirs = [.. _locations.FindDirsByCwd()];
        }

        foreach (var dir in _fallbackDirs)
        {
            if (Directory.Exists(dir) && !dirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
                dirs.Add(dir);
        }
        return dirs;
    }

    private void ReadFile(FileState state, List<SessionDelta> deltas)
    {
        if (state.Match == Match.Ignored)
            return;   // 他のプロジェクトのセッション。開き直さない

        var key = new SessionKey(AgentId, state.SessionId);
        var lines = state.Tail.ReadNewLines(state.Path, out var truncated);
        var hadEvents = state.EventCount > 0;
        if (truncated)
        {
            // 作り直されたファイル。変換の状態も作り直す（Store には、あとで Replace か Remove で伝える）。
            state.Reset(_formatLog);
        }

        void RemoveIfHad()
        {
            if (truncated && hadEvents)
                deltas.Add(new SessionDelta(key, SessionDeltaKind.Remove, []));
        }

        if (lines.Count == 0)
        {
            RemoveIfHad();
            return;
        }

        var batch = new List<(long LineNumber, string Text)>(state.Pending);
        batch.AddRange(lines);
        state.Pending.Clear();

        if (state.Match == Match.Unknown)
        {
            // 照合：最初の cwd で決める（フォルダ名の規則に頼らない）。見つかるまでの行は、取っておく。
            var cwd = batch.Select(l => ClaudeLocations.CwdOf(l.Text)).FirstOrDefault(c => c is not null);
            if (cwd is null)
            {
                if (batch.Count > MaxLinesWithoutCwd)
                {
                    state.Match = Match.Ignored;
                    _log?.Invoke($"{Path.GetFileName(state.Path)}: 先頭の {MaxLinesWithoutCwd} 行に cwd が無いので、読まない");
                }
                else
                {
                    state.Pending.AddRange(batch);
                }
                RemoveIfHad();
                return;
            }

            state.Match = _locations.Matches(cwd) ? Match.Matched : Match.Ignored;
            if (state.Match == Match.Ignored)
            {
                RemoveIfHad();
                return;
            }
        }

        var events = new List<AgentEvent>();
        foreach (var (lineNumber, text) in batch)
            events.AddRange(state.Normalizer.NormalizeLine(lineNumber, text));
        state.Normalizer.FlushLog();
        state.EventCount += events.Count;

        if (truncated)
        {
            if (events.Count > 0)
                deltas.Add(new SessionDelta(key, SessionDeltaKind.Replace, events));
            else
                RemoveIfHad();
        }
        else if (events.Count > 0)
        {
            deltas.Add(new SessionDelta(key, SessionDeltaKind.Append, events));
        }
    }
}
