using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>Phase 18-2：ダミーの Source で、汎用 ProjectEventStore の差分の規則（計画 8.1）を確かめる。</summary>
public sealed class GenericProjectEventStoreTests
{
    private sealed class FakeSource(string agentId) : ISessionSource
    {
        public Queue<IReadOnlyList<SessionDelta>> Batches { get; } = new();
        public Exception? Throw { get; set; }
        public string AgentId { get; } = agentId;
        public IReadOnlyList<WatchTarget> WatchTargets { get; set; } = [];

        public IReadOnlyList<SessionDelta> ReadNew()
        {
            if (Throw is { } ex)
                throw ex;
            return Batches.Count > 0 ? Batches.Dequeue() : [];
        }

        public void Next(params SessionDelta[] deltas) => Batches.Enqueue(deltas);
    }

    private readonly FakeSource _source = new("fake");
    private readonly List<string> _logs = [];
    private readonly ProjectEventStore _store;

    public GenericProjectEventStoreTests() => _store = new ProjectEventStore([_source], log: _logs.Add);

    private static SessionKey K(string id, string agent = "fake") => new(agent, id);

    private static SessionDelta Append(SessionKey key, params (string Evt, string Fields, int Sec)[] items) =>
        new(key, SessionDeltaKind.Append, Events(items));

    private static SessionDelta Replace(SessionKey key, params (string Evt, string Fields, int Sec)[] items) =>
        new(key, SessionDeltaKind.Replace, Events(items));

    private static SessionDelta Remove(SessionKey key) => new(key, SessionDeltaKind.Remove, []);

    private static readonly (string, string, int) Prompt = E("beforeSubmitPrompt", 0, "\"prompt\":\"a\"");
    private static readonly (string, string, int) Stop = E("stop", 1, "\"status\":\"completed\"");

    [Fact]
    public void Append_creates_the_session_and_adds_to_the_end()
    {
        _source.Next(Append(K("a"), Prompt));
        Assert.Equal([K("a")], _store.Refresh());
        Assert.Single(_store.GetEvents(K("a")));

        _source.Next(Append(K("a"), Stop));
        Assert.Equal([K("a")], _store.Refresh());
        Assert.Equal(2, _store.GetEvents(K("a")).Count);
        Assert.Equal(SessionState.YourTurn, _store.GetSummary(K("a"))!.State);
    }

    [Fact]
    public void Empty_append_is_ignored_and_does_not_create_a_session()
    {
        _source.Next(new SessionDelta(K("a"), SessionDeltaKind.Append, []));

        Assert.Empty(_store.Refresh());
        Assert.Empty(_store.Sessions);
        Assert.Null(_store.GetSummary(K("a")));
    }

    [Fact]
    public void Replace_swaps_the_whole_content()
    {
        _source.Next(Append(K("a"), Prompt, Stop));
        _store.Refresh();

        _source.Next(Replace(K("a"), Prompt));
        Assert.Equal([K("a")], _store.Refresh());

        Assert.Single(_store.GetEvents(K("a")));
        Assert.Equal(SessionState.Running, _store.GetSummary(K("a"))!.State);
    }

    [Fact]
    public void Replace_creates_the_session_when_it_did_not_exist()
    {
        _source.Next(Replace(K("a"), Prompt));

        Assert.Equal([K("a")], _store.Refresh());
        Assert.Equal([K("a")], _store.Sessions);
    }

    [Fact]
    public void Empty_replace_is_the_same_as_remove()
    {
        _source.Next(Append(K("a"), Prompt));
        _store.Refresh();

        _source.Next(new SessionDelta(K("a"), SessionDeltaKind.Replace, []));

        Assert.Equal([K("a")], _store.Refresh());
        Assert.Null(_store.GetSummary(K("a")));
        Assert.Empty(_store.Sessions);
        Assert.Empty(_store.GetEvents(K("a")));
    }

    [Fact]
    public void Remove_reports_once_and_removing_a_missing_session_is_not_a_change()
    {
        _source.Next(Append(K("a"), Prompt));
        _store.Refresh();

        _source.Next(Remove(K("a")));
        Assert.Equal([K("a")], _store.Refresh());

        _source.Next(Remove(K("a")), Remove(K("never")));
        Assert.Empty(_store.Refresh());
    }

    [Fact]
    public void Several_deltas_for_one_key_are_applied_in_order_and_reported_once()
    {
        _source.Next(Append(K("a"), Prompt), Remove(K("a")), Replace(K("a"), Prompt, Stop), Append(K("b"), Prompt), Append(K("a"), Prompt));

        var changed = _store.Refresh();

        Assert.Equal([K("a"), K("b")], changed);
        Assert.Equal(3, _store.GetEvents(K("a")).Count);
    }

    [Fact]
    public void Summary_is_cached_until_the_session_changes()
    {
        _source.Next(Append(K("a"), Prompt), Append(K("b"), Prompt));
        _store.Refresh();
        var a1 = _store.GetSummary(K("a"));
        var b1 = _store.GetSummary(K("b"));
        Assert.Same(a1, _store.GetSummary(K("a")));

        _source.Next(Append(K("a"), Stop));
        _store.Refresh();

        Assert.NotSame(a1, _store.GetSummary(K("a")));
        Assert.Same(b1, _store.GetSummary(K("b")));   // 変わっていないセッションのキャッシュは残る
    }

    [Fact]
    public void Summary_is_dropped_after_replace()
    {
        _source.Next(Append(K("a"), Prompt, Stop));
        _store.Refresh();
        Assert.Equal(SessionState.YourTurn, _store.GetSummary(K("a"))!.State);

        _source.Next(Replace(K("a"), Prompt));
        _store.Refresh();

        Assert.Equal(SessionState.Running, _store.GetSummary(K("a"))!.State);
    }

    [Fact]
    public void Sessions_from_several_sources_are_kept_apart_by_their_keys()
    {
        var other = new FakeSource("other");
        var store = new ProjectEventStore([_source, other]);
        _source.Next(Append(K("x"), Prompt));
        other.Next(Append(K("x", "other"), Prompt, Stop));

        var changed = store.Refresh();

        Assert.Equal([K("x"), K("x", "other")], changed);
        Assert.Equal(SessionState.Running, store.GetSummary(K("x"))!.State);
        Assert.Equal(SessionState.YourTurn, store.GetSummary(K("x", "other"))!.State);
        Assert.Equal(2, store.Sessions.Count());
    }

    [Fact]
    public void A_source_that_throws_does_not_stop_the_others_and_is_logged_with_its_stack()
    {
        var bad = new FakeSource("bad") { Throw = new InvalidOperationException("壊れた") };
        var store = new ProjectEventStore([bad, _source], log: _logs.Add);
        _source.Next(Append(K("a"), Prompt));

        var changed = store.Refresh();

        Assert.Equal([K("a")], changed);
        var log = Assert.Single(_logs);
        Assert.Contains("bad", log);
        Assert.Contains("InvalidOperationException", log);
        Assert.Contains("壊れた", log);
    }

    [Fact]
    public void A_source_that_recovers_is_read_again_next_time()
    {
        _source.Throw = new IOException("一時的");
        Assert.Empty(_store.Refresh());

        _source.Throw = null;
        _source.Next(Append(K("a"), Prompt));
        Assert.Equal([K("a")], _store.Refresh());
    }

    [Fact]
    public void WatchTargets_are_collected_from_all_sources_each_time()
    {
        var other = new FakeSource("other");
        var store = new ProjectEventStore([_source, other]);
        Assert.Empty(store.WatchTargets);

        _source.WatchTargets = [new WatchTarget(@"C:\a", "*.jsonl", true)];
        other.WatchTargets = [new WatchTarget(@"C:\b", "*.jsonl", false)];

        Assert.Equal([@"C:\a", @"C:\b"], store.WatchTargets.Select(t => t.Directory));
    }
}
