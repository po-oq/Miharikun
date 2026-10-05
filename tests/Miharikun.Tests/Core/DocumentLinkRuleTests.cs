using Miharikun.Core.Documents;

namespace Miharikun.Tests.Core;

public sealed class DocumentLinkRuleTests
{
    private const string Root = @"C:\work\proj";

    private static readonly GitIgnoreMatcher Matcher = GitIgnoreMatcher.Parse("node_modules/\n*.secret.md\nbuild/\n");

    private static bool Check(string fullPath, out string rel, GitIgnoreMatcher? matcher = null) =>
        DocumentLinkRule.TryGetInAppPath(Root, fullPath, matcher ?? Matcher, out rel);

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
        Assert.True(DocumentLinkRule.TryGetInAppPath(Root + @"\", @"C:\work\proj\a.md", Matcher, out var rel));
        Assert.Equal("a.md", rel);
    }

    [Theory]
    [InlineData(@"C:\work\other\a.md")]
    [InlineData(@"C:\work\a.md")]
    [InlineData(@"C:\work\proj\..\a.md")]
    [InlineData(@"C:\work\proj2\a.md")]
    [InlineData(@"D:\work\proj\a.md")]
    public void Documents_outside_the_root_are_not(string full)
    {
        Assert.False(Check(full, out var rel));
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
    public void The_root_itself_is_not()
    {
        Assert.False(Check(Root, out _));
    }
}
