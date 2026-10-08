using Miharikun.Core.Git;

namespace Miharikun.Tests.Core;

/// <summary>29-4：使う git の場所（計画 7.13）。OS・PATH・ファイルの有無・リンクは差し替えて試す。</summary>
public sealed class GitLocatorTests
{
    private static string? Find(string? path, IEnumerable<string> existing, string? xcodeSelectLink = null, bool isWindows = false)
    {
        var files = existing.ToHashSet();
        return GitLocator.Find(isWindows, path, files.Contains, p => p == "/var/db/xcode_select_link" ? xcodeSelectLink : null);
    }

    [Fact]
    public void Windows_uses_git_from_the_path_whatever_exists()
    {
        Assert.Equal("git", Find(null, [], isWindows: true));
        Assert.Equal("git", Find(@"C:\a;C:\b", [], isWindows: true));
    }

    [Fact]
    public void Mac_prefers_a_git_in_the_path_other_than_the_usr_bin_stub()
    {
        var found = Find("/usr/bin:/opt/mybin:/bin", ["/usr/bin/git", "/opt/mybin/git", "/opt/homebrew/bin/git"]);

        Assert.Equal("/opt/mybin/git", found);
    }

    [Fact]
    public void Mac_never_uses_the_usr_bin_stub_even_when_it_is_the_only_one_in_the_path()
    {
        Assert.Null(Find("/usr/bin:/bin", ["/usr/bin/git"]));
    }

    [Fact]
    public void Mac_falls_back_in_order_homebrew_usr_local_then_the_developer_folders()
    {
        var all = new[]
        {
            "/opt/homebrew/bin/git", "/usr/local/bin/git", "/Applications/Xcode-beta.app/Contents/Developer/usr/bin/git",
            "/Library/Developer/CommandLineTools/usr/bin/git", "/Applications/Xcode.app/Contents/Developer/usr/bin/git",
        };
        const string selected = "/Applications/Xcode-beta.app/Contents/Developer";

        Assert.Equal("/opt/homebrew/bin/git", Find("/usr/bin", all, selected));
        Assert.Equal("/usr/local/bin/git", Find("/usr/bin", all.Skip(1), selected));
        Assert.Equal("/Applications/Xcode-beta.app/Contents/Developer/usr/bin/git", Find("/usr/bin", all.Skip(2), selected));   // xcode-select が指すもの
        Assert.Equal("/Library/Developer/CommandLineTools/usr/bin/git", Find("/usr/bin", all.Skip(3), selected));
        Assert.Equal("/Applications/Xcode.app/Contents/Developer/usr/bin/git", Find("/usr/bin", all.Skip(4), selected));
        Assert.Null(Find("/usr/bin", [], selected));
    }

    [Fact]
    public void Mac_ignores_relative_path_entries_and_a_missing_path_variable()
    {
        Assert.Null(Find("bin:.:", ["bin/git", "./git"]));   // 相対パスは使わない（カレントディレクトリに左右されないように）
        Assert.Equal("/opt/homebrew/bin/git", Find(null, ["/opt/homebrew/bin/git"]));
    }

    [MacFact]
    public void On_this_mac_the_real_lookup_finds_a_git_that_runs_without_the_stub()
    {
        var git = GitLocator.Find();

        Assert.NotNull(git);   // Command Line Tools か Xcode が入っている前提（27-1 で確認）
        Assert.NotEqual("/usr/bin/git", git);
        Assert.True(File.Exists(git));
    }
}
