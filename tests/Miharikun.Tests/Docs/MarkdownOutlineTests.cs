using System.Text.RegularExpressions;
using Miharikun.Docs;

namespace Miharikun.Tests.Docs;

public sealed class MarkdownOutlineTests
{
    private static readonly string Folder = TestPaths.Abs("work", "proj", "docs");

    private static MarkdownRendering Run(string md) => MarkdownRenderer.RenderWithOutline(md, Folder, false);

    private static IReadOnlyList<MarkdownHeading> Headings(string md) => Run(md).Headings;

    // ── id が HTML の id と一致する ───────────────────────────────

    [Theory]
    [InlineData("# 概要\n\n## Phase 1\n\n## Phase 1\n")]               // 同じ名前（-1 が付く）
    [InlineData("# はじめに\n\n## 日本語の見出し\n")]                   // 日本語
    [InlineData("## [x] 完了した節\n\n## [ ] これから\n")]               // 印付き
    [InlineData("> ## 引用の中\n\n- ## リストの中\n")]                   // 引用とリストの中
    [InlineData("Setext 見出し\n=====\n\n小見出し\n-----\n")]            // setext
    public void Ids_match_the_ids_in_the_html(string md)
    {
        var r = Run(md);
        Assert.NotEmpty(r.Headings);
        foreach (var h in r.Headings)
        {
            Assert.False(string.IsNullOrEmpty(h.Id));
            Assert.Matches($@"<h{h.Level}[^>]* id=""{Regex.Escape(h.Id!)}""", r.Html);
        }
    }

    [Fact]
    public void Duplicate_names_get_distinct_ids_in_order()
    {
        var h = Headings("## A\n\n## A\n");
        Assert.Equal(2, h.Count);
        Assert.NotEqual(h[0].Id, h[1].Id);
    }

    [Fact]
    public void Levels_and_order_follow_the_document()
    {
        var h = Headings("# a\n\n### b\n\n## c\n");
        Assert.Equal(new[] { 1, 3, 2 }, h.Select(x => x.Level));
        Assert.Equal(new[] { "a", "b", "c" }, h.Select(x => x.Text));
    }

    // ── 見出しの印 ────────────────────────────────────────────────

    [Theory]
    [InlineData("## [x] 済み", true, "済み")]
    [InlineData("## [X] 済み", true, "済み")]
    [InlineData("## [ ] 未了", false, "未了")]
    [InlineData("## 印なし", null, "印なし")]
    [InlineData("## [x]付き", null, "[x]付き")]                       // 後ろに空白が無いなら印ではない
    public void Mark_in_front_of_the_text_is_read_and_removed(string line, bool? done, string text)
    {
        var h = Assert.Single(Headings(line + "\n"));
        Assert.Equal(done, h.Done);
        Assert.Equal(text, h.Text);
    }

    // ── 文字（記号を外す）────────────────────────────────────────

    [Fact]
    public void Inline_markup_is_stripped_to_plain_text()
    {
        var h = Assert.Single(Headings("## **太字** と `code` と [リンク](a.md) と *強調*\n"));
        Assert.Equal("太字 と code と リンク と 強調", h.Text);
    }

    [Fact]
    public void Whitespace_runs_are_collapsed_and_trimmed()
    {
        var h = Assert.Single(Headings("##   a    b   \n"));
        Assert.Equal("a b", h.Text);
    }

    [Fact]
    public void Headings_with_no_text_are_not_listed()
    {
        Assert.Empty(Headings("## \n\n## [x]\n\n##\n"));
    }

    // ── 節のタスク数 ──────────────────────────────────────────────

    [Fact]
    public void Tasks_are_counted_per_section()
    {
        var h = Headings("## A\n\n- [x] 1\n- [ ] 2\n- [ ] 3\n\n## B\n\n- [x] 4\n");
        Assert.Equal((1, 3), (h[0].TasksDone, h[0].TasksTotal));
        Assert.Equal((1, 1), (h[1].TasksDone, h[1].TasksTotal));
    }

    [Fact]
    public void Tasks_under_a_lower_heading_count_for_the_upper_headings_too()
    {
        var h = Headings("## A\n\n- [x] a1\n\n### A-1\n\n- [x] b1\n- [ ] b2\n\n## B\n\n- [ ] c1\n");
        Assert.Equal((2, 3), (h[0].TasksDone, h[0].TasksTotal));   // A: 自分の 1 + A-1 の 2
        Assert.Equal((1, 2), (h[1].TasksDone, h[1].TasksTotal));   // A-1
        Assert.Equal((0, 1), (h[2].TasksDone, h[2].TasksTotal));   // B
    }

    [Fact]
    public void Nested_and_numbered_task_items_are_counted()
    {
        var h = Headings("## A\n\n- [x] 親\n  - [ ] 子\n    - [x] 孫\n\n1. [ ] 番号付き\n");
        Assert.Equal((2, 4), (h[0].TasksDone, h[0].TasksTotal));
    }

    [Fact]
    public void Task_like_text_in_code_blocks_is_not_counted()
    {
        var h = Headings("## A\n\n```\n- [x] コードの中\n```\n\n    - [ ] 字下げのコード\n\n本文 [x] の途中\n");
        Assert.Equal((0, 0), (h[0].TasksDone, h[0].TasksTotal));
    }

    [Fact]
    public void Tasks_before_the_first_heading_are_not_counted()
    {
        var h = Headings("- [x] 見出しの前\n\n## A\n\n- [ ] 見出しの後\n");
        Assert.Equal((0, 1), (h[0].TasksDone, h[0].TasksTotal));
    }

    [Fact]
    public void Tasks_are_not_counted_for_a_sibling_after_the_section_ends()
    {
        var h = Headings("### A-1\n\n- [x] a\n\n### A-2\n\n- [ ] b\n");
        Assert.Equal((1, 1), (h[0].TasksDone, h[0].TasksTotal));
        Assert.Equal((0, 1), (h[1].TasksDone, h[1].TasksTotal));
    }

    // ── Render との関係 ───────────────────────────────────────────

    [Fact]
    public void Render_and_RenderWithOutline_give_the_same_html()
    {
        const string md = "# 見出し\n\n- [x] a\n- [ ] b\n\n```mermaid\ngraph TD; A-->B\n```\n";
        Assert.Equal(MarkdownRenderer.Render(md, Folder, true, "t"), MarkdownRenderer.RenderWithOutline(md, Folder, true, "t").Html);
    }

    [Fact]
    public void A_document_without_headings_has_an_empty_outline()
    {
        var r = Run("ただの本文\n");
        Assert.Empty(r.Headings);
    }
}
