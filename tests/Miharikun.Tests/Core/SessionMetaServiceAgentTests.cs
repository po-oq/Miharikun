using Miharikun.Core.Agents;
using Miharikun.Core.Meta;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

/// <summary>Phase 18-5：メタの保存先を、セッションのエージェントごとに切り替える。</summary>
public sealed class SessionMetaServiceAgentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-metaagent-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly SessionMetaService _service;

    public SessionMetaServiceAgentTests()
    {
        _paths = new AppPaths(_dir);
        _service = new SessionMetaService(agentId => new MetaStore(_paths, agentId));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Each_agent_saves_under_its_own_folder_even_with_the_same_session_id()
    {
        var cursor = new SessionKey("cursor", "s1");
        var claude = new SessionKey("claude", "s1");

        _service.Update(cursor, (m, _) => m with { Memo = "cursor のメモ" });
        _service.Update(claude, (m, _) => m with { Memo = "claude のメモ" });

        Assert.True(File.Exists(_paths.MetaFile("cursor", "s1")));
        Assert.True(File.Exists(_paths.MetaFile("claude", "s1")));
        Assert.Equal("cursor のメモ", _service.Get(cursor).Memo);
        Assert.Equal("claude のメモ", _service.Get(claude).Memo);
    }

    [Fact]
    public void Saved_meta_of_each_agent_is_read_back_after_a_restart()
    {
        _service.Update(new SessionKey("claude", "s1"), (m, _) => m with { Memo = "残る" });

        var restarted = new SessionMetaService(agentId => new MetaStore(_paths, agentId));

        Assert.Equal("残る", restarted.Get(new SessionKey("claude", "s1")).Memo);
        Assert.Equal("", restarted.Get(new SessionKey("cursor", "s1")).Memo);
    }

    [Fact]
    public void The_store_for_an_agent_is_made_once()
    {
        var made = new List<string>();
        var service = new SessionMetaService(id => { made.Add(id); return new MetaStore(_paths, id); });

        service.Get(new SessionKey("cursor", "a"));
        service.Get(new SessionKey("cursor", "b"));
        service.Get(new SessionKey("claude", "a"));

        Assert.Equal(["cursor", "claude"], made);
    }
}
