using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class SessionDetailTextTests
{
    private static readonly SessionKey Key = new("cursor", "conv-1");

    private static SessionSummary Analyze(params (string, string, int)[] items) =>
        SessionAnalyzer.Analyze(Key, Events(items));

    [Theory]
    [InlineData(0, "0秒")]
    [InlineData(42, "42秒")]
    [InlineData(59.9, "59秒")]
    [InlineData(60, "1分")]
    [InlineData(3 * 60 + 5, "3分")]
    [InlineData(72 * 60, "1時間12分")]
    [InlineData(25 * 3600, "25時間0分")]
    [InlineData(-5, "0秒")]
    public void Duration(double seconds, string expected) =>
        Assert.Equal(expected, SessionText.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Clock_shows_date_only_when_not_today()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var sameDay = now.AddHours(-1).ToLocalTime();
        var otherDay = now.AddDays(-2).ToLocalTime();

        Assert.Equal(sameDay.ToString("HH:mm"), SessionText.Clock(sameDay, now));
        Assert.Equal(otherDay.ToString("MM/dd HH:mm"), SessionText.Clock(otherDay, now));
    }

    [Theory]
    [InlineData(TurnStatus.Completed, "済み")]
    [InlineData(TurnStatus.Aborted, "中断")]
    [InlineData(TurnStatus.Error, "エラー")]
    [InlineData(TurnStatus.Running, "実行中")]
    [InlineData(TurnStatus.Unknown, "結果不明")]
    public void Turn_status_label(TurnStatus status, string expected) =>
        Assert.Equal(expected, SessionText.TurnStatusLabel(status));

    [Fact]
    public void Model_text_formats_id_value_params()
    {
        var s = Analyze(E("stop", 0, "\"model_id\":\"claude-x\",\"model_params\":[{\"id\":\"thinking\",\"value\":\"on\"},{\"id\":\"effort\",\"value\":\"high\"}]"));
        Assert.Equal("claude-x（thinking: on, effort: high）", SessionText.ModelText(s));

        Assert.Equal("auto", SessionText.ModelText(Analyze(E("stop", 0, "\"model\":\"auto\""))));
        Assert.Equal("auto", SessionText.ModelText(Analyze(E("stop", 0, "\"model\":\"auto\",\"model_params\":[]"))));
        Assert.Null(SessionText.ModelText(Analyze(E("stop"))));
    }

    [Fact]
    public void Model_text_keeps_unknown_param_shapes_visible()
    {
        var s = Analyze(E("stop", 0, "\"model\":\"m\",\"model_params\":[\"fast\"]"));

        Assert.Equal("m（\"fast\"）", SessionText.ModelText(s));
    }

    [Fact]
    public void Compaction_text()
    {
        Assert.Equal("なし", SessionText.CompactionText(Analyze(E("sessionStart"))));
        Assert.Equal("1回（直近 85%・auto）",
            SessionText.CompactionText(Analyze(E("preCompact", 0, "\"trigger\":\"auto\",\"context_usage_percent\":85"))));
        Assert.Equal("1回", SessionText.CompactionText(Analyze(E("preCompact"))));
    }

    [Fact]
    public void Test_runs_text_counts_unknown_separately()
    {
        string T(string id, string result) =>
            $"\"tool_use_id\":\"{id}\",\"tool_name\":\"Shell\",\"tool_input\":{{\"command\":\"dotnet test\"}}{result}";
        var s = Analyze(
            E("postToolUse", 0, T("1", ",\"tool_output\":{\"exitCode\":0}")),
            E("postToolUse", 1, T("2", ",\"tool_output\":{\"exitCode\":0}")),
            E("postToolUse", 2, T("3", ",\"tool_output\":{\"exitCode\":1}")),
            E("postToolUse", 3, T("4", "")));

        Assert.Equal("4回（成功2 / 失敗1 / 不明1）", SessionText.TestRunsText(s));
        Assert.Equal("なし", SessionText.TestRunsText(Analyze(E("sessionStart"))));
    }

    [Fact]
    public void Turn_in_progress_flag()
    {
        Assert.True(Analyze(E("beforeSubmitPrompt", 0, "\"prompt\":\"a\"")).TurnInProgress);
        Assert.False(Analyze(E("beforeSubmitPrompt", 0, "\"prompt\":\"a\""), E("stop", 1, "\"status\":\"completed\"")).TurnInProgress);
        Assert.False(Analyze(E("sessionStart")).TurnInProgress);
    }
}