using System.Security.Cryptography;
using System.Text;
using Miharikun.Docs;

namespace Miharikun.Tests.Docs;

/// <summary>44-1：同梱の mermaid・highlight.js の版・一覧・書き出し（計画 9.5）。</summary>
public sealed class MarkdownAssetsTests : IDisposable
{
    private readonly string _lib = Path.Combine(Path.GetTempPath(), "miharikun-lib-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_lib, true); } catch { }
    }

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void Every_embedded_file_is_present_and_not_empty()
    {
        Assert.Contains("mermaid.min.js", MarkdownAssets.Names);
        Assert.Contains("highlight.min.js", MarkdownAssets.Names);
        Assert.Contains("hljs-github.min.css", MarkdownAssets.Names);
        Assert.Contains("hljs-github-dark.min.css", MarkdownAssets.Names);
        foreach (var name in MarkdownAssets.Names)
            Assert.NotEmpty(MarkdownAssets.Read(name));
    }

    [Fact]
    public void Version_is_made_from_the_hash_of_the_embedded_contents()
    {
        // 名前順に「名前・長さ・中身」を SHA-256 に入れ、v- ＋ 16 進の先頭 12 文字（計画 9.5）
        using var sha = SHA256.Create();
        foreach (var name in MarkdownAssets.Names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var bytes = MarkdownAssets.Read(name);
            var head = Encoding.UTF8.GetBytes(name + "\n" + bytes.Length + "\n");
            sha.TransformBlock(head, 0, head.Length, null, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        var expected = "v-" + Convert.ToHexString(sha.Hash!).ToLowerInvariant()[..12];

        Assert.Equal(expected, MarkdownAssets.Version);
    }

    [Fact]
    public void Version_does_not_contain_the_words_mermaid_or_highlight()
    {
        // 既存のテスト（mermaid・highlight が無いページには、その文字が出ない）が、本物の lib のフォルダの形で通るように
        Assert.DoesNotContain("mermaid", MarkdownAssets.Version, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("highlight", MarkdownAssets.Version, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^v-[0-9a-f]{12}$", MarkdownAssets.Version);
    }

    [Fact]
    public void Ensure_writes_everything_when_nothing_is_there_and_returns_the_version_folder()
    {
        var dir = MarkdownAssets.Ensure(_lib, Write);

        Assert.Equal(Path.Combine(_lib, MarkdownAssets.Version), dir);
        foreach (var name in MarkdownAssets.Names)
            Assert.Equal(MarkdownAssets.Read(name), File.ReadAllBytes(Path.Combine(dir, name)));
    }

    [Fact]
    public void Ensure_does_not_write_again_when_the_length_is_the_same()
    {
        var dir = MarkdownAssets.Ensure(_lib, Write);
        var written = new List<string>();

        MarkdownAssets.Ensure(_lib, (p, b) => { written.Add(p); Write(p, b); });

        Assert.Empty(written);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Ensure_rewrites_a_file_that_is_missing_or_has_a_different_length()
    {
        var dir = MarkdownAssets.Ensure(_lib, Write);
        File.Delete(Path.Combine(dir, "highlight.min.js"));
        File.WriteAllBytes(Path.Combine(dir, "mermaid.min.js"), [1, 2, 3]);   // 途中で壊れたファイル
        var written = new List<string>();

        MarkdownAssets.Ensure(_lib, (p, b) => { written.Add(Path.GetFileName(p)); Write(p, b); });

        Assert.Equal(["highlight.min.js", "mermaid.min.js"], written.Order(StringComparer.Ordinal));
        Assert.Equal(MarkdownAssets.Read("mermaid.min.js"), File.ReadAllBytes(Path.Combine(dir, "mermaid.min.js")));
    }

    [Fact]
    public void Ensure_does_not_throw_when_writing_fails_and_reports_it_to_the_log()
    {
        var log = new List<string>();

        var dir = MarkdownAssets.Ensure(_lib, (_, _) => throw new IOException("disk"), log.Add);
        var dir2 = MarkdownAssets.Ensure(_lib, (_, _) => throw new UnauthorizedAccessException(), log.Add);

        Assert.Equal(Path.Combine(_lib, MarkdownAssets.Version), dir);
        Assert.Equal(dir, dir2);
        Assert.NotEmpty(log);
    }
}
