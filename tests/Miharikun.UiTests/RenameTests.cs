using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Tests.Presentation;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>タイトルの名前の変更（鉛筆）：表示されたらフォーカス、フォーカスアウトで確定、Esc で取り消し（計画 7.9）。</summary>
public sealed class RenameTests
{
    private static (Window Window, MainVmHarness Harness) Open()
    {
        var h = new MainVmHarness();
        Scene.Fill(h);
        var window = new Window { Width = 1280, Height = 560, Content = new DashboardView { DataContext = h.Vm } };
        window.Show();
        Flush();
        return (window, h);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Box(Window window, string automationId) =>
        window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible && Avalonia.Automation.AutomationProperties.GetAutomationId(t) == automationId);

    [AvaloniaFact]
    public void The_box_gets_the_focus_when_it_appears_and_losing_the_focus_commits_the_new_title()
    {
        var (window, h) = Open();
        using var _ = h;

        h.Vm.Detail!.Rename.BeginCommand.Execute(null);
        Flush();
        var box = Box(window, "DetailRenameBox");
        Assert.True(box.IsVisible);
        Assert.True(box.IsFocused);   // 表示された瞬間にフォーカスされる

        box.Text = "新しい名前";
        Box(window, "TimelineSearch").Focus();   // フォーカスアウト
        Flush();

        Assert.False(h.Vm.Detail.Rename.IsEditing);
        Assert.Equal("新しい名前", h.Vm.Selected!.Title);
    }

    [AvaloniaFact]
    public void Escape_cancels_without_saving_even_though_the_box_then_loses_the_focus()
    {
        var (window, h) = Open();
        using var _ = h;
        var before = h.Vm.Selected!.Title;

        h.Vm.Detail!.Rename.BeginCommand.Execute(null);
        Flush();
        Box(window, "DetailRenameBox").Text = "取り消す名前";
        h.Vm.Detail.Rename.CancelCommand.Execute(null);   // Esc のキー操作と同じコマンド
        Flush();
        Box(window, "TimelineSearch").Focus();
        Flush();

        Assert.Equal(before, h.Vm.Selected.Title);
    }

    [AvaloniaFact]
    public void The_card_title_box_also_commits_when_it_loses_the_focus()
    {
        var (window, h) = Open();
        using var _ = h;
        var card = h.Vm.Selected!;

        card.Rename.BeginCommand.Execute(null);
        Flush();
        var box = Box(window, "CardRenameBox");
        Assert.True(box.IsFocused);

        box.Text = "カードで変えた名前";
        Box(window, "TimelineSearch").Focus();
        Flush();

        Assert.Equal("カードで変えた名前", card.Title);
    }
}
