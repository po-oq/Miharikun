using System.Net;
using System.Reflection;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace Miharikun.Docs;

/// <summary>
/// md → 完全な HTML 文書（要件 12.7）。相対パスの画像・リンクは <c>&lt;base&gt;</c> で元のフォルダ基準にする。
/// タスクリスト（- [ ] / - [x]）は Markdig 標準でチェックボックスになる。mermaid と色付けは CDN（繋がらないときはコードのまま）。
/// </summary>
public static class MarkdownRenderer
{
    private const string MermaidModule = "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
    private const string HighlightBase = "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0";

    // 見出し id は日本語を残す GitHub 方式。UseAdvancedExtensions() の既定は ASCII だけで、日本語見出しが id="section" になり
    // #リンクが一致しない。UseAutoIdentifiers は UseAdvancedExtensions() より前に呼ぶ（後だと既定のまま）。
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .Build();

    private static readonly Lazy<string> BaseCss = new(() => Resource("markdown-base.css"));
    private static readonly Lazy<string> LightCss = new(() => Resource("markdown-light.css"));
    private static readonly Lazy<string> DarkCss = new(() => Resource("markdown-dark.css"));

    /// <param name="baseFolder">md のあるフォルダ（相対パスの基準）。</param>
    /// <param name="isDark">ライト/ダーク。mermaid・コードの色付けにも渡す。</param>
    public static string Render(string markdown, string baseFolder, bool isDark, string? title = null)
    {
        var body = ReadOnlyCheckboxes(Markdown.ToHtml(markdown, Pipeline));
        var hasMermaid = body.Contains(@"<pre class=""mermaid"">", StringComparison.Ordinal);
        var hasCode = body.Contains(@"<code class=""language-", StringComparison.Ordinal);

        var head = new System.Text.StringBuilder();
        head.Append("<!doctype html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        head.Append($"<base href=\"{WebUtility.HtmlEncode(FolderUri(baseFolder))}\">\n");
        head.Append($"<title>{WebUtility.HtmlEncode(title ?? "")}</title>\n");
        head.Append("<style>\n").Append(BaseCss.Value).Append(isDark ? DarkCss.Value : LightCss.Value);
        if (hasMermaid)
            head.Append("pre.mermaid { background: transparent; text-align: center; }\n");
        head.Append("</style>\n");
        if (hasCode)
            head.Append($"<link rel=\"stylesheet\" href=\"{HighlightBase}/styles/{(isDark ? "github-dark" : "github")}.min.css\">\n");
        head.Append("</head>\n<body>\n");

        var foot = new System.Text.StringBuilder();
        // base があるので「#見出し」は元フォルダの URL になってしまう。ページ内のスクロールに置き換える。
        foot.Append("""
            <script>
            document.addEventListener('click', function (e) {
              var a = e.target.closest('a[href^="#"]');
              if (!a) return;
              e.preventDefault();
              var el = document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1)));
              if (el) el.scrollIntoView();
            });
            </script>

            """);
        if (hasCode)
            foot.Append($$"""
                <script src="{{HighlightBase}}/highlight.min.js"></script>
                <script>
                if (window.hljs) document.querySelectorAll('pre code[class^="language-"]').forEach(function (el) { hljs.highlightElement(el); });
                </script>

                """);
        if (hasMermaid)
            foot.Append($$"""
                <script type="module">
                try {
                  const { default: mermaid } = await import('{{MermaidModule}}');
                  mermaid.initialize({ startOnLoad: false, theme: '{{(isDark ? "dark" : "default")}}' });
                  await mermaid.run({ querySelector: 'pre.mermaid' });
                } catch (e) { }
                </script>

                """);
        foot.Append("</body>\n</html>\n");

        return head + body + foot;
    }

    /// <summary>
    /// タスクリストのチェックボックスを、読み取り専用にする。Markdig は disabled を付けるが、ブラウザは灰色の薄い表示にして読みにくい。
    /// 代わりに、押しても状態が変わらないようにして（md は書き換えない）、通常の色で表示する。
    /// </summary>
    private static string ReadOnlyCheckboxes(string html) =>
        html.Replace(@"<input disabled=""disabled"" type=""checkbox""", @"<input type=""checkbox"" onclick=""return false"" tabindex=""-1""", StringComparison.Ordinal);

    /// <summary>フォルダの URL（末尾 / つき。日本語・空白・# ・% は %エンコードされる）。&lt;base&gt; に使う。</summary>
    public static string FolderUri(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (!Path.EndsInDirectorySeparator(full))
            full += Path.DirectorySeparatorChar;
        return new Uri(full).AbsoluteUri;
    }

    /// <summary>ファイルの URL（html を file:/// で開くときに使う）。</summary>
    public static string FileUri(string file) => new Uri(Path.GetFullPath(file)).AbsoluteUri;

    private static string Resource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Miharikun.Docs.Assets.{name}")
            ?? throw new InvalidOperationException($"埋め込みリソース {name} が無い");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
