using Miharikun.Core.Git;

namespace Miharikun.Tests.Core;

/// <summary>29-4：使う git の場所（計画 7.13）。OS・PATH・ファイルの有無・リンクは差し替えて試す。</summary>
public sealed class GitLocatorTests
{
    private static string? Find(string? path, IEnumerable<string> existing, string? xcodeSelectLink = null, bool isWindows = false,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var files = existing.ToHashSet();
        return GitLocator.Find(isWindows, path, files.Contains, p => p == "/var/db/xcode_select_link" ? xcodeSelectLink : null,
            name => env is not null && env.TryGetValue(name, out var v) ? v : null);
    }

    private static string? FindWin(string? path, params string[] existing) => Find(path, existing, isWindows: true);

    private static readonly Dictionary<string, string> WinEnv = new()
    {
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramW6432"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["LOCALAPPDATA"] = @"C:\Users\x\AppData\Local",
    };

    // ---- Windows：PATH の使える項目（ドライブ・UNC）と決まった場所から git.exe のフルパスを返す（計画 9.1）----

    [Fact]
    public void Windows_returns_the_full_path_of_the_first_usable_path_entry_that_has_git_exe()
    {
        Assert.Equal(@"C:\b\git.exe", FindWin(@"C:\a;C:\b;C:\c", @"C:\b\git.exe", @"C:\c\git.exe"));
    }

    [Fact]
    public void Windows_never_uses_relative_dot_empty_rootless_or_device_entries()
    {
        // どれも git.exe が「ある」ことにしても使わない（カレントフォルダの git.exe を拾わないため）
        string[] bad = [@"bin", ".", "", @"\x", @"\\?\C:\Git", @"\\.\C:\Git", @"..\Git", @"Git\cmd"];
        var existing = bad.Select(b => b.TrimEnd('\\') + @"\git.exe").Append("git.exe").Append(@".\git.exe").ToArray();

        Assert.Null(FindWin(string.Join(';', bad), existing));
    }

    [Fact]
    public void Windows_uses_unc_entries_in_both_slash_forms()
    {
        Assert.Equal(@"\\server\share\Git\cmd\git.exe", FindWin(@"\\server\share\Git\cmd", @"\\server\share\Git\cmd\git.exe"));
        Assert.Equal(@"//server/share/Git/cmd\git.exe", FindWin("//server/share/Git/cmd", @"//server/share/Git/cmd\git.exe"));
    }

    [Fact]
    public void Windows_keeps_the_path_order_even_when_drive_and_unc_entries_are_mixed()
    {
        const string path = @"\\server\share\bin;C:\Git\cmd";
        var both = new[] { @"\\server\share\bin\git.exe", @"C:\Git\cmd\git.exe" };

        Assert.Equal(@"\\server\share\bin\git.exe", FindWin(path, both));
        Assert.Equal(@"C:\Git\cmd\git.exe", FindWin(path, both[1]));
        Assert.Equal(@"C:\Git\cmd\git.exe", FindWin(@"C:\Git\cmd;\\server\share\bin", both));
    }

    [Fact]
    public void Windows_trims_spaces_quotes_and_a_trailing_backslash_of_an_entry()
    {
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe",
            FindWin("\"C:\\Program Files\\Git\\cmd\\\"", @"C:\Program Files\Git\cmd\git.exe"));
        Assert.Equal(@"C:\Git\cmd\git.exe", FindWin(@"  C:\Git\cmd/  ", @"C:\Git\cmd\git.exe"));
        Assert.Equal(@"C:\git.exe", FindWin(@"C:\", @"C:\git.exe"));
    }

    [Fact]
    public void Windows_falls_back_to_the_known_places_in_order_when_the_path_has_none()
    {
        var all = new[]
        {
            @"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files (x86)\Git\cmd\git.exe",
            @"C:\Users\x\AppData\Local\Programs\Git\cmd\git.exe",
        };

        Assert.Equal(all[0], Find(@"C:\none", all, isWindows: true, env: WinEnv));
        Assert.Equal(all[1], Find(@"C:\none", all.Skip(1), isWindows: true, env: WinEnv));
        Assert.Equal(all[2], Find(@"C:\none", all.Skip(2), isWindows: true, env: WinEnv));
        Assert.Null(Find(@"C:\none", [], isWindows: true, env: WinEnv));
    }

    [Fact]
    public void Windows_known_places_try_program_files_then_w6432_then_x86_then_local_programs()
    {
        var env = new Dictionary<string, string>
        {
            ["ProgramFiles"] = @"C:\PF32", ["ProgramW6432"] = @"C:\PF64", ["ProgramFiles(x86)"] = @"C:\PF86", ["LOCALAPPDATA"] = @"C:\L",
        };
        string[] all = [@"C:\PF32\Git\cmd\git.exe", @"C:\PF64\Git\cmd\git.exe", @"C:\PF86\Git\cmd\git.exe", @"C:\L\Programs\Git\cmd\git.exe"];

        for (var i = 0; i < all.Length; i++)
            Assert.Equal(all[i], Find(null, all.Skip(i), isWindows: true, env: env));
    }

    [Fact]
    public void Windows_known_places_skip_environment_values_that_are_missing_empty_or_not_usable()
    {
        var env = new Dictionary<string, string>
        {
            ["ProgramFiles"] = "", ["ProgramW6432"] = @"relative\dir", ["ProgramFiles(x86)"] = @"\\?\C:\PF", ["LOCALAPPDATA"] = @"\x",
        };
        string[] files = [@"\Git\cmd\git.exe", @"relative\dir\Git\cmd\git.exe", @"\\?\C:\PF\Git\cmd\git.exe", @"\x\Programs\Git\cmd\git.exe", @"Git\cmd\git.exe"];

        Assert.Null(Find(null, files, isWindows: true, env: env));
        Assert.Null(Find(null, files, isWindows: true));   // 環境変数がひとつも無い
    }

    [Fact]
    public void Windows_path_wins_over_the_known_places()
    {
        var found = Find(@"D:\tools\git\cmd", [@"D:\tools\git\cmd\git.exe", @"C:\Program Files\Git\cmd\git.exe"], isWindows: true, env: WinEnv);

        Assert.Equal(@"D:\tools\git\cmd\git.exe", found);
    }

    [Fact]
    public void Windows_returns_null_when_git_is_nowhere_and_never_the_bare_name()
    {
        Assert.Null(FindWin(null));
        Assert.Null(FindWin(@"C:\a;C:\b"));
    }

    [WindowsFact]
    public void On_this_windows_pc_the_real_lookup_returns_the_full_path_of_an_existing_file()
    {
        var git = GitLocator.Find();

        Assert.NotNull(git);
        Assert.True(Path.IsPathFullyQualified(git));
        Assert.True(File.Exists(git));
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
