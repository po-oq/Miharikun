using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using Miharikun.ViewModels;

namespace Miharikun.Tests.Presentation;

/// <summary>ViewModel のテスト用の部品。本物の git・画面・ファイル監視は使わない（メタだけ一時フォルダに書く）。</summary>
internal sealed class MainVmHarness : IDisposable
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Now;

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "miharikun-vm-" + Guid.NewGuid().ToString("N"));
    public string Project => Path.Combine(Dir, "proj");
    public AppPaths Paths { get; }
    public FakeUiServices Ui { get; } = new();
    public SessionMetaService Meta { get; }
    public SessionMonitor Monitor { get; }
    public MainViewModel Vm { get; }
    public List<string> Log { get; } = [];

    /// <summary>git status の差し替え（null＝git 不明）。呼ばれた回数も数える。</summary>
    public GitStatus? GitStatus { get; set; }
    public int GitStatusCalls { get; private set; }
    public IReadOnlyList<GitCommit>? Commits { get; set; } = [];
    public int CommitCalls { get; private set; }
    public Func<GitStatus?>? GitStatusOverride { get; set; }

    public MainVmHarness(int runningTimeoutMinutes = 10, string? hookErrorLogPath = null)
    {
        Paths = new AppPaths(Dir);
        Directory.CreateDirectory(Project);
        Meta = new SessionMetaService(agent => new MetaStore(Paths, agent), clock: () => Now);
        Monitor = new SessionMonitor(new ProjectEventStore([]));
        Vm = new MainViewModel(Project, Monitor, new ImmediateSynchronizationContext(), _ => [], Meta, Ui,
            () =>
            {
                GitStatusCalls++;
                return GitStatusOverride is { } f ? f() : GitStatus;
            },
            _ =>
            {
                CommitCalls++;
                return Commits;
            },
            runningTimeoutMinutes, hookErrorLogPath, Log.Add);
    }

    public void Dispose()
    {
        Vm.Dispose();
        Monitor.Dispose();
        try { Directory.Delete(Dir, true); } catch { }
    }

    public static SessionKey KeyOf(string id, string agent = "claude") => new(agent, id);

    /// <summary>
    /// セッションの要約を作る。<paramref name="running"/> なら入力だけ（実行中）、そうでなければ返事まで終わった（あなたの番）。
    /// 最後の動きは <paramref name="at"/>。<paramref name="editedFile"/> があれば、そのファイルを編集した記録を入れる。
    /// </summary>
    public SessionSnapshot Snap(string id, string prompt, DateTimeOffset at, bool running = false, string agent = "claude",
        string? editedFile = null)
    {
        var key = KeyOf(id, agent);
        var events = new List<AgentEvent>();
        long seq = 0;
        if (running)
        {
            events.Add(new(key, ++seq, at, AgentEventKind.PromptSubmitted, Text: prompt));
        }
        else
        {
            events.Add(new(key, ++seq, at.AddSeconds(-3), AgentEventKind.PromptSubmitted, Text: prompt));
            if (editedFile is not null)
                events.Add(new(key, ++seq, at.AddSeconds(-2), AgentEventKind.FileEdited, FilePath: editedFile));
            events.Add(new(key, ++seq, at.AddSeconds(-1), AgentEventKind.AssistantMessage, Text: "返事です"));
            events.Add(new(key, ++seq, at, AgentEventKind.TurnEnded, Outcome: TurnOutcome.Completed));
        }
        var summary = SessionAnalyzer.Analyze(key, events);
        return new SessionSnapshot(summary, SessionSearch.BuildSearchText(summary, events));
    }

    public void Add(params SessionSnapshot[] snaps) => Vm.Apply(new SessionUpdate(snaps, []));

    public void Remove(params SessionKey[] keys) => Vm.Apply(new SessionUpdate([], keys));

    public static GitStatus Dirty(string repoRoot, params string[] files) =>
        new(repoRoot, new HashSet<string>(files.Select(f => GitClient.NormalizePath(f)!), StringComparer.OrdinalIgnoreCase));

    public string Order() => string.Join(",", Vm.VisibleCards.Select(c => c.Key.SessionId));

    public SessionCardViewModel Card(string id) => Vm.Cards.Single(c => c.Key.SessionId == id);
}
