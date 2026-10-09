using System.Text.RegularExpressions;
using Miharikun.Docs;

namespace Miharikun.Tests.Docs;

public sealed class MarkdownRendererTests
{
    private static readonly string Folder = TestPaths.Abs("work", "proj", "docs");

    // 本物の形：<データ>/preview/lib/<版>（版の名前に mermaid・highlight の文字が入らないことも、これで確かめられる）
    private static readonly string LibFolder = TestPaths.Abs("data", "preview", "lib", MarkdownAssets.Version);

    private static string Html(string md, bool dark = false) => MarkdownRenderer.Render(md, Folder, libFolder: LibFolder, isDark: dark);

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
        Assert.Matches(@"<li class=""task-list-item""><input type=""checkbox"" tabindex=""-1"" /> 未完了</li>", body);
        Assert.Contains("contains-task-list", body);
    }

    [Theory]
    [InlineData("- [x] 完了")]
    [InlineData("- [X] 完了")]                                       // 大文字 X も可
    public void Checked_item_becomes_a_checked_read_only_checkbox(string md) =>
        Assert.Contains(@"<input type=""checkbox"" tabindex=""-1"" checked=""checked"" /> 完了", Body(md + "\n"));

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
    public void Mermaid_blocks_come_out_as_pre_mermaid_and_load_the_bundled_mermaid_as_a_plain_script()
    {
        var html = Html("```mermaid\ngraph TD\n A-->B\n```\n");
        Assert.Contains(@"<pre class=""mermaid"">", html);
        Assert.Contains("A-->B", html);                               // Markdig 標準の出力（mermaid.js は innerHTML を読んで復号する）
        Assert.Contains($@" defer src=""{MarkdownRenderer.FolderUri(LibFolder)}mermaid.min.js""></script>", html);
        Assert.DoesNotContain("type=\"module\"", html);               // ES module の import() はやめた
        Assert.DoesNotContain("import(", html);
    }

    [Fact]
    public void Mermaid_is_initialized_with_the_strict_security_level()
    {
        Assert.Contains("securityLevel: 'strict'", Html("```mermaid\nA-->B\n```\n"));
    }

    [Fact]
    public void The_page_never_refers_to_a_cdn()
    {
        foreach (var html in new[] { Html("```mermaid\nA-->B\n```\n\n```cs\nx\n```\n"), Html("```mermaid\nA-->B\n```\n\n```cs\nx\n```\n", dark: true) })
        {
            Assert.DoesNotContain("cdn.jsdelivr", html);
            Assert.DoesNotContain("cdnjs", html);
            Assert.DoesNotContain("https://", html);
        }
    }

    [Theory]
    [InlineData("日本語 の#フォルダ")]
    [InlineData("a b%c&d")]
    public void The_lib_files_are_referred_to_by_a_percent_encoded_and_html_encoded_file_url(string folderName)
    {
        var lib = TestPaths.Abs("data", folderName, "lib", MarkdownAssets.Version);
        var html = MarkdownRenderer.Render("```mermaid\nA-->B\n```\n\n```cs\nx\n```\n", Folder, libFolder: lib, isDark: false);

        var url = System.Net.WebUtility.HtmlEncode(MarkdownRenderer.FolderUri(lib));
        Assert.Contains($@" defer src=""{url}mermaid.min.js""></script>", html);
        Assert.Contains($@" defer src=""{url}highlight.min.js""></script>", html);
        Assert.Contains($@"<link rel=""stylesheet"" href=""{url}hljs-github.min.css"">", html);
        Assert.DoesNotContain("#", url.Replace("&#", ""));              // # は %23 になっている（フラグメントにならない）
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
    public void Code_blocks_with_a_language_are_highlighted_with_the_bundled_highlightjs()
    {
        var html = Html("```cs\nvar x = 1;\n```\n");
        var lib = MarkdownRenderer.FolderUri(LibFolder);
        Assert.Contains(@"<code class=""language-cs"">", html);
        Assert.Contains($@" defer src=""{lib}highlight.min.js""></script>", html);
        Assert.Contains($@"href=""{lib}hljs-github.min.css""", html);
    }

    [Fact]
    public void Highlightjs_uses_the_dark_stylesheet_in_the_dark_theme() =>
        Assert.Contains("hljs-github-dark.min.css", Html("```cs\nx\n```\n", dark: true));

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
        var html = MarkdownRenderer.Render("本文", Folder, libFolder: LibFolder, isDark: false, title: "要件 & 定義");
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

        var html = MarkdownRenderer.Render(File.ReadAllText(file), Path.GetDirectoryName(file)!, libFolder: LibFolder, isDark: false);
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
        // 本文には Miharikun の script を入れない（本文が見出しの 1 行だけと等しい）
        var body = Body("# 見出し\n");
        Assert.DoesNotContain("<script", body);
        Assert.Equal("<h1 id=\"見出し\">見出し</h1>\n", body.TrimStart());
    }

    // ── チェックボックス：クリックを取り消すスクリプト（CSP で onclick が使えないため。計画 9.6）──

    [Fact]
    public void Checkboxes_have_no_inline_handler_and_a_script_cancels_the_click_on_descendant_checkboxes()
    {
        var html = Html("- [ ] a\n");
        Assert.DoesNotContain("onclick", html);
        // 空行を挟んだリストは <li><p><input> になる。子（>）ではなく子孫のセレクタで当てる
        Assert.Contains("li.task-list-item input[type=\"checkbox\"]", html);
        Assert.DoesNotContain("li.task-list-item > input", html);
        Assert.Contains("e.preventDefault()", html);
    }

    [Fact]
    public void A_task_list_separated_by_a_blank_line_still_gets_checkboxes_inside_the_paragraph()
    {
        var body = Body("- [ ] a\n\n- [x] b\n");

        Assert.Equal(2, Count(body, @"type=""checkbox"""));
        Assert.Contains(@"<li class=""task-list-item""><p><input type=""checkbox"" tabindex=""-1"" /> a</p>", body);
        Assert.Contains(@"checked=""checked"" /> b</p>", body);
    }

    // ── CSP と nonce（計画 9.6）──────────────────────────────────────────

    private static string Nonce(string html) => Regex.Match(html, @"'nonce-([^']+)'").Groups[1].Value;

    [Fact]
    public void The_csp_meta_comes_right_after_charset_and_before_base()
    {
        var html = Html("# a\n");
        var charset = html.IndexOf(@"<meta charset=""utf-8"">", StringComparison.Ordinal);
        var csp = html.IndexOf(@"<meta http-equiv=""Content-Security-Policy""", StringComparison.Ordinal);
        var baseTag = html.IndexOf("<base ", StringComparison.Ordinal);
        var style = html.IndexOf("<style>", StringComparison.Ordinal);

        Assert.True(charset >= 0 && charset < csp && csp < baseTag && baseTag < style, $"{charset} {csp} {baseTag} {style}");
        Assert.Contains($"content=\"script-src 'nonce-{Nonce(html)}'; object-src 'none'; frame-src 'none'; form-action 'none'\"", html);
        Assert.DoesNotContain("style-src", html);   // md の画像・css・mermaid の <style> を今のまま許す
        Assert.DoesNotContain("img-src", html);
    }

    [Fact]
    public void The_nonce_is_the_same_within_a_page_and_different_between_pages()
    {
        const string md = "```mermaid\nA-->B\n```\n\n```cs\nx\n```\n";
        var a = Html(md);
        var b = Html(md);

        Assert.NotEmpty(Nonce(a));
        Assert.True(Regex.Matches(a, @"nonce=""([^""]+)""").All(m => m.Groups[1].Value == Nonce(a)));
        Assert.NotEqual(Nonce(a), Nonce(b));
    }

    [Fact]
    public void Every_script_miharikun_adds_has_the_nonce_and_all_of_them_are_in_the_head()
    {
        var html = Html("```mermaid\nA-->B\n```\n\n```cs\nx\n```\n\n- [ ] a\n");
        var nonce = Nonce(html);

        var scripts = Regex.Matches(html, "<script[^>]*>");
        Assert.Equal(5, scripts.Count);    // 共通 1 つ（# リンク・チェックボックス・Esc・位置の補正）＋ hljs の 2 つ ＋ mermaid の 2 つ
        Assert.All(scripts, m => Assert.Contains($@"nonce=""{nonce}""", m.Value));
        // 本文の後ろには何も置かない（md の閉じていない <script src="… が nonce を属性として読んでしまうのを防ぐ）
        var afterBody = html[html.IndexOf("<body", StringComparison.Ordinal)..];
        Assert.DoesNotContain("<script", afterBody);
        Assert.DoesNotContain("nonce=", afterBody);
    }

    [Fact]
    public void Scripts_and_handlers_written_in_the_md_get_no_nonce()
    {
        var html = Html("<script>document.title='x'</script>\n\n<img src=x onerror=\"document.title='y'\">\n");
        var nonce = Nonce(html);
        var body = html[html.IndexOf("<body", StringComparison.Ordinal)..];

        Assert.Contains("<script>document.title='x'</script>", body);   // 本文はそのまま（nonce が無いので CSP が止める）
        Assert.DoesNotContain(nonce, body);
    }

    [Fact]
    public void Deferred_scripts_wait_for_the_body_with_DOMContentLoaded()
    {
        var html = Html("```mermaid\nA-->B\n```\n\n```cs\nx\n```\n");

        Assert.Matches(@"<script nonce=""[^""]+"" defer src=""[^""]*mermaid\.min\.js""></script>", html);
        Assert.Matches(@"<script nonce=""[^""]+"" defer src=""[^""]*highlight\.min\.js""></script>", html);
        Assert.Equal(3, Count(html, "addEventListener\\('DOMContentLoaded'"));   // hljs・mermaid・位置の補正
    }

    // ── 生の HTML の http-equiv（meta refresh を効かなくする。計画 9.6）──────

    [Theory]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://example.com/\">\n")]
    [InlineData("a <meta HTTP-EQUIV=\"refresh\" content=\"0;url=x.bat\"> b\n")]
    public void Raw_html_http_equiv_is_renamed_so_that_meta_refresh_does_nothing(string md)
    {
        var body = Body(md);

        Assert.DoesNotMatch("(?i)(?<!data-)http-equiv=", body);
        Assert.Contains("data-http-equiv=", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Http_equiv_in_code_blocks_and_text_is_left_alone_and_the_csp_meta_stays()
    {
        var html = Html("```html\n<meta http-equiv=\"refresh\">\n```\n\n`http-equiv` と書く\n");

        Assert.Contains("&lt;meta http-equiv=&quot;refresh&quot;&gt;", html);
        Assert.Contains("<code>http-equiv</code>", html);
        Assert.Contains(@"<meta http-equiv=""Content-Security-Policy""", html);
    }
}
