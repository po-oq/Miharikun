using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

/// <summary>
/// 各 ISessionSource が返す差分を適用し、プロジェクトのセッションの共通イベントと要約を保持する（要件 9章）。
/// 何をどう読むかは Source の仕事で、ここは保持・要約のキャッシュ・変更通知だけを行う。差分の規則は計画 8.1。
/// スレッドセーフではない。Refresh と読み出しは同じスレッド（または呼び出し側の排他）で行うこと。
/// </summary>
public sealed class ProjectEventStore
{
    private sealed class Entry(SessionKey key)
    {
        public SessionKey Key { get; } = key;
        public List<AgentEvent> Events { get; } = [];
        public SessionSummary? Summary { get; set; }
    }

    private readonly IReadOnlyList<ISessionSource> _sources;
    private readonly AnalyzerSettings _settings;
    private readonly Action<string>? _log;
    private readonly Dictionary<SessionKey, Entry> _sessions = [];

    public ProjectEventStore(IReadOnlyList<ISessionSource> sources, AnalyzerSettings? settings = null, Action<string>? log = null)
    {
        _sources = sources;
        _settings = settings ?? new AnalyzerSettings();
        _log = log;
    }

    /// <summary>いま監視してほしいフォルダ（全 Source 分）。</summary>
    public IReadOnlyList<WatchTarget> WatchTargets => [.. _sources.SelectMany(s => s.WatchTargets)];

    public IEnumerable<SessionKey> Sessions => _sessions.Values.Where(s => s.Events.Count > 0).Select(s => s.Key);

    public IReadOnlyList<AgentEvent> GetEvents(SessionKey key) =>
        _sessions.TryGetValue(key, out var state) ? state.Events : [];

    public SessionSummary? GetSummary(SessionKey key) =>
        _sessions.TryGetValue(key, out var state) && state.Events.Count > 0
            ? state.Summary ??= SessionAnalyzer.Analyze(key, state.Events, _settings)
            : null;

    /// <summary>
    /// 各 Source の新しい差分を取り込み、内容が変わったセッションを返す（重複なし）。
    /// Source が例外を投げても、その内容をログに残して、ほかの Source は続ける。
    /// </summary>
    public IReadOnlyCollection<SessionKey> Refresh()
    {
        var changed = new List<SessionKey>();
        foreach (var source in _sources)
        {
            IReadOnlyList<SessionDelta> deltas;
            try
            {
                deltas = source.ReadNew();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"{source.AgentId} の読み込みに失敗: {ex}");
                continue;
            }

            foreach (var delta in deltas)
            {
                if (Apply(delta) && !changed.Contains(delta.Key))
                    changed.Add(delta.Key);
            }
        }
        return changed;
    }

    /// <summary>差分を適用する。何も変わらなければ false。</summary>
    private bool Apply(SessionDelta delta)
    {
        var kind = delta.Kind;
        if (kind == SessionDeltaKind.Replace && delta.Events.Count == 0)
            kind = SessionDeltaKind.Remove;

        switch (kind)
        {
            case SessionDeltaKind.Append:
                if (delta.Events.Count == 0)
                    return false;
                GetOrAdd(delta.Key).Events.AddRange(delta.Events);
                break;

            case SessionDeltaKind.Replace:
                var state = GetOrAdd(delta.Key);
                state.Events.Clear();
                state.Events.AddRange(delta.Events);
                break;

            default:   // Remove
                if (!_sessions.TryGetValue(delta.Key, out var existing))
                    return false;
                var had = existing.Events.Count > 0;
                _sessions.Remove(delta.Key);
                return had;
        }

        _sessions[delta.Key].Summary = null;
        return true;
    }

    private Entry GetOrAdd(SessionKey key)
    {
        if (!_sessions.TryGetValue(key, out var state))
            _sessions[key] = state = new Entry(key);
        return state;
    }
}
