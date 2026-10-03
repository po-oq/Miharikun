using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class TimelineTests
{
    private static readonly SessionKey Key = new("cursor", "conv-1");

    private static string Tool(string id, string name = "Shell", string? cmd = null) =>
        $"\"tool_use_id\":\"{id}\",\"tool_name\":\"{name}\"" + (cmd is null ? "" : $",\"tool_input\":{{\"command\":\"{cmd}\"}}");

    private static List<TimelineItem> Build(params (string, string, int)[] items) => TimelineBuilder.Build(Events(items));

    [Fact]
    public void Maps_event_kinds_and_skips_events_that_are_not_in_the_timeline()
    {
        var items = Build(
            E("sessionStart", 0),
            E("beforeSubmitPrompt", 1, "\"prompt\":\"依頼\""),
            E("afterAgentThought", 2, "\"text\":\"考え中\""),
            E("afterAgentResponse", 3, "\"text\":\"返事\""),
            E("preCompact", 4, "\"trigger\":\"auto\",\"context_usage_percent\":85"),
            E("afterFileEdit", 5, "\"file_path\":\"a.cs\""),
            E("subagentStart", 6),
            E("stop", 7, "\"status\":\"completed\""),
            E("sessionEnd", 8, "\"reason\":\"user_close\""));

        Assert.Equal(
            [TimelineKind.Other, TimelineKind.Input, TimelineKind.Thought, TimelineKind.Response, TimelineKind.Compaction, TimelineKind.Other],
            items.Select(i => i.Kind));
        Assert.Equal(["セッション開始", "依頼", "考え中", "返事", "圧縮（auto・85%）", "セッション終了（user_close）"],
            items.Select(i => i.Text));
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 9L], items.Select(i => i.Seq));
    }

    [Fact]
    public void Events_whose_text_could_not_be_read_are_not_blank_rows()
    {
        var items = Build(
            E("beforeSubmitPrompt", 0),
            E("afterAgentThought", 1),
            E("afterAgentResponse", 2, "\"text\":\"  \""));

        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
        Assert.Equal(["（依頼文を取得できませんでした）", "（本文を取得できませんでした）", "（本文を取得できませんでした）"], items.Select(i => i.Text));
    }

    [Fact]
    public void Running_tool_is_shown_from_its_pre_event()
    {
        var items = Build(E("preToolUse", 0, Tool("t1", cmd: "npx playwright test")));

        var item = Assert.Single(items);
        Assert.Equal(("Shell: npx playwright test（実行中）", true, TimelineKind.Tool), (item.Text, item.IsRunning, item.Kind));
    }

    [Fact]
    public void Finished_tool_replaces_its_pre_row_in_place_with_duration_and_exit_code()
    {
        var items = Build(
            E("preToolUse", 0, Tool("t1", cmd: "dotnet test")),
            E("afterAgentResponse", 1, "\"text\":\"途中の返事\""),
            E("postToolUse", 2, Tool("t1", cmd: "dotnet test") + ",\"duration\":12300,\"tool_output\":{\"exitCode\":0}"));

        Assert.Equal(2, items.Count);
        Assert.Equal(TimelineKind.Tool, items[0].Kind);          // 開始の位置のまま
        Assert.Equal("Shell: dotnet test（12秒・exit 0）", items[0].Text);
        Assert.False(items[0].IsRunning);
        Assert.Equal((1L, 3L), (items[0].Seq, items[0].EndSeq));
    }

    [Fact]
    public void Failed_tool_says_so()
    {
        var items = Build(
            E("preToolUse", 0, Tool("t1", "Read")),
            E("postToolUseFailure", 1, Tool("t1", "Read") + ",\"failure_type\":\"timeout\",\"duration\":500"));

        Assert.Equal("Read（失敗: timeout・1秒未満）", Assert.Single(items).Text);
    }

    [Fact]
    public void Post_without_pre_is_added_as_its_own_row()
    {
        var item = Assert.Single(Build(E("postToolUse", 0, Tool("t1", "Read"))));

        Assert.Equal((1L, 1L, "Read"), (item.Seq, item.EndSeq, item.Text));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("sessionEnd")]
    public void Unfinished_tool_is_closed_as_unknown_by_stop_or_session_end(string closing)
    {
        var items = Build(E("preToolUse", 0, Tool("t1", cmd: "ls")), E(closing, 1, closing == "stop" ? "\"status\":\"aborted\"" : ""));

        Assert.Equal("Shell: ls（終了・結果不明）", items[0].Text);
        Assert.False(items[0].IsRunning);
    }

    [Fact]
    public void Build_is_deterministic_so_a_prefix_stays_equal_when_events_are_appended()
    {
        var events = Events(
            E("beforeSubmitPrompt", 0, "\"prompt\":\"a\""), E("afterAgentResponse", 1, "\"text\":\"b\""),
            E("preToolUse", 2, Tool("t1", cmd: "x")));
        var before = TimelineBuilder.Build(events);

        var more = new List<AgentEvent>(events);
        more.AddRange(Events(E("beforeSubmitPrompt", 3, "\"prompt\":\"c\"")).Select(e => e with { Seq = 4 }));
        var after = TimelineBuilder.Build(more);

        Assert.Equal(before, after.Take(before.Count));   // 末尾以外は変わらない＝差分更新でスクロール位置を保てる
    }

    [Fact]
    public void Find_matches_start_or_end_seq()
    {
        var items = Build(E("preToolUse", 0, Tool("t1")), E("postToolUse", 1, Tool("t1")));

        Assert.Same(items[0], TimelineBuilder.Find(items, 1));
        Assert.Same(items[0], TimelineBuilder.Find(items, 2));
        Assert.Null(TimelineBuilder.Find(items, 99));
    }

    [Fact]
    public void Summary_jump_targets_point_at_the_events_shown_in_the_summary()
    {
        var running = SessionAnalyzer.Analyze(Key, Events(
            E("beforeSubmitPrompt", 0, "\"prompt\":\"p\""), E("afterAgentResponse", 1, "\"text\":\"r\""),
            E("preToolUse", 2, Tool("t1"))));
        Assert.Equal((1L, TimelineKind.Input), SummaryJump.Prompt(running));
        Assert.Equal((2L, TimelineKind.Response), SummaryJump.Response(running));
        Assert.Equal((3L, TimelineKind.Tool), SummaryJump.Tool(running));

        var finished = SessionAnalyzer.Analyze(Key, Events(E("preToolUse", 0, Tool("t1")), E("postToolUse", 1, Tool("t1"))));
        Assert.Equal((2L, TimelineKind.Tool), SummaryJump.Tool(finished));   // 結果側。Find で同じ行に解決される

        var empty = SessionAnalyzer.Analyze(Key, Events(E("sessionStart")));
        Assert.Null(SummaryJump.Prompt(empty));
        Assert.Null(SummaryJump.Tool(empty));
        Assert.Null(SummaryJump.Response(empty));
    }
}