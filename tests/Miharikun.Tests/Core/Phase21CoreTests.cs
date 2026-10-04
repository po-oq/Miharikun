using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Settings;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Phase 21：画面から使う Core の小さな部品（サブエージェントの文言・タイムラインへのジャンプ・設定の入力の検査・transcript のパス）。</summary>
public sealed class Phase21CoreTests
{
    private readonly ClaudeLogBuilder _b = new();

    private static SessionSummary Analyze(IEnumerable<string> lines, ClaudeLogBuilder? b = null, string? path = null)
    {
        var normalizer = new ClaudeTranscriptNormalizer("sess-1", "sess-1.jsonl", null, path);
        var events = new List<AgentEvent>();
        var i = 0;
        foreach (var line in lines)
            events.AddRange(normalizer.NormalizeLine(++i, line));
        return SessionAnalyzer.Analyze(new SessionKey("claude", "sess-1"), events);
    }

    // ---- サブエージェントの文言 ----

    [Fact]
    public void Subagent_text_is_none_without_subagents()
    {
        Assert.Equal("なし", SessionText.SubagentText(Analyze([_b.User("a")])));
    }

    [Fact]
    public void Subagent_text_shows_running_with_descriptions_and_the_finished_count()
    {
        var s = Analyze([
            _b.User("a"),
            _b.AgentCall("a1", "調べる"), _b.AgentCall("a2", "直す"), _b.AgentCall("a3", "終わるもの"),
            _b.AgentResult("a3")]);

        Assert.Equal("動いている 2（調べる、直す） / 動いた 1", SessionText.SubagentText(s));
    }

    [Fact]
    public void Subagent_text_has_zero_running_and_no_parentheses_when_all_finished()
    {
        var s = Analyze([_b.User("a"), _b.AgentCall("a1", "調べる"), _b.AgentResult("a1")]);

        Assert.Equal("動いている 0 / 動いた 1", SessionText.SubagentText(s));
    }

    [Fact]
    public void Subagent_text_limits_the_descriptions_and_cuts_long_ones()
    {
        var long_ = new string('あ', 40);
        var s = Analyze([
            _b.User("a"),
            _b.AgentCall("a1", long_), _b.AgentCall("a2", "二"), _b.AgentCall("a3", "三"), _b.AgentCall("a4", "四"), _b.AgentCall("a5", "五")]);

        var text = SessionText.SubagentText(s);

        Assert.StartsWith("動いている 5（" + new string('あ', 24) + "…、二、三 ほか） / 動いた 0", text);
    }

    [Fact]
    public void Subagent_text_without_descriptions_has_only_the_count()
    {
        var s = Analyze([
            _b.User("a"),
            _b.ToolUse("a1", "Agent", new JsonObject { ["prompt"] = "…" })]);

        Assert.Equal("動いている 1 / 動いた 0", SessionText.SubagentText(s));
    }

    // ---- タイムラインへのジャンプ（Seq と種類の両方が一致する行を優先） ----

    [Fact]
    public void Jump_prefers_the_row_whose_seq_and_kind_both_match()
    {
        var items = new List<TimelineItem>
        {
            new(1, null, ClaudeLogBuilder.Start, TimelineKind.Other, "セッション開始"),
            new(1, null, ClaudeLogBuilder.Start, TimelineKind.Input, "最初の依頼"),
            new(2, 3, ClaudeLogBuilder.Start, TimelineKind.Tool, "Read"),
        };

        Assert.Equal("最初の依頼", TimelineBuilder.Find(items, 1, TimelineKind.Input)!.Text);
        Assert.Equal("セッション開始", TimelineBuilder.Find(items, 1, TimelineKind.Other)!.Text);
        Assert.Equal("Read", TimelineBuilder.Find(items, 3, TimelineKind.Tool)!.Text);   // 結果側の番号でも見つかる
    }

    [Fact]
    public void Jump_falls_back_to_the_seq_alone_and_to_nothing()
    {
        var items = new List<TimelineItem> { new(5, null, ClaudeLogBuilder.Start, TimelineKind.Response, "返事") };

        Assert.Equal("返事", TimelineBuilder.Find(items, 5, TimelineKind.Input)!.Text);   // 種類が違っても、番号が合えば（今までどおり）
        Assert.Null(TimelineBuilder.Find(items, 9, TimelineKind.Input));
    }

    [Fact]
    public void Jump_in_a_real_claude_log_goes_to_the_prompt_not_the_session_start()
    {
        var events = new List<AgentEvent>();
        var normalizer = new ClaudeTranscriptNormalizer("sess-1", "sess-1.jsonl");
        events.AddRange(normalizer.NormalizeLine(1, _b.User("最初の依頼")));   // 同じ行から SessionStarted と PromptSubmitted が出る
        var items = TimelineBuilder.Build(events);
        var summary = SessionAnalyzer.Analyze(new SessionKey("claude", "sess-1"), events);

        var (seq, kind) = SummaryJump.Prompt(summary)!.Value;

        Assert.Equal(TimelineKind.Input, TimelineBuilder.Find(items, seq, kind)!.Kind);
    }

    // ---- 設定の入力の検査 ----

    [Theory]
    [InlineData("10", 10)]
    [InlineData("0", 0)]
    [InlineData(" 25 ", 25)]
    [InlineData("007", 7)]
    [InlineData("10080", 10080)]
    public void Valid_timeouts_are_accepted(string text, int expected)
    {
        Assert.True(RunningTimeoutInput.TryParse(text, out var minutes));
        Assert.Equal(expected, minutes);
        Assert.Null(RunningTimeoutInput.Validate(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("10分")]
    [InlineData("abc")]
    [InlineData("１０")]          // 全角の数字
    [InlineData("10080 1")]
    [InlineData("10081")]        // 1 週間より大きい
    [InlineData("99999999999")]  // int に収まらない
    public void Bad_timeouts_are_rejected_with_a_message(string? text)
    {
        Assert.False(RunningTimeoutInput.TryParse(text, out var minutes));
        Assert.Equal(0, minutes);
        Assert.NotNull(RunningTimeoutInput.Validate(text));
    }

    // ---- transcript のパス ----

    [Fact]
    public void The_transcript_path_is_carried_to_the_summary()
    {
        var s = Analyze([_b.User("a"), _b.AssistantText("b")], path: @"C:\x\.claude\projects\p\sess-1.jsonl");

        Assert.Equal(@"C:\x\.claude\projects\p\sess-1.jsonl", s.TranscriptPath);
    }

    [Fact]
    public void Without_a_path_there_is_no_transcript_path()
    {
        Assert.Null(Analyze([_b.User("a")]).TranscriptPath);
    }
}
