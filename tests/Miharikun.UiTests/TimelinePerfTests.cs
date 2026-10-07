using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Core.Agents;
using Miharikun.Tests.Presentation;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>
/// 5,000 イベントのタイムライン（計画 30-5）の性能の計測。<c>MIHARIKUN_PERF=1</c> のときだけ動き、結果を
/// <c>MIHARIKUN_PERF_OUT</c>（フォルダ）の <c>timeline-perf.txt</c> に書く（時間は環境で揺れるので、通常の <c>dotnet test</c> では飛ばす）。
/// WPF 版の記録（<c>docs/macos-support/phase28-wpf-record.md</c>）と同じ操作：セッションを選ぶ・種別の ON/OFF・検索・別のセッションへ／戻る。
/// </summary>
public sealed class TimelinePerfTests
{
    private static void Flush(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static double Time(Window w, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        Flush(w);
        _ = w.CaptureRenderedFrame();   // 実際に描くところまで含める（レイアウトだけでなく、画面に出るまで）
        return sw.Elapsed.TotalMilliseconds;
    }

    [AvaloniaFact]
    public void Five_thousand_events()
    {
        if (Environment.GetEnvironmentVariable("MIHARIKUN_PERF") != "1")
        {
            Assert.Skip("MIHARIKUN_PERF=1 のときだけ動く");
            return;
        }

        using var h = new MainVmHarness();
        var big = MainVmHarness.KeyOf("big");
        var events = Scene.Big(big, 5000);
        h.EventsOf = key => key == big ? events : [];
        var bigSnapshot = Snapshot(h, big, events);
        h.Add(bigSnapshot, h.Snap("small", "小さいセッション", MainVmHarness.Now.AddMinutes(-5)));

        var window = new Window { Width = 1280, Height = 820, Content = new DashboardView { DataContext = h.Vm } };
        window.Show();
        Flush(window);

        var lines = new List<string> { $"イベント {events.Count} 件・ターン {events.Count(e => e.Kind == AgentEventKind.PromptSubmitted)} 個・{RuntimeInformation()}" };
        void Log(string name, double ms) => lines.Add($"{name}: {ms:F0} ms");

        Log("big を選ぶ（タイムラインが出るまで）", Time(window, () => h.Vm.Selected = h.Card("big")));
        Log("種別の ON/OFF（ツール）", Time(window, () => h.Vm.Timeline.ShowTool = true));
        Log("種別の ON/OFF（思考）", Time(window, () => h.Vm.Timeline.ShowThought = true));
        Log("種別を戻す", Time(window, () => { h.Vm.Timeline.ShowTool = false; h.Vm.Timeline.ShowThought = false; }));
        Log("検索 1 文字（'1'）", Time(window, () => h.Vm.Timeline.SearchText = "1"));
        Log("検索 2 文字（'12'）", Time(window, () => h.Vm.Timeline.SearchText = "12"));
        Log("検索を空に", Time(window, () => h.Vm.Timeline.SearchText = ""));
        Log("別のセッションへ", Time(window, () => h.Vm.Selected = h.Card("small")));
        Log("big へ戻る", Time(window, () => h.Vm.Selected = h.Card("big")));
        var target = h.Vm.Timeline.VisibleItems[h.Vm.Timeline.VisibleItems.Count / 2];
        Log("ジャンプ（中ほどの行へスクロール）", Time(window, () =>
        {
            window.GetVisualDescendants().OfType<TimelinePanel>().First().EnsureVisible(h.Vm.Timeline.VisibleItems.IndexOf(target));
        }));
        Log("全部コピーの文字列を作る", Time(window, () => _ = h.Vm.Timeline.CopyAllText()));
        lines.Add($"作られている行（画面の中だけ）: {window.GetVisualDescendants().OfType<TimelinePanel>().First().RealizedCount} / {h.Vm.Timeline.VisibleItems.Count}");

        var dir = Environment.GetEnvironmentVariable("MIHARIKUN_PERF_OUT") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "timeline-perf.txt"), lines);
    }

    private static string RuntimeInformation() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}・.NET {Environment.Version}・Skia ヘッドレス";

    private static Miharikun.Core.Sessions.SessionSnapshot Snapshot(MainVmHarness h, Miharikun.Core.Agents.SessionKey key, IReadOnlyList<AgentEvent> events)
    {
        var summary = Miharikun.Core.Sessions.SessionAnalyzer.Analyze(key, events);
        return new Miharikun.Core.Sessions.SessionSnapshot(summary, Miharikun.Core.Sessions.SessionSearch.BuildSearchText(summary, events));
    }
}
