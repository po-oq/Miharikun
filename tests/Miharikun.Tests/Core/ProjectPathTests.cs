using Miharikun.Core.Projects;

namespace Miharikun.Tests.Core;

public sealed class ProjectPathTests
{
    [Theory]
    [InlineData(@"C:\work\proj", @"C:\work\proj")]
    [InlineData(@"C:\work\proj", @"c:\WORK\Proj")]          // 大文字小文字
    [InlineData(@"C:\work\proj\", @"C:\work\proj")]         // 末尾区切り（起動側）
    [InlineData(@"C:\work\proj", @"C:\work\proj\")]         // 末尾区切り（セッション側）
    [InlineData(@"C:\work\proj", @"C:/work/proj")]          // スラッシュ区切り
    [InlineData(@"C:\work\x\..\proj", @"C:\work\proj")]     // 正規化
    public void Matches(string project, string root) =>
        Assert.True(ProjectPath.Matches(project, [root]));

    [Theory]
    [InlineData(@"C:\work\proj", @"C:\work\proj2")]
    [InlineData(@"C:\work\proj", @"C:\work\proj\sub")]      // 完全一致のみ。サブフォルダは対象外
    [InlineData(@"C:\work\proj", @"C:\work")]
    [InlineData(@"C:\work\proj", "")]
    [InlineData("", @"C:\work\proj")]
    public void Does_not_match(string project, string root) =>
        Assert.False(ProjectPath.Matches(project, [root]));

    [Fact]
    public void Multi_root_matches_when_any_root_matches()
    {
        Assert.True(ProjectPath.Matches(@"C:\b", [@"C:\a", @"C:\B"]));
        Assert.False(ProjectPath.Matches(@"C:\c", [@"C:\a", @"C:\b"]));
        Assert.False(ProjectPath.Matches(@"C:\a", []));
    }

    [Fact]
    public void Invalid_paths_do_not_throw()
    {
        Assert.False(ProjectPath.Matches(@"C:\a", ["C:\\a\0b"]));
        Assert.Null(ProjectPath.Normalize(null));
    }
}