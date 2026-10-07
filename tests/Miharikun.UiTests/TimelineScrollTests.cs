using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Core.Sessions;
using Miharikun.Tests.Presentation;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>
/// 長い（5,000 行・行の高さがまちまちな）タイムラインのスクロール（<see cref="TimelinePanel"/>）：
/// ①マウスを載せただけでは動かない ②一番下から上へ、ホイールで進むと、位置が単調に上がり、途中で飛ばない（標準の ListBox は、
/// 全体の高さの見積もりが外れて、先頭に近づくと 1,000 行以上を一度に飛んだ）③最後まで行くと先頭 ④指定した行へ移る。
/// </summary>
public sealed class TimelineScrollTests
{
    private sealed class Screen : IDisposable
    {
        public MainVmHarness Harness { get; } = new();
        public Window Window { get; }
        public TimelinePanel Panel { get; }
        public ScrollViewer Scroll { get; }

        public Screen(int events = 5000)
        {
            var key = MainVmHarness.KeyOf("big");
            var list = Scene.Big(key, events);
            Harness.EventsOf = k => k == key ? list : [];
            var summary = SessionAnalyzer.Analyze(key, list);
            Harness.Add(new SessionSnapshot(summary, SessionSearch.BuildSearchText(summary, list)));
            Harness.Vm.Selected = Harness.Card("big");
            Window = new Window { Width = 1280, Height = 820, Content = new DashboardView { DataContext = Harness.Vm } };
            Window.Show();
            Flush();
            Panel = Window.GetVisualDescendants().OfType<TimelinePanel>().First();
            Scroll = Panel.GetVisualAncestors().OfType<ScrollViewer>().First();
        }

        public void Flush()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            Harness.Dispose();
        }
    }

    [AvaloniaFact]
    public void Hovering_over_the_rows_of_a_long_timeline_does_not_move_the_scroll_position()
    {
        using var s = new Screen();
        s.Scroll.Offset = new Vector(0, 6000);
        s.Flush();
        var before = s.Scroll.Offset.Y;
        Assert.True(before > 1000);

        var top = s.Scroll.TranslatePoint(new Point(40, 10), s.Window)!.Value;
        for (var round = 0; round < 3; round++)
        {
            for (var y = 0; y < s.Scroll.Bounds.Height - 20; y += 15)
            {
                s.Window.MouseMove(new Point(top.X, top.Y + y));
                s.Flush();
            }
        }

        Assert.Equal(before, s.Scroll.Offset.Y, 1);
    }

    [AvaloniaFact]
    public void Scrolling_up_from_the_bottom_by_the_wheel_moves_steadily_to_the_top_without_jumping()
    {
        using var s = new Screen();
        s.Scroll.ScrollToEnd();
        s.Flush();
        s.Flush();
        var wheel = s.Scroll.TranslatePoint(new Point(60, 100), s.Window)!.Value;

        var steps = 0;
        var worstRowJump = 0;          // 1 ノッチで、先頭に見えている行の番号が変わった最大
        var worstOffsetJump = 0.0;     // 1 ノッチで、位置が動いた量の最大
        var worstBarBack = 0.0;        // バー（位置 ÷ 動ける範囲）が、上へ進んでいるのに下がった最大
        var prevRow = s.Panel.IndexAt(s.Scroll.Offset.Y);
        var prevOffset = s.Scroll.Offset.Y;
        double Bar() => s.Scroll.Offset.Y / Math.Max(1, s.Scroll.Extent.Height - s.Scroll.Viewport.Height);
        var prevBar = Bar();
        while (s.Scroll.Offset.Y > 0.01 && steps < 20000)
        {
            s.Window.MouseWheel(wheel, new Vector(0, 1));   // 上へ 1 ノッチ
            s.Flush();
            steps++;
            var row = s.Panel.IndexAt(s.Scroll.Offset.Y);
            worstRowJump = Math.Max(worstRowJump, Math.Abs(prevRow - row));
            worstOffsetJump = Math.Max(worstOffsetJump, Math.Abs(prevOffset - s.Scroll.Offset.Y));
            worstBarBack = Math.Max(worstBarBack, Bar() - prevBar);
            prevRow = row;
            prevOffset = s.Scroll.Offset.Y;
            prevBar = Bar();
        }

        var report = $"ノッチ {steps}・1 ノッチで行が変わった最大 {worstRowJump}・位置が動いた最大 {worstOffsetJump:F0}px・バーが下がった最大 {worstBarBack:P3}・最後の行 {prevRow}・全体の高さ {s.Scroll.Extent.Height:F0}";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "scrollbar-report.txt"), report);
        Assert.True(s.Scroll.Offset.Y <= 0.01, "先頭まで届く：" + report);
        Assert.Equal(0, prevRow);
        Assert.True(worstRowJump <= 3, "途中で飛ばない：" + report);              // 1 ノッチ（約 50px）は、高い行でも数行まで
        Assert.True(worstOffsetJump <= 120, "1 ノッチの動きが大きすぎない：" + report);
        Assert.True(worstBarBack < 0.002, "バーが戻らない：" + report);
    }

    [AvaloniaFact]
    public void The_total_height_stays_close_to_the_real_one_so_the_bar_is_trustworthy_from_the_start()
    {
        using var s = new Screen();
        var estimated = s.Scroll.Extent.Height;   // まだ上の行を作っていない（一番上の画面）ときの見積もり

        // 全部の行を一度作って測る（一番下まで、少しずつ）
        for (var y = 0.0; y < estimated; y += 600)
        {
            s.Scroll.Offset = new Vector(0, y);
            s.Flush();
        }
        var real = s.Scroll.Extent.Height;

        Assert.InRange(estimated / real, 0.8, 1.25);   // 見積もりが実際の ±20% 以内（標準の ListBox は 4 倍外れた）
    }

    [AvaloniaFact]
    public void Ensure_visible_scrolls_the_minimum_to_show_a_row()
    {
        using var s = new Screen();
        var target = s.Harness.Vm.Timeline.VisibleItems.Count / 2;

        s.Panel.EnsureVisible(target);
        s.Flush();
        Assert.InRange(s.Panel.OffsetOf(target), s.Scroll.Offset.Y - 1, s.Scroll.Offset.Y + s.Scroll.Viewport.Height);   // 見える位置にある

        var offset = s.Scroll.Offset.Y;
        s.Panel.EnsureVisible(target);   // 見えているなら動かない
        s.Flush();
        Assert.Equal(offset, s.Scroll.Offset.Y, 1);
    }

    [AvaloniaFact]
    public void Only_the_visible_rows_are_created()
    {
        using var s = new Screen();

        Assert.InRange(s.Panel.RealizedCount, 1, 60);
        Assert.True(s.Harness.Vm.Timeline.VisibleItems.Count > 1000);
    }
}
