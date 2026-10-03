using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

public sealed class TimelineSearchCopyTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 3, 19, 5, 0, TimeSpan.FromHours(9));

    private static TimelineItem Item(TimelineKind kind, string text, bool hasTime = true, long seq = 1) =>
        new(seq, null, At, kind, text, HasTime: hasTime);

    [Theory]
    [InlineData("AgentRegistry", true)]
    [InlineData("agentregistry", true)]            // 大文字小文字は無視
    [InlineData("  Registry  ", true)]             // 前後の空白は無視
    [InlineData("存在しない語", false)]
    [InlineData("", true)]                         // 空は全件
    [InlineData(null, true)]
    public void Search_matches_the_text_case_insensitively(string? query, bool expected) =>
        Assert.Equal(expected, TimelineText.Matches(Item(TimelineKind.Response, "`AgentRegistry` は工場です"), query));

    [Fact]
    public void Search_looks_at_the_full_text_not_only_the_shortened_preview()
    {
        // 画面では先頭だけ見せる長い本文でも、後ろにある語でヒットする
        var long_ = string.Concat(Enumerable.Repeat("あ", 5000)) + "末尾の語";
        Assert.True(TimelineText.Matches(Item(TimelineKind.Response, long_), "末尾の語"));
    }

    [Theory]
    [InlineData(TimelineKind.Input, "入力")]
    [InlineData(TimelineKind.Response, "返事")]
    [InlineData(TimelineKind.Thought, "思考")]
    [InlineData(TimelineKind.Tool, "ツール")]
    [InlineData(TimelineKind.Compaction, "圧縮")]
    [InlineData(TimelineKind.Other, "記録")]
    public void Kind_labels(TimelineKind kind, string expected) => Assert.Equal(expected, TimelineText.KindLabel(kind));

    [Fact]
    public void Copy_all_joins_rows_as_time_kind_and_full_body_with_a_blank_line_between()
    {
        var items = new[]
        {
            Item(TimelineKind.Input, "説明して"),
            Item(TimelineKind.Response, "1行目\n2行目"),
        };

        var text = TimelineText.CopyAll(items);

        var time = At.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Assert.Equal($"{time}　入力：説明して\n\n{time}　返事：1行目\n2行目", text);
    }

    [Fact]
    public void Copy_all_omits_the_time_for_rows_without_a_real_time_and_uses_the_full_text()
    {
        // 導入前に取り込んだセッションは時刻がない
        var long_ = string.Concat(Enumerable.Repeat("あ", 5000));
        var text = TimelineText.CopyAll([Item(TimelineKind.Input, long_, hasTime: false)]);

        Assert.Equal("入力：" + long_, text);
    }

    [Fact]
    public void Copy_all_of_nothing_is_empty() => Assert.Equal("", TimelineText.CopyAll([]));

    [Fact]
    public void Count_text_shows_shown_over_total_and_is_empty_without_a_query()
    {
        Assert.Equal("3/48件", TimelineText.CountText(shown: 3, total: 48, query: "語"));
        Assert.Equal("0/48件", TimelineText.CountText(shown: 0, total: 48, query: "語"));
        Assert.Equal("", TimelineText.CountText(shown: 48, total: 48, query: ""));
        Assert.Equal("", TimelineText.CountText(shown: 48, total: 48, query: "  "));
    }
}
