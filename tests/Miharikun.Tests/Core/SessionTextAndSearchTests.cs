using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class SessionTextAndSearchTests
{
    private static readonly SessionKey Key = new("cursor", "conv-1");

    private static SessionSummary Analyze(params (string, string, int)[] items) =>
        SessionAnalyzer.Analyze(Key, Events(items));

    [Theory]
    [InlineData(0, "たった今")]
    [InlineData(59, "たった今")]
    [InlineData(60, "1分前")]
    [InlineData(8 * 60, "8分前")]
    [InlineData(3 * 3600, "3時間前")]
    [InlineData(14 * 3600, "14時間前")]
    [InlineData(2 * 86400, "2日前")]
    [InlineData(-5, "たった今")]   // 時計のずれで未来になっても崩れない
    public void Relative_time(int secondsAgo, string expected) =>
        Assert.Equal(expected, SessionText.RelativeTime(T0, T0.AddSeconds(secondsAgo)));

    [Fact]
    public void State_labels_match_the_requirements()
    {
        Assert.Equal("🔵 実行中", SessionText.StateLabel(SessionState.Running));
        Assert.Equal("🟢 ボスの番", SessionText.StateLabel(SessionState.YourTurn));
        Assert.Equal("🟡 中断", SessionText.StateLabel(SessionState.Aborted));
        Assert.Equal("🔴 エラー", SessionText.StateLabel(SessionState.Error));
        Assert.Equal("⚪ 閉じた", SessionText.StateLabel(SessionState.Closed));
        Assert.Equal("⚪ 閉じた（導入前）", SessionText.StateLabel(SessionState.Imported));
    }

    [Fact]
    public void Three_line_summary_while_a_tool_is_running()
    {
        var s = Analyze(
            E("beforeSubmitPrompt", 0, "\"prompt\":\"\\n  最初の行\\n2行目\""),
            E("afterAgentResponse", 1, "\"text\":\"一行目\\n\\n二行目\\n三行目\""),
            E("preToolUse", 2, "\"tool_use_id\":\"t\",\"tool_name\":\"Shell\",\"tool_input\":{\"command\":\"npx playwright test\\nsecond\"}"));

        Assert.Equal("最初の行", SessionText.PromptLine(s));
        Assert.Equal("Shell: npx playwright test（実行中・42秒）", SessionText.ToolLine(s, T0.AddSeconds(2 + 42)));
        Assert.Equal("一行目\n二行目", SessionText.ResponseLines(s));
    }

    [Fact]
    public void Tool_line_falls_back_to_the_last_finished_tool()
    {
        var s = Analyze(
            E("postToolUse", 0, "\"tool_use_id\":\"a\",\"tool_name\":\"Read\""),
            E("postToolUse", 1, "\"tool_use_id\":\"b\",\"tool_name\":\"Shell\",\"tool_input\":{\"command\":\"ls\"}"));

        Assert.Equal("Shell: ls", SessionText.ToolLine(s, T0.AddSeconds(5)));
    }

    [Fact]
    public void Missing_parts_give_null()
    {
        var s = Analyze(E("sessionStart"));

        Assert.Null(SessionText.PromptLine(s));
        Assert.Null(SessionText.ToolLine(s, T0));
        Assert.Null(SessionText.ResponseLines(s));
    }

    [Fact]
    public void Search_text_covers_title_every_prompt_and_changed_files()
    {
        var events = Events(
            E("beforeSubmitPrompt", 0, "\"prompt\":\"テストタブを追加\""),
            E("beforeSubmitPrompt", 1, "\"prompt\":\"Playwright で確認\""),
            E("afterFileEdit", 2, "\"file_path\":\"src\\\\Tab.cs\""),
            E("afterAgentResponse", 3, "\"text\":\"返事は検索対象外\""));
        var summary = SessionAnalyzer.Analyze(Key, events);

        var text = SessionSearch.BuildSearchText(summary, events);

        Assert.True(SessionSearch.Matches(text, "テストタブ"));
        Assert.True(SessionSearch.Matches(text, "playwright"));     // 大文字小文字無視
        Assert.True(SessionSearch.Matches(text, "tab.cs"));         // 変更ファイル名
        Assert.False(SessionSearch.Matches(text, "返事は"));
        Assert.True(SessionSearch.Matches(text, "  "));             // 空白だけは全件
        Assert.True(SessionSearch.Matches(text, null));
    }
}