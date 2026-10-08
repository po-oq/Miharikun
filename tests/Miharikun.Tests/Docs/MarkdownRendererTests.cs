using System.Text.RegularExpressions;
using Miharikun.Docs;

namespace Miharikun.Tests.Docs;

public sealed class MarkdownRendererTests
{
    private static readonly string Folder = TestPaths.Abs("work", "proj", "docs");

    private static string Html(string md, bool dark = false) => MarkdownRenderer.Render(md, Folder, dark);

    private static string Body(string md)
    {
        var html = Html(md);
        var start = html.IndexOf("<body", StringComparison.Ordinal);
        var tagEnd = html.IndexOf('>', start) + 1;
        var end = html.IndexOf("</body>", StringComparison.Ordinal);
        return html[tagEnd..end];
    }

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    // ── チェックボックス（最重要。Markdig の出力の確認テスト）────────────────

    [Fact]
    public void Unchecked_item_becomes_an_empty_read_only_checkbox()
    {
        var body = Body("- [ ] 未完了\n");
        Assert.Matches(@"<li class=""task-list-item""><input type=""checkbox"" onclick=""return false"" tabindex=""-1"" /> 未完了</li>", body);
        Assert.Contains("contains-task-list", body);
    }

    [Theory]
    [InlineData("- [x] 完了")]
    [InlineData("- [X] 完了")]                                       // 大文字 X も可
    public void Checked_item_becomes_a_checked_read_only_checkbox(string md) =>
        Assert.Contains(@"<input type=""checkbox"" onclick=""return false"" tabindex=""-1"" checked=""checked"" /> 完了", Body(md + "\n"));

    [Fact]
    public void Nested_items_are_checkboxes_too()
    {
        var body = Body("- [x] 親\n  - [ ] 子\n  - [x] 孫\n");
        Assert.Equal(3, Count(body, @"type=""checkbox"""));
        Assert.Equal(2, Count(body, @"checked=""checked"""));
        Assert.Equal(2, Count(body, "<ul"));                          // 入れ子のまま
    }

    [Fact]
    public void Numbered_lists_can_hold_checkboxes()
    {
        var body = Body("1. [x] 番号付き\n2. [ ] 二つ目\n");
        Assert.Contains("<ol", body);
        Assert.Equal(2, Count(body, @"type=""checkbox"""));
    }

    [Fact]
    public void Checkboxes_inside_code_blocks_are_left_as_text()
    {
        var body = Body("```\n- [ ] コード内\n```\n\n    - [x] インデントのコード\n");
        Assert.DoesNotContain(@"type=""checkbox""", body);
        Assert.Contains("- [ ] コード内", body);
    }

    [Fact]
    public void A_bracket_x_in_a_heading_stays_text()
    {
        var body = Body("## [x] Phase 1\n");
        Assert.DoesNotContain(@"type=""checkbox""", body);
        Assert.Contains("[x] Phase 1", body);
    }

    [Fact]
    public void Ordinary_items_stay_ordinary_next_to_task_items()
    {
        var body = Body("- [ ] タスク\n- 通常\n");
        Assert.Equal(1, Count(body, @"type=""checkbox"""));
        Assert.Contains("<li>通常</li>", body);
    }

    [Fact]
    public void Task_list_dots_are_hidden_by_the_stylesheet()
    {
        var html = Html("- [ ] a\n");
        Assert.Contains("list-style: none", html);
        Assert.Contains(".task-list-item", html);
    }

    // ── 見出し id と #リンク ────────────────────────────────────────────

    [Fact]
    public void Japanese_headings_keep_their_text_as_the_id_and_duplicates_are_numbered()
    {
        var body = Body("# 概要\n\n[目次](#概要)\n\n# 概要\n");
        Assert.Contains(@"<h1 id=""概要"">概要</h1>", body);
        Assert.Contains(@"<h1 id=""概要-1"">概要</h1>", body);
    }

    [Fact]
    public void English_headings_get_github_style_ids() =>
        Assert.Contains(@"<h2 id=""phase-13-core-ab"">", Body("## Phase 13: Core (A/B)\n"));

    [Fact]
    public void The_page_intercepts_hash_links_and_scrolls_to_the_decoded_id()
    {
        var html = Html("# a\n");
        Assert.Contains(@"a[href^=""#""]", html);
        Assert.Contains("decodeURIComponent", html);
        Assert.Contains("scrollIntoView", html);
    }

    // ── <base>（相対パスの基準）─────────────────────────────────────────

    [WindowsFact]
    public void Base_points_at_the_source_folder_with_a_trailing_slash() =>
        Assert.Contains(@"<base href=""file:///C:/work/proj/docs/"">", Html("x"));

    [MacFact]
    public void Mac_base_points_at_the_source_folder_with_a_trailing_slash() =>
        Assert.Contains(@"<base href=""file:///work/proj/docs/"">", Html("x"));

    [MacTheory]
    [InlineData("/work/日本語 #a%b", "file:///work/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23a%25b/")]
    [InlineData("/Users/x/Library/Application Support/a b/", "file:///Users/x/Library/Application%20Support/a%20b/")]
    [InlineData("/work/proj/", "file:///work/proj/")]
    public void Mac_FolderUri_escapes_special_characters(string folder, string expected) =>
        Assert.Equal(expected, MarkdownRenderer.FolderUri(folder));

    [MacFact]
    public void Mac_FileUri_escapes_a_hash_in_a_folder_name() =>
        Assert.Equal("file:///work/a%23b/c.md", MarkdownRenderer.FileUri("/work/a#b/c.md"));

    [WindowsTheory]
    [InlineData(@"C:\work\日本語 #a%b", "file:///C:/work/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23a%25b/")]
    [InlineData(@"C:\work\a b\", "file:///C:/work/a%20b/")]
    [InlineData(@"C:\work\proj\", "file:///C:/work/proj/")]
    public void FolderUri_escapes_special_characters(string folder, string expected) =>
        Assert.Equal(expected, MarkdownRenderer.FolderUri(folder));

    [WindowsFact]
    public void FileUri_escapes_a_hash_in_a_folder_name() =>
        Assert.Equal("file:///C:/work/a%23b/c.md", MarkdownRenderer.FileUri(@"C:\work\a#b\c.md"));

    // ── mermaid・色付け・テーマ ───────────────────────────────────────────

    [Fact]
    public void Mermaid_blocks_come_out_as_pre_mermaid_and_load_mermaid_from_the_cdn()
    {
        var html = Html("```mermaid\ngraph TD\n A-->B\n```\n");
        Assert.Contains(@"<pre class=""mermaid"">", html);
        Assert.Contains("A-->B", html);                               // Markdig 標準の出力（mermaid.js は innerHTML を読んで復号する）
        Assert.Contains("cdn.jsdelivr.net/npm/mermaid@11", html);
    }

    [Fact]
    public void Mermaid_gets_the_theme_that_matches_the_page()
    {
        const string md = "```mermaid\nA-->B\n```\n";
        Assert.Contains("theme: 'default'", Html(md, dark: false));
        Assert.Contains("theme: 'dark'", Html(md, dark: true));
    }

    [Fact]
    public void Mermaid_is_not_loaded_when_there_is_no_diagram()
    {
        var html = Html("# 図なし\n\n```cs\nvar x = 1;\n```\n");
        Assert.DoesNotContain("mermaid", html);
    }

    [Fact]
    public void Code_blocks_with_a_language_are_highlighted_with_highlightjs_from_the_cdn()
    {
        var html = Html("```cs\nvar x = 1;\n```\n");
        Assert.Contains(@"<code class=""language-cs"">", html);
        Assert.Contains("highlight.js", html);
        Assert.Contains("highlight.min.js", html);
        Assert.Contains("github.min.css", html);
    }

    [Fact]
    public void Highlightjs_uses_the_dark_stylesheet_in_the_dark_theme() =>
        Assert.Contains("github-dark.min.css", Html("```cs\nx\n```\n", dark: true));

    [Fact]
    public void Highlightjs_is_not_loaded_for_plain_pages_or_mermaid_only_pages()
    {
        Assert.DoesNotContain("highlight", Html("# 本文だけ\n"));
        Assert.DoesNotContain("highlight", Html("```mermaid\nA-->B\n```\n"));
    }

    [Fact]
    public void Light_and_dark_use_different_stylesheets()
    {
        var light = Html("x", dark: false);
        var dark = Html("x", dark: true);
        Assert.Contains("color-scheme: light", light);
        Assert.Contains("color-scheme: dark", dark);
        Assert.DoesNotContain("color-scheme: dark", light);
    }

    // ── そのほか ────────────────────────────────────────────────────────

    [Fact]
    public void Output_is_a_complete_utf8_document_with_the_given_title()
    {
        var html = MarkdownRenderer.Render("本文", Folder, false, title: "要件 & 定義");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains(@"<meta charset=""utf-8"">", html);
        Assert.Contains("<title>要件 &amp; 定義</title>", html);
    }

    [Fact]
    public void Tables_and_relative_images_come_through()
    {
        var body = Body("| a | b |\n|---|---|\n| 1 | 2 |\n\n![図](img/a.png)\n");
        Assert.Contains("<table>", body);
        Assert.Contains(@"src=""img/a.png""", body);                  // <base> が解決するので書き換えない
    }

    [Fact]
    public void The_requirements_document_itself_renders()
    {
        var dir = AppContext.BaseDirectory;
        string? file = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "docs", "miharikun-requirements.md");
            if (File.Exists(candidate))
            {
                file = candidate;
                break;
            }
        }
        if (file is null)
            return;                                                  // リポジトリ外で実行されたときは飛ばす

        var html = MarkdownRenderer.Render(File.ReadAllText(file), Path.GetDirectoryName(file)!, false);
        Assert.Contains("<h1", html);
        Assert.Contains(@"type=""checkbox""", html);                  // 要件定義のチェックリスト
        Assert.Contains("<table>", html);
    }

    // ── ページの JS（Esc の受け口・位置の補正。Issue #28）────────────────

    [Fact]
    public void Page_has_the_escape_listener_and_the_scroll_keeper_once_each()
    {
        var html = Html("# 見出し\n");
        Assert.Equal(1, Count(html, Regex.Escape("window.__miharikunEsc = true")));
        Assert.Equal(1, Count(html, Regex.Escape("invokeCSharpAction('key:Escape')")));
        Assert.Equal(1, Count(html, Regex.Escape("window.scrollBy(0, anchor.getBoundingClientRect().top - anchorTop)")));
    }

    [Fact]
    public void Escape_listener_script_is_exposed_without_script_tags()
    {
        Assert.DoesNotContain("<script", MarkdownRenderer.EscapeListenerScript);
        Assert.Contains("window.__miharikunEsc", MarkdownRenderer.EscapeListenerScript);
        Assert.Contains("invokeCSharpAction('key:Escape')", MarkdownRenderer.EscapeListenerScript);
    }

    [Fact]
    public void Body_is_not_changed_by_the_added_scripts()
    {
        Assert.Equal("<h1 id=\"見出し\">見出し</h1>\n", Body("# 見出し\n").Split("<script>")[0].TrimStart());
    }
}
