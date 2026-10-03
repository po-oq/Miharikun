using System.Text;
using Miharikun.Core.Documents;

namespace Miharikun.Tests.Core;

public sealed class DocumentOverviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-ov-" + Guid.NewGuid().ToString("N"));

    public DocumentOverviewTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private DocumentOverview Read(string name, string content, Encoding? encoding = null)
    {
        File.WriteAllBytes(Path.Combine(_dir, name), (encoding ?? new UTF8Encoding(false)).GetBytes(content));
        return DocumentOverview.Read(_dir, name);
    }

    [Fact]
    public void Markdown_title_is_the_first_heading_of_any_level()
    {
        Assert.Equal("docs 索引", Read("a.md", "前置き\n\n# docs 索引\n\n## 次\n").Title);
        Assert.Equal("小見出し", Read("b.md", "text\n### 小見出し ###\n# 後\n").Title);
    }

    [Fact]
    public void Headings_inside_code_fences_are_ignored()
    {
        var o = Read("a.md", "```\n# not a title\n```\n\n~~~sh\n# nor this\n~~~\n# Real\n");
        Assert.Equal("Real", o.Title);
    }

    [Fact]
    public void Markdown_without_heading_falls_back_to_the_file_name()
    {
        Assert.Equal("plain.md", Read("plain.md", "just text\n#hashtag is not a heading\n").Title);
        Assert.Equal("empty.md", Read("empty.md", "").Title);
    }

    [Fact]
    public void Html_title_comes_from_the_title_tag_with_entities_decoded_and_whitespace_collapsed()
    {
        var o = Read("p.html", "<html><HEAD><TITLE>\n  設計 &amp; 計画\n  メモ </TITLE></HEAD><body>x</body></html>");
        Assert.Equal("設計 & 計画 メモ", o.Title);
    }

    [Fact]
    public void Html_without_title_falls_back_to_the_file_name()
    {
        Assert.Equal("p.htm", Read("p.htm", "<html><body>x</body></html>").Title);
        Assert.Equal("q.html", Read("q.html", "<title>   </title>").Title);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("a\n", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("a\n\n", 2)]
    public void Line_count(string content, int expected) =>
        Assert.Equal(expected, Read("l.md", content).LineCount);

    [Fact]
    public void Carries_path_size_and_times()
    {
        var o = Read("sub.md", "hello");
        Assert.Equal("sub.md", o.RelativePath);
        Assert.Equal(5, o.Size);
        Assert.True(DateTime.UtcNow - o.ModifiedUtc < TimeSpan.FromMinutes(5));
        Assert.True(DateTime.UtcNow - o.CreatedUtc < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void A_utf8_bom_does_not_break_the_title()
    {
        var o = Read("bom.md", "# タイトル\n", new UTF8Encoding(true));
        Assert.Equal("タイトル", o.Title);
    }

    [Fact]
    public void Undecodable_text_falls_back_to_the_file_name_but_still_counts_lines()
    {
        var sjis = CodePagesEncoding();
        var o = Read("old.html", "<title>日本語のタイトル</title>\n<p>本文</p>\n", sjis);
        Assert.Equal("old.html", o.Title);
        Assert.Equal(2, o.LineCount);
    }

    [Fact]
    public void Only_the_head_of_the_file_is_scanned_for_the_title()
    {
        var o = Read("big.md", new string('x', 100_000) + "\n# Late heading\n");
        Assert.Equal("big.md", o.Title);
        Assert.Equal(2, o.LineCount);                                   // 行数は全文
    }

    [Fact]
    public void A_multibyte_character_cut_at_the_scan_limit_does_not_break_the_title()
    {
        var o = Read("cut.md", "# 見出し\n" + new string('あ', 40_000));
        Assert.Equal("見出し", o.Title);
    }

    private static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}
