using Miharikun.Core.Projects;

namespace Miharikun.Tests.Core;

public sealed class ProjectPathTests
{
    [WindowsTheory]
    [InlineData(@"C:\work\proj", @"C:\work\proj")]
    [InlineData(@"C:\work\proj", @"c:\WORK\Proj")]          // 大文字小文字
    [InlineData(@"C:\work\proj\", @"C:\work\proj")]         // 末尾区切り（起動側）
    [InlineData(@"C:\work\proj", @"C:\work\proj\")]         // 末尾区切り（セッション側）
    [InlineData(@"C:\work\proj", @"C:/work/proj")]          // スラッシュ区切り
    [InlineData(@"C:\work\x\..\proj", @"C:\work\proj")]     // 正規化
    [InlineData(@"C:\work\proj", "/c:/work/proj")]          // 実機：Cursor は workspace_roots を /c:/... の形で渡す
    [InlineData(@"c:\zDev\repo\Miharikun", "/c:/zDev/repo/Miharikun")]
    [InlineData(@"C:\work\proj", @"\C:\work\proj")]
    public void Matches(string project, string root) =>
        Assert.True(ProjectPath.Matches(project, [root]));

    [WindowsTheory]
    [InlineData(@"C:\work\proj", @"C:\work\proj2")]
    [InlineData(@"C:\work\proj", @"C:\work\proj\sub")]      // 完全一致のみ。サブフォルダは対象外
    [InlineData(@"C:\work\proj", @"C:\work")]
    [InlineData(@"C:\work\proj", "")]
    [InlineData("", @"C:\work\proj")]
    public void Does_not_match(string project, string root) =>
        Assert.False(ProjectPath.Matches(project, [root]));

    [WindowsFact]
    public void Multi_root_matches_when_any_root_matches()
    {
        Assert.True(ProjectPath.Matches(@"C:\b", [@"C:\a", @"C:\B"]));
        Assert.False(ProjectPath.Matches(@"C:\c", [@"C:\a", @"C:\b"]));
        Assert.False(ProjectPath.Matches(@"C:\a", []));
    }

    [WindowsFact]
    public void Invalid_paths_do_not_throw()
    {
        Assert.False(ProjectPath.Matches(@"C:\a", ["C:\\a\0b"]));
        Assert.Null(ProjectPath.Normalize(null));
    }

    // ---- mac（/Users/x/…）。27-1 で、Cursor の workspace_roots も Claude Code の cwd も /Users/… の絶対パスと確認 ----

    [MacTheory]
    [InlineData("/Users/x/proj", "/Users/x/proj")]
    [InlineData("/Users/x/proj", "/USERS/x/Proj")]          // 大文字小文字（7.14）
    [InlineData("/Users/x/proj/", "/Users/x/proj")]         // 末尾区切り（起動側）
    [InlineData("/Users/x/proj", "/Users/x/proj/")]         // 末尾区切り（セッション側）
    [InlineData("/Users/x/work/../proj", "/Users/x/proj")]  // 正規化
    [InlineData("/Users/x/my proj", "/Users/x/my proj")]    // 空白
    [InlineData("/Users/x/日本語 フォルダ", "/Users/x/日本語 フォルダ")]
    public void Mac_matches(string project, string root) =>
        Assert.True(ProjectPath.Matches(project, [root]));

    [MacTheory]
    [InlineData("/Users/x/proj", "/Users/x/proj2")]
    [InlineData("/Users/x/proj", "/Users/x/proj/sub")]      // 完全一致のみ。サブフォルダは対象外
    [InlineData("/Users/x/proj", "/Users/x")]
    [InlineData("/Users/x/proj", "")]
    [InlineData("", "/Users/x/proj")]
    public void Mac_does_not_match(string project, string root) =>
        Assert.False(ProjectPath.Matches(project, [root]));

    [MacFact]
    public void Mac_multi_root_matches_when_any_root_matches()
    {
        Assert.True(ProjectPath.Matches("/b", ["/a", "/B"]));
        Assert.False(ProjectPath.Matches("/c", ["/a", "/b"]));
        Assert.False(ProjectPath.Matches("/a", []));
    }

    [MacFact]
    public void Mac_invalid_paths_do_not_throw()
    {
        Assert.False(ProjectPath.Matches("/a", ["/a\0b"]));
        Assert.Null(ProjectPath.Normalize(null));
    }

    // ---- NFC（29-4。計画 7.14）----

    [Fact]
    public void NFC_and_NFD_spellings_of_a_japanese_name_match()
    {
        var nfc = TestPaths.Abs("work", "ぷろじぇくと");
        var nfd = nfc.Normalize(System.Text.NormalizationForm.FormD);
        Assert.NotEqual(nfc, nfd);   // 濁点・半濁点が分かれている

        Assert.True(ProjectPath.Matches(nfc, [nfd]));
        Assert.True(ProjectPath.Matches(nfd, [nfc]));
        Assert.Equal(ProjectPath.Normalize(nfc), ProjectPath.Normalize(nfd));
        Assert.Equal(nfc, ProjectPath.Normalize(nfd));
    }

    // ---- シンボリックリンク（29-4。mac）----

    [MacFact]
    public void A_project_matches_by_its_logical_or_its_real_path()
    {
        var dir = RealPath.Resolve(Path.Combine(Path.GetTempPath(), "mk-pp-" + Guid.NewGuid().ToString("N")));
        try
        {
            var real = Path.Combine(dir, "real");
            Directory.CreateDirectory(real);
            var link = Path.Combine(dir, "lnk");
            Directory.CreateSymbolicLink(link, real);
            var other = Path.Combine(dir, "other");
            Directory.CreateDirectory(other);

            Assert.True(ProjectPath.Matches(link, [real]));    // 起動はリンク、Claude の cwd・git は実パス
            Assert.True(ProjectPath.Matches(real, [link]));    // 起動は実パス（pwd -P）、Cursor の workspace_roots はリンク
            Assert.True(ProjectPath.Matches(link, [link]));
            Assert.False(ProjectPath.Matches(link, [other]));
            Assert.False(ProjectPath.Matches(link, [Path.Combine(real, "sub")]));   // 完全一致のみ
        }
        finally
        {
            RealPath.ClearCache();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
