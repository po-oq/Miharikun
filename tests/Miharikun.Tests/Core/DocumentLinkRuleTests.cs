using Miharikun.Core.Documents;

namespace Miharikun.Tests.Core;

public sealed class DocumentLinkRuleTests
{
    private static readonly string Root = TestPaths.Abs("work", "proj");

    /// <summary>Windows の形のパス（<c>C:\work\…</c>）を、この OS の形にする（mac は <c>/work/…</c>）。</summary>
    private static string Os(string windowsPath) =>
        OperatingSystem.IsWindows() ? windowsPath : windowsPath[2..].Replace('\\', '/');

    private static readonly GitIgnoreMatcher Matcher = GitIgnoreMatcher.Parse("node_modules/\n*.secret.md\nbuild/\n");

    private static bool Check(string fullPath, out string rel, GitIgnoreMatcher? matcher = null) =>
        DocumentLinkRule.TryGetInAppPath(Root, Os(fullPath), matcher ?? Matcher, out rel);

    [Theory]
    [InlineData(@"C:\work\proj\README.md", "README.md")]
    [InlineData(@"C:\work\proj\docs\a.md", "docs/a.md")]
    [InlineData(@"C:\work\proj\docs\a.HTML", "docs/a.HTML")]
    [InlineData(@"C:\work\proj\docs\a.htm", "docs/a.htm")]
    [InlineData(@"C:\work\proj\Docs\A.MD", "Docs/A.MD")]
    public void Documents_inside_the_root_are_opened_in_the_app_with_slash_separators(string full, string expected)
    {
        Assert.True(Check(full, out var rel));
        Assert.Equal(expected, rel);
    }

    [Fact]
    public void Root_may_have_a_trailing_separator()
    {
        Assert.True(DocumentLinkRule.TryGetInAppPath(Root + Path.DirectorySeparatorChar, Os(@"C:\work\proj\a.md"), Matcher, out var rel));
        Assert.Equal("a.md", rel);
    }

    [Theory]
    [InlineData(@"C:\work\other\a.md")]
    [InlineData(@"C:\work\a.md")]
    [InlineData(@"C:\work\proj\..\a.md")]
    [InlineData(@"C:\work\proj2\a.md")]
    public void Documents_outside_the_root_are_not(string full)
    {
        Assert.False(Check(full, out var rel));
        Assert.Equal("", rel);
    }

    [WindowsFact]
    public void A_document_on_another_drive_is_not_inside()
    {
        Assert.False(Check(@"D:\work\proj\a.md", out var rel));
        Assert.Equal("", rel);
    }

    [Fact]
    public void Name_starting_with_two_dots_is_inside()
    {
        Assert.True(Check(@"C:\work\proj\..memo.md", out var rel));
        Assert.Equal("..memo.md", rel);
        Assert.True(Check(@"C:\work\proj\docs\..x.md", out rel));
        Assert.Equal("docs/..x.md", rel);
    }

    [Theory]
    [InlineData(@"C:\work\proj\node_modules\x\a.md")]
    [InlineData(@"C:\work\proj\build\a.html")]
    [InlineData(@"C:\work\proj\docs\x.secret.md")]
    public void Excluded_files_and_files_under_excluded_folders_are_not(string full)
    {
        Assert.False(Check(full, out _));
    }

    [Theory]
    [InlineData(@"C:\work\proj\a.txt")]
    [InlineData(@"C:\work\proj\a.png")]
    [InlineData(@"C:\work\proj\docs")]
    [InlineData(@"C:\work\proj\a")]
    public void Other_files_and_folders_are_not(string full)
    {
        Assert.False(Check(full, out _));
    }

    [Fact]
    public void A_link_written_in_NFC_to_a_file_with_an_NFD_name_gives_the_NFC_relative_path()
    {
        var nfd = "がっこう.md".Normalize(System.Text.NormalizationForm.FormD);

        Assert.True(DocumentLinkRule.TryGetInAppPath(Root, Path.Combine(Root, nfd), Matcher, out var rel));
        Assert.Equal("がっこう.md", rel);   // 索引（NFC で比べる）と同じ形
    }

    [Fact]
    public void The_root_itself_is_not()
    {
        Assert.False(DocumentLinkRule.TryGetInAppPath(Root, Root, Matcher, out _));
    }
}
