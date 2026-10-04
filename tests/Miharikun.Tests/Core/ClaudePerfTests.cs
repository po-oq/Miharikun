using System.Diagnostics;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>
/// Phase 20-3：大きな会話ログの初回の読み込みと追記の時間・メモリを測る（MIHARIKUN_PERF=1 のときだけ動く。
/// Defender などで時間が揺れるため、普段の dotnet test では飛ばす）。数値は標準出力に出る。しきい値はゆるい安全網だけ。
/// </summary>
public sealed class ClaudePerfTests : IDisposable
{
    private const string Project = @"C:\work\proj";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "miharikun-claudeperf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Folder => Path.Combine(_root, ".claude", "projects", "C--work-proj");

    /// <summary>
    /// 実ログに近い構成のダミー（内容は合成）。1 回のやりとり = 依頼・思考・返答・Bash の呼び出しと結果（3KB の出力）・
    /// Read の呼び出しと結果（8KB）・Edit の呼び出しと結果・読み飛ばす attachment（1KB）で、約 16KB・14 行。
    /// </summary>
    private static long WriteLog(string path, long targetBytes, ClaudeLogBuilder b)
    {
        var shellOut = new string('s', 3000);
        var readOut = new string('r', 8000);
        var attachment = new string('a', 1000);
        var think = new string('t', 1000);
        long lines = 0, i = 0;
        using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        while (writer.BaseStream.Length < targetBytes)
        {
            var n = ++i;
            var batch = new[]
            {
                b.User($"依頼 {n}: ここにそれなりの長さの依頼文が入る。" + new string('あ', 100)),
                b.AssistantThinking(think),
                b.AssistantText($"返答 {n}: " + new string('い', 200)),
                b.Bash($"b{n}", "dotnet test --no-build"),
                b.ToolResult($"b{n}", shellOut),
                b.ToolUse($"r{n}", "Read", new System.Text.Json.Nodes.JsonObject { ["file_path"] = @"C:\work\proj\src\a.cs" }),
                b.ToolResult($"r{n}", readOut, contentAsBlocks: true),
                b.ToolUse($"e{n}", "Edit", new System.Text.Json.Nodes.JsonObject { ["file_path"] = $@"C:\work\proj\src\f{n % 50}.cs" }),
                b.ToolResult($"e{n}", "The file has been updated"),
                b.Other("attachment", new System.Text.Json.Nodes.JsonObject { ["attachment"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "x", ["content"] = attachment } }),
                b.AssistantText($"おわり {n}", stopReason: "end_turn"),
            };
            foreach (var line in batch)
            {
                writer.WriteLine(line);
                lines++;
            }
            writer.Flush();
        }
        return lines;
    }

    [PerfTheory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(60)]
    public void First_read_and_append_of_a_large_session(int megabytes)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "big.jsonl");
        var b = new ClaudeLogBuilder("big", Project);
        var lines = WriteLog(path, megabytes * 1024L * 1024L, b);
        var size = new FileInfo(path).Length;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var allocBefore = GC.GetTotalAllocatedBytes(true);

        var source = new ClaudeSessionSource(Project, Path.Combine(_root, ".claude"));
        var store = new ProjectEventStore([source]);
        var sw = Stopwatch.StartNew();
        var changed = store.Refresh();
        var firstRead = sw.Elapsed;
        sw.Restart();
        var summary = store.GetSummary(new SessionKey("claude", "big"))!;
        var analyze = sw.Elapsed;

        var allocated = GC.GetTotalAllocatedBytes(true) - allocBefore;
        var retained = GC.GetTotalMemory(true) - before;
        var events = store.GetEvents(new SessionKey("claude", "big")).Count;

        // 追記（1 回のやりとり分）を読む
        File.AppendAllText(path, string.Concat(new[] { b.User("追記"), b.AssistantText("追記の返事", stopReason: "end_turn") }.Select(l => l + "\n")));
        sw.Restart();
        var appended = store.Refresh();
        var appendRead = sw.Elapsed;

        // 何も変わらないときのポーリング 1 回（3 秒ごとに走る）
        sw.Restart();
        for (var i = 0; i < 10; i++)
            store.Refresh();
        var idlePoll = TimeSpan.FromTicks(sw.Elapsed.Ticks / 10);

        Console.WriteLine(
            $"claude {megabytes}MB（実サイズ {size / 1024.0 / 1024.0:F1}MB・{lines:N0} 行・{events:N0} イベント）: " +
            $"初回の読み込み {firstRead.TotalMilliseconds:F0}ms／要約 {analyze.TotalMilliseconds:F0}ms／" +
            $"確保 {allocated / 1024.0 / 1024.0:F0}MB・保持 {retained / 1024.0 / 1024.0:F0}MB／" +
            $"追記 {appendRead.TotalMilliseconds:F1}ms／変化なしのポーリング {idlePoll.TotalMilliseconds:F1}ms");

        Assert.Single(changed);
        Assert.Single(appended);
        Assert.Equal(SessionState.YourTurn, summary.State);
        Assert.True(firstRead < TimeSpan.FromSeconds(20), $"初回の読み込みが遅すぎる: {firstRead}");   // 安全網（目安は 3 秒）
        Assert.True(appendRead < TimeSpan.FromSeconds(1), $"追記の読み込みが遅すぎる: {appendRead}");
    }
}


public sealed class ClaudePerfManyFilesTests : IDisposable
{
    private const string Project = @"C:workproj";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "miharikun-claudeperf2-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>セッションが多いプロジェクト（古いものが溜まった状態）：初回の読み込みと、変化なしのポーリング 1 回の時間。</summary>
    [PerfFact]
    public void Many_sessions_first_read_and_idle_poll()
    {
        var folder = Path.Combine(_root, ".claude", "projects", "C--work-proj");
        Directory.CreateDirectory(folder);
        const int files = 300;
        for (var f = 0; f < files; f++)
        {
            var b = new ClaudeLogBuilder("s" + f, Project);
            var lines = new List<string>();
            for (var i = 0; i < 40; i++)
            {
                lines.Add(b.User("依頼 " + i));
                lines.Add(b.Bash("b" + i, "ls"));
                lines.Add(b.ToolResult("b" + i, new string('o', 1500)));
                lines.Add(b.AssistantText("返事 " + i, stopReason: "end_turn"));
            }
            File.WriteAllLines(Path.Combine(folder, "s" + f + ".jsonl"), lines);
        }
        var total = Directory.GetFiles(folder).Sum(x => new FileInfo(x).Length);

        var store = new ProjectEventStore([new ClaudeSessionSource(Project, Path.Combine(_root, ".claude"))]);
        var sw = Stopwatch.StartNew();
        var changed = store.Refresh();
        var first = sw.Elapsed;
        sw.Restart();
        for (var i = 0; i < 10; i++)
            store.Refresh();
        var idle = TimeSpan.FromTicks(sw.Elapsed.Ticks / 10);

        Console.WriteLine($"claude {files} sessions ({total / 1024.0 / 1024.0:F1}MB): first read {first.TotalMilliseconds:F0}ms / idle poll {idle.TotalMilliseconds:F1}ms");

        Assert.Equal(files, changed.Count);
        Assert.True(first < TimeSpan.FromSeconds(20), $"{first}");
        Assert.True(idle < TimeSpan.FromSeconds(1), $"{idle}");
    }
}

/// <summary>MIHARIKUN_PERF=1 のときだけ動く Theory（<see cref="PerfFactAttribute"/> の Theory 版）。</summary>
public sealed class PerfTheoryAttribute : TheoryAttribute
{
    public PerfTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("MIHARIKUN_PERF") != "1")
            Skip = "計測用。MIHARIKUN_PERF=1 で実行する";
    }
}
