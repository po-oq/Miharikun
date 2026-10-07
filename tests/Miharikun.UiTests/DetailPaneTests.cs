using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Tests.Presentation;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>中央ペイン（詳細。要件 12.3）：ブロックが全部出る・概要の編集・3 行サマリーのジャンプ・ステータスのボタン。</summary>
public sealed class DetailPaneTests
{
    private static (Window Window, MainVmHarness Harness) Open()
    {
        var h = new MainVmHarness();
        Scene.Fill(h);   // s2（概要・メモ・ステータス「作業中」つき）を選んだ状態
        var window = new Window { Width = 1280, Height = 1100, Content = new DashboardView { DataContext = h.Vm } };
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
        w.GetVisualDescendants().OfType<T>().First(c => c.IsVisible && Avalonia.Automation.AutomationProperties.GetAutomationId(c) == automationId);

    [AvaloniaFact]
    public void Every_block_of_the_detail_is_shown()
    {
        var (window, h) = Open();
        using var _ = h;

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        foreach (var heading in new[] { "セッション概要", "いまの状況", "完了チェック", "稼働状態（コストの目安）", "成果", "ターン一覧" })
            Assert.Contains(heading, texts);
        Assert.Contains(texts, t => t.StartsWith("ログイン画面のバリデーションを直した"));   // 概要の本文
        Assert.Contains(texts, t => t.Contains("最後に頼んだこと"));
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "作業中" && b.Classes.Contains("primary"));   // 選択中のステータス
    }

    [AvaloniaFact]
    public void Editing_the_summary_shows_the_box_with_the_focus_and_saving_changes_the_summary()
    {
        var (window, h) = Open();
        using var _ = h;

        var edit = window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string)?.Contains("編集") == true && b.IsEffectivelyVisible);
        edit.Command!.Execute(null);
        Flush();
        var box = Find<TextBox>(window, "SummaryDraftBox");
        Assert.True(box.IsFocused);
        Assert.StartsWith("ログイン画面のバリデーション", box.Text);   // いまの概要が入っている

        box.Text = "概要を書き換えた";
        window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "保存" && b.IsEffectivelyVisible).Command!.Execute(null);
        Flush();

        Assert.False(h.Vm.Detail!.IsEditingSummary);
        Assert.Equal("概要を書き換えた", h.Vm.Detail.SummaryText);
    }

    [AvaloniaFact]
    public void Cancelling_the_summary_edit_keeps_the_old_summary()
    {
        var (window, h) = Open();
        using var _ = h;
        var before = h.Vm.Detail!.SummaryText;

        h.Vm.Detail.EditSummaryCommand.Execute(null);
        Flush();
        Find<TextBox>(window, "SummaryDraftBox").Text = "捨てる";
        h.Vm.Detail.CancelSummaryCommand.Execute(null);
        Flush();

        Assert.Equal(before, h.Vm.Detail.SummaryText);
    }

    [AvaloniaFact]
    public void Clicking_a_summary_line_jumps_in_the_timeline_and_a_line_without_a_target_is_disabled()
    {
        var (window, h) = Open();
        using var _ = h;
        var jumped = new List<object>();
        h.Vm.Timeline.ScrollRequested += item => jumped.Add(item);

        var prompt = Find<Button>(window, "JumpPrompt");
        Assert.True(prompt.IsEffectivelyEnabled);
        prompt.Command!.Execute(null);
        Flush();
        Assert.Single(jumped);

        Assert.False(Find<Button>(window, "JumpTool").IsEffectivelyEnabled);   // このセッションはツールを使っていない
    }

    [AvaloniaFact]
    public void The_status_buttons_toggle_and_the_selected_one_is_marked()
    {
        var (window, h) = Open();
        using var _ = h;
        var buttons = () => window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("statusbtn")).ToList();

        Assert.Equal(["作業中"], buttons().Where(b => b.Classes.Contains("primary")).Select(b => (string)b.Content!));

        buttons().First(b => b.Content as string == "完了").Command!.Execute(Miharikun.Core.Meta.SessionStatus.Done);
        Flush();
        Assert.Equal(["完了"], buttons().Where(b => b.Classes.Contains("primary")).Select(b => (string)b.Content!));

        buttons().First(b => b.Content as string == "完了").Command!.Execute(Miharikun.Core.Meta.SessionStatus.Done);   // もう一度で未設定
        Flush();
        Assert.Equal(["未設定"], buttons().Where(b => b.Classes.Contains("primary")).Select(b => (string)b.Content!));
    }
}
