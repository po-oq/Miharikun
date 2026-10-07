using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Tests.Presentation;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>右ペイン（要件 12.4）：メモ・タイムライン（行・クリックで開閉・ダブルクリックでコピー）・拡大モード・最近の入力。</summary>
public sealed class RightPaneTests
{
    private static (Window Window, MainVmHarness Harness) Open()
    {
        var h = new MainVmHarness();
        Scene.Fill(h);   // s2（メモつき・タイムラインに長い返事つき）を選んだ状態
        var window = new Window { Width = 1280, Height = 1800, Content = new DashboardView { DataContext = h.Vm } };
        window.Show();
        Flush();
        return (window, h);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Window w, string automationId) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == automationId);

    private static Border TruncatedRow(Window w) =>
        w.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("trow") && b.Classes.Contains("truncated"));

    private static Point CenterIn(Window w, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, 12), w)!.Value;

    [AvaloniaFact]
    public void The_timeline_shows_a_row_per_visible_item_with_the_kind_as_a_style_class()
    {
        var (window, h) = Open();
        using var _ = h;

        var rows = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("trow") && b.IsVisible).ToList();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.Classes.Contains("input"));
        Assert.Contains(rows, r => r.Classes.Contains("response"));
        Assert.Contains(rows, r => r.Classes.Contains("compaction"));
        Assert.Contains(rows, r => r.Classes.Contains("truncated"));   // 長い返事
    }

    [AvaloniaFact]
    public void One_click_opens_a_long_row_and_a_double_click_copies_it_and_leaves_it_closed()
    {
        var (window, h) = Open();
        using var _ = h;
        var item = (TimelineItemViewModel)TruncatedRow(window).DataContext!;
        Assert.False(item.IsExpanded);

        var point = CenterIn(window, TruncatedRow(window));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
        Assert.True(item.IsExpanded);   // 1 回目のクリックで全文が開く

        window.MouseDown(point, MouseButton.Left);   // 2 回目（ダブルクリック。同じ場所・すぐに）
        window.MouseUp(point, MouseButton.Left);
        Flush();

        Assert.False(item.IsExpanded);   // コピー側で、1 回目の開閉が元に戻る
        Assert.Contains(h.Ui.Clipboard, text => text.Contains("空欄・空白だけ"));
        Assert.True(item.JustCopied);
    }

    [AvaloniaFact]
    public void The_memo_is_typed_into_the_view_model_and_saved_when_the_box_loses_the_focus()
    {
        var (window, h) = Open();
        using var _ = h;
        var key = h.Vm.Selected!.Key;
        var box = Find<TextBox>(window, "MemoBox");
        Assert.True(box.IsEnabled);
        Assert.Equal("明日、文言をデザイナーに確認する", box.Text);

        box.Focus();
        box.Text = "続きは明日";
        Assert.Equal("続きは明日", h.Vm.Detail!.MemoText);   // 入力するたびに VM へ
        Find<TextBox>(window, "TimelineSearch").Focus();   // フォーカスアウトで保存
        Flush();

        Assert.Equal("続きは明日", h.Meta.Get(key).Memo);
    }

    [AvaloniaFact]
    public void The_memo_box_is_disabled_without_a_selected_session()
    {
        using var h = new MainVmHarness();
        var window = new Window { Width = 1280, Height = 900, Content = new DashboardView { DataContext = h.Vm } };
        window.Show();
        Flush();

        Assert.False(Find<TextBox>(window, "MemoBox").IsEnabled);
    }

    [AvaloniaFact]
    public void The_expand_mode_hides_the_left_and_center_panes_and_the_recent_lists_and_escape_brings_them_back()
    {
        var (window, h) = Open();
        using var _ = h;
        var dashboard = window.GetVisualDescendants().OfType<DashboardView>().First();
        var left = dashboard.FindControl<Border>("PaneLeft")!;
        var center = dashboard.FindControl<Border>("PaneCenter")!;
        var widthBefore = dashboard.FindControl<Border>("PaneRight")!.Bounds.Width;
        var expand = Find<Button>(window, "ExpandButton");
        Assert.Equal("⤢ 拡大", expand.Content);

        expand.Command!.Execute(null);
        Flush();
        Assert.True(h.Vm.IsTimelineExpanded);
        Assert.False(left.IsVisible);
        Assert.False(center.IsVisible);
        Assert.Equal("⤡ 戻す", expand.Content);
        Assert.True(dashboard.FindControl<Border>("PaneRight")!.Bounds.Width > widthBefore * 2);   // 全幅に広がる

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Flush();
        Assert.False(h.Vm.IsTimelineExpanded);
        Assert.True(left.IsVisible);
        Assert.True(center.IsVisible);
        Assert.Equal(widthBefore, dashboard.FindControl<Border>("PaneRight")!.Bounds.Width, 1);   // 元の幅に戻る
    }

    [AvaloniaFact]
    public void Choosing_a_recent_input_selects_that_session()
    {
        var (window, h) = Open();
        using var _ = h;
        Assert.Equal("s2", h.Vm.Selected!.Key.SessionId);

        var recent = Find<ItemsControl>(window, "RecentInputs").GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("設定画面の保存ボタン") == true));
        recent.Command!.Execute(recent.CommandParameter);
        Flush();

        Assert.Equal("s1", h.Vm.Selected!.Key.SessionId);
    }

    [AvaloniaFact]
    public void The_kind_chips_and_the_search_box_narrow_the_rows()
    {
        var (window, h) = Open();
        using var _ = h;
        int Rows() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("trow") && b.IsVisible);   // 捨てた行（プールに隠してある）は数えない
        var all = Rows();
        Assert.Equal(h.Vm.Timeline.VisibleItems.Count, all);   // 背の高いウィンドウなので、全部の行が作られている

        h.Vm.Timeline.ShowCompaction = false;
        Flush();
        Assert.Equal(all - 1, Rows());

        Find<TextBox>(window, "TimelineSearch").Text = "テストも足して";
        Flush();
        Assert.Equal(1, Rows());
        Assert.Contains("1/", h.Vm.Timeline.CountText);
    }
}
