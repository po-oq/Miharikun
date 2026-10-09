using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Reflection;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Miharikun.Docs;

/// <summary>
/// md → 完全な HTML 文書（要件 12.7）。相対パスの画像・リンクは <c>&lt;base&gt;</c> で元のフォルダ基準にする。
/// タスクリスト（- [ ] / - [x]）は Markdig 標準でチェックボックスになる。mermaid と色付けは同梱のファイル（<see cref="MarkdownAssets"/>。
/// ネットが無くても出る）を <c>libFolder</c> から読む。
/// </summary>
public static class MarkdownRenderer
{
    // 見出し id は日本語を残す GitHub 方式。UseAdvancedExtensions() の既定は ASCII だけで、日本語見出しが id="section" になり
    // #リンクが一致しない。UseAutoIdentifiers は UseAdvancedExtensions() より前に呼ぶ（後だと既定のまま）。
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .Build();

    private static readonly Lazy<string> BaseCss = new(() => Resource("markdown-base.css"));
    private static readonly Lazy<string> LightCss = new(() => Resource("markdown-light.css"));
    private static readonly Lazy<string> DarkCss = new(() => Resource("markdown-dark.css"));

    /// <summary>
    /// Esc の受け口（<c>&lt;script&gt;</c> なしの本文）。Esc が押されたら C# へ <c>key:Escape</c> を知らせる。2 回入っても 1 回だけ効く。
    /// html のプレビューにも読み込みの後で入れる。ページが自分で Esc を使った（defaultPrevented）ときは知らせない。
    /// </summary>
    public const string EscapeListenerScript = """
        (function () {
          if (window.__miharikunEsc) return;
          window.__miharikunEsc = true;
          document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !e.defaultPrevented && typeof invokeCSharpAction === 'function')
              invokeCSharpAction('key:Escape');
          });
        })();
        """;

    // 幅が変わっても（拡大⇄戻す・区切り線のドラッグ）、見ていた所を保つ。md のページだけ。
    private const string ScrollKeeperScript = """
        (function () {
          var anchor = null, anchorTop = 0, adjusting = false;
          function remember() {
            var els = document.querySelectorAll('h1,h2,h3,h4,h5,h6,p,li,pre,table,blockquote');
            for (var i = 0; i < els.length; i++) {
              var t = els[i].getBoundingClientRect().top;
              if (t >= 0) { anchor = els[i]; anchorTop = t; return; }
            }
          }
          window.addEventListener('scroll', function () { if (!adjusting) remember(); }, { passive: true });
          window.addEventListener('resize', function () {
            if (!anchor) return;
            adjusting = true;
            window.scrollBy(0, anchor.getBoundingClientRect().top - anchorTop);
            requestAnimationFrame(function () { adjusting = false; });
          });
          document.addEventListener('DOMContentLoaded', remember);
        })();
        """;

    /// <param name="baseFolder">md のあるフォルダ（相対パスの基準）。</param>
    /// <param name="libFolder">同梱の mermaid・highlight.js を書き出したフォルダ（<see cref="MarkdownAssets.Ensure"/> が返すもの）。<c>&lt;base&gt;</c> があるので、絶対の file URL で読む。</param>
    /// <param name="isDark">ライト/ダーク。mermaid・コードの色付けにも渡す。</param>
    public static string Render(string markdown, string baseFolder, string libFolder, bool isDark, string? title = null) =>
        RenderWithOutline(markdown, baseFolder, libFolder, isDark, title).Html;

    /// <summary>md を 1 回だけ解析して、HTML と見出しの一覧を返す（目次の id と本文の id を食い違わせないため）。</summary>
    public static MarkdownRendering RenderWithOutline(string markdown, string baseFolder, string libFolder, bool isDark, string? title = null)
    {
        var lib = WebUtility.HtmlEncode(FolderUri(libFolder));
        var doc = Markdown.Parse(markdown, Pipeline);
        NeutralizeHttpEquiv(doc);
        var body = ReadOnlyCheckboxes(doc.ToHtml(Pipeline));
        var hasMermaid = body.Contains(@"<pre class=""mermaid"">", StringComparison.Ordinal);
        var hasCode = body.Contains(@"<code class=""language-", StringComparison.Ordinal);

        // ページ（1 回の描画）ごとの nonce。Miharikun が入れる script にだけ付ける。md に書かれた script・属性の script には付かないので、CSP が止める。
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var head = new System.Text.StringBuilder();
        head.Append("<!doctype html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        // <base> より前（md の本文が属性を横取りできない位置）。style-src・img-src は付けない（md の画像・css・mermaid の <style> を許す）。
        head.Append($"<meta http-equiv=\"Content-Security-Policy\" content=\"script-src 'nonce-{nonce}'; object-src 'none'; frame-src 'none'; form-action 'none'\">\n");
        head.Append($"<base href=\"{WebUtility.HtmlEncode(FolderUri(baseFolder))}\">\n");
        head.Append($"<title>{WebUtility.HtmlEncode(title ?? "")}</title>\n");
        head.Append("<style>\n").Append(BaseCss.Value).Append(isDark ? DarkCss.Value : LightCss.Value);
        if (hasMermaid)
            head.Append("pre.mermaid { background: transparent; text-align: center; }\n");
        head.Append("</style>\n");
        if (hasCode)
            head.Append($"<link rel=\"stylesheet\" href=\"{lib}{(isDark ? MarkdownAssets.HighlightDarkCss : MarkdownAssets.HighlightLightCss)}\">\n");

        // Miharikun の script は全部 <head> に置き、本文の後ろには何も置かない。Markdig は閉じていない生の HTML をそのまま通すので、
        // 本文の後ろに <script nonce="…"> があると、md の最後の <script src="…（> なし）がその nonce を属性として読んでしまう。
        // 本文を待つものは DOMContentLoaded の中で動かす（# リンク・チェックボックスは document へのクリックの受け口なので head のままで動く）。
        head.Append($"<script nonce=\"{nonce}\">\n");
        // base があるので「#見出し」は元フォルダの URL になってしまう。ページ内のスクロールに置き換える。
        head.Append("""
            document.addEventListener('click', function (e) {
              var a = e.target.closest('a[href^="#"]');
              if (!a) return;
              e.preventDefault();
              var el = document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1)));
              if (el) el.scrollIntoView();
            });
            // チェックボックスは押しても変わらない（md は書き換えない）。属性に書くハンドラは CSP で使えないので、クリックを取り消す。
            // 空行を挟んだリストは <li><p><input> になるので、子ではなく子孫のセレクタで当てる。Space キーの押下も click になるので同じく取り消される。
            document.addEventListener('click', function (e) {
              var t = e.target;
              if (t && t.matches && t.matches('li.task-list-item input[type="checkbox"]')) e.preventDefault();
            }, true);

            """);
        head.Append(EscapeListenerScript).Append('\n').Append(ScrollKeeperScript).Append("\n</script>\n");
        if (hasCode)
            head.Append($$"""
                <script nonce="{{nonce}}" defer src="{{lib}}{{MarkdownAssets.HighlightFile}}"></script>
                <script nonce="{{nonce}}">
                document.addEventListener('DOMContentLoaded', function () {
                  if (window.hljs) document.querySelectorAll('pre code[class^="language-"]').forEach(function (el) { hljs.highlightElement(el); });
                });
                </script>

                """);
        if (hasMermaid)
            head.Append($$"""
                <script nonce="{{nonce}}" defer src="{{lib}}{{MarkdownAssets.MermaidFile}}"></script>
                <script nonce="{{nonce}}">
                document.addEventListener('DOMContentLoaded', function () {
                  try {
                    if (window.mermaid) {
                      mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: '{{(isDark ? "dark" : "default")}}' });
                      mermaid.run({ querySelector: 'pre.mermaid' }).catch(function () { });
                    }
                  } catch (e) { }
                });
                </script>

                """);
        head.Append("</head>\n<body>\n");

        return new MarkdownRendering(head + body + "</body>\n</html>\n", MarkdownOutline.Extract(doc));
    }

    /// <summary>
    /// タスクリストのチェックボックスを、読み取り専用にする。Markdig は disabled を付けるが、ブラウザは灰色の薄い表示にして読みにくい。
    /// 代わりに、押しても状態が変わらないようにして（md は書き換えない）、通常の色で表示する。
    /// </summary>
    private static string ReadOnlyCheckboxes(string html) =>
        html.Replace(@"<input disabled=""disabled"" type=""checkbox""", @"<input type=""checkbox"" tabindex=""-1""", StringComparison.Ordinal);

    /// <summary>
    /// 生の HTML（<see cref="HtmlBlock"/>・<see cref="HtmlInline"/>）の <c>http-equiv</c> を <c>data-http-equiv</c> にする（大文字小文字は区別しない）。
    /// md に書かれた <c>&lt;meta http-equiv="refresh" content="0;url=…"&gt;</c> は CSP では止まらず、読み込みの後にブラウザ・既定のアプリ・エクスプローラーが
    /// 勝手に開くので、効かなくする（md で使う正当な理由は無い）。コードブロック・本文の文字は変えない。
    /// </summary>
    private static void NeutralizeHttpEquiv(MarkdownDocument doc)
    {
        foreach (var block in doc.Descendants<HtmlBlock>())
        {
            var lines = block.Lines.Lines;
            for (var i = 0; i < block.Lines.Count; i++)
            {
                var text = lines[i].ToString();
                if (HttpEquiv.IsMatch(text))
                    lines[i].Slice = new StringSlice(HttpEquiv.Replace(text, "data-http-equiv"));
            }
        }
        foreach (var inline in doc.Descendants<HtmlInline>())
        {
            if (HttpEquiv.IsMatch(inline.Tag))
                inline.Tag = HttpEquiv.Replace(inline.Tag, "data-http-equiv");
        }
    }

    private static readonly Regex HttpEquiv = new("http-equiv", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
