using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Tests.Presentation;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>
/// 選択の保ち方（計画 7.3）：実際の <c>ListBox</c> で、並べ替え・追加・絞り込みをしても、選んでいるカードの選択が外れない
/// （Avalonia の <c>ListBox</c> は <c>Move</c> を「取り除く＋足す」として扱い、選択を外すため、VM が選択中のカードを動かさずに合わせる）。
/// 絞り込みで隠れたときだけ外れる。
/// </summary>
public sealed class CardSelectionTests
{
    private static readonly DateTimeOffset T = MainVmHarness.Now;

    private sealed class Screen : IDisposable
    {
        public MainVmHarness Harness { get; } = new();
        public Window Window { get; }
        public ListBox List { get; }

        public Screen()
        {
            Window = new Window { Width = 420, Height = 700, Content = new LeftPaneView { DataContext = Harness.Vm } };
            Window.Show();
            Flush();
            List = Window.GetVisualDescendants().OfType<ListBox>().First(l => l.Classes.Contains("cards"));
        }

        public void Add(params Miharikun.Core.Sessions.SessionSnapshot[] snaps)
        {
            Harness.Add(snaps);
            Flush();
        }

        public void Dispose()
        {
            Window.Close();
            Harness.Dispose();
        }
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static Screen ThreeCards()
    {
        var s = new Screen();
        s.Add(s.Harness.Snap("a", "A", T.AddMinutes(-1)), s.Harness.Snap("b", "B", T.AddMinutes(-2)), s.Harness.Snap("c", "C", T.AddMinutes(-3)));
        return s;
    }

    [AvaloniaFact]
    public void A_card_selected_by_the_list_control_is_selected_in_the_view_model()
    {
        using var s = ThreeCards();

        s.List.SelectedItem = s.Harness.Card("b");
        Flush();

        Assert.Same(s.Harness.Card("b"), s.Harness.Vm.Selected);
        Assert.NotNull(s.Harness.Vm.Detail);
    }

    [AvaloniaFact]
    public void The_selection_is_kept_when_the_selected_card_moves_up()
    {
        using var s = ThreeCards();
        s.Harness.Vm.Selected = s.Harness.Card("b");
        Flush();

        s.Add(s.Harness.Snap("b", "B", T));   // b が一番新しくなる（上へ移る）

        Assert.Equal("b,a,c", s.Harness.Order());
        Assert.Same(s.Harness.Card("b"), s.List.SelectedItem);
        Assert.Same(s.Harness.Card("b"), s.Harness.Vm.Selected);
    }

    [AvaloniaFact]
    public void The_selection_is_kept_when_another_card_moves_above_the_selected_one()
    {
        using var s = ThreeCards();
        s.Harness.Vm.Selected = s.Harness.Card("b");
        Flush();

        s.Add(s.Harness.Snap("c", "C", T));   // c が上へ移る（b は残る）

        Assert.Equal("c,a,b", s.Harness.Order());
        Assert.Same(s.Harness.Card("b"), s.List.SelectedItem);
        Assert.Same(s.Harness.Card("b"), s.Harness.Vm.Selected);
        Assert.NotNull(s.Harness.Vm.Detail);
    }

    [AvaloniaFact]
    public void The_selection_is_kept_when_a_new_card_is_added_and_when_the_selected_one_is_pushed_down()
    {
        using var s = ThreeCards();
        s.Harness.Vm.Selected = s.Harness.Card("a");
        Flush();

        s.Add(s.Harness.Snap("d", "D", T), s.Harness.Snap("e", "E", T.AddSeconds(-1)));

        Assert.Equal("d,e,a,b,c", s.Harness.Order());
        Assert.Same(s.Harness.Card("a"), s.List.SelectedItem);
    }

    [AvaloniaFact]
    public void The_selection_is_kept_while_a_filter_still_shows_the_selected_card_and_cleared_when_it_hides_it()
    {
        using var s = ThreeCards();
        s.Harness.Vm.Selected = s.Harness.Card("a");
        Flush();

        s.Harness.Vm.SearchText = "A";   // a は残る
        Flush();
        Assert.Same(s.Harness.Card("a"), s.List.SelectedItem);

        s.Harness.Vm.SearchText = "B";   // a が隠れる
        Flush();
        Assert.Null(s.List.SelectedItem);
        Assert.Null(s.Harness.Vm.Selected);
        Assert.Null(s.Harness.Vm.Detail);
    }

    [AvaloniaFact]
    public void The_list_shows_the_filter_chips_the_count_and_the_search_box_of_the_view_model()
    {
        using var s = ThreeCards();

        var texts = s.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, t => t.StartsWith("全て"));      // ステータスのチップ
        Assert.Contains(texts, t => t.StartsWith("Cursor"));    // エージェントのチップ
        Assert.Contains(s.Window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content as string == "実行中のみ");
        Assert.Contains(s.Window.GetVisualDescendants().OfType<TextBox>(),
            t => Avalonia.Automation.AutomationProperties.GetAutomationId(t) == "SearchBox");
    }

    [AvaloniaFact]
    public void The_agent_chip_is_selected_at_the_start_and_choosing_a_chip_filters_the_cards()
    {
        using var s = new Screen();
        s.Add(s.Harness.Snap("a", "A", T.AddMinutes(-1), agent: "claude"), s.Harness.Snap("b", "B", T.AddMinutes(-2), agent: "cursor"));
        var chips = s.Window.GetVisualDescendants().OfType<ListBox>().First(l => l.Classes.Contains("chips"));

        Assert.Equal("", s.Harness.Vm.AgentKey);                       // 起動時は「全て」（null が書き戻されない）
        Assert.Equal("", ((AgentTabItem)chips.SelectedItem!).Key);

        chips.SelectedItem = s.Harness.Vm.AgentTabs.Single(t => t.Key == "claude");
        Flush();
        Assert.Equal("claude", s.Harness.Vm.AgentKey);
        Assert.Equal("a", s.Harness.Order());

        chips.SelectedItem = s.Harness.Vm.AgentTabs.Single(t => t.Key == "");
        Flush();
        Assert.Equal("a,b", s.Harness.Order());
    }

    [AvaloniaFact]
    public void The_hook_warning_band_appears_only_while_sessions_have_no_hook_and_shows_the_os_wording()
    {
        using var s = ThreeCards();
        Border Band() => s.Window.GetVisualDescendants().OfType<Border>()
            .First(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "HookWarning");

        Assert.False(Band().IsVisible);

        s.Harness.Vm.NoHookCount = 3;
        Flush();

        Assert.True(Band().IsVisible);
        var text = Band().GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).First(t => t is not null && t.Contains("Hook なし 3 件"));
        Assert.Contains(OperatingSystem.IsWindows() ? "この PC で" : "この Mac で", text);
        Assert.Contains(Band().GetVisualDescendants().OfType<Button>(), b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == "OpenHookErrorLog");

        s.Harness.Vm.NoHookCount = 0;
        Flush();
        Assert.False(Band().IsVisible);
    }
}
