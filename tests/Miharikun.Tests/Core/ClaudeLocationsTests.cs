using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Phase 20-1：Claude Code の会話ログの探索と照合（要件 9.1）。</summary>
public sealed class ClaudeLocationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "miharikun-claude-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = [];
    private static readonly string Project = TestPaths.Abs("work", "proj");

    /// <summary>Windows 式のフォルダ名（<c>C--work-proj</c>）を、この OS の形にする（mac は <c>-work-proj</c>）。</summary>
    private static string Os(string windowsName) =>
        OperatingSystem.IsWindows() ? windowsName : System.Text.RegularExpressions.Regex.Replace(windowsName, "^[A-Za-z]--", "-");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string ClaudeDir => Path.Combine(_root, ".claude");
    private string ProjectsDir => Path.Combine(ClaudeDir, "projects");

    private ClaudeLocations Locations(string? project = null) => new(ClaudeDir, project ?? Project, _logs.Add);

    private string MakeFolder(string name)
    {
        var dir = Path.Combine(ProjectsDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteLines(string path, params string[] lines) => File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));

    private static string CwdLine(string cwd) =>
        ClaudeLogBuilder.S(new System.Text.Json.Nodes.JsonObject { ["type"] = "user", ["cwd"] = cwd, ["sessionId"] = "s" });

    // ---- フォルダ名の規則 ----

    [WindowsTheory]
    [InlineData(@"C:\zDev\repo\Miharikun", "C--zDev-repo-Miharikun")]
    [InlineData(@"C:\zDev\repo\Miharikun\", "C--zDev-repo-Miharikun")]       // 末尾の区切りは落とす
    [InlineData("C:/zDev/repo/Miharikun", "C--zDev-repo-Miharikun")]         // / 区切りも同じ
    [InlineData(@"D:\x", "D--x")]
    [InlineData(@"C:\a.b\c_d", "C--a-b-c-d")]                                // . と _ も 1 文字ずつ -
    [InlineData(@"C:\a\.claude\worktrees\w1", "C--a--claude-worktrees-w1")]
    [InlineData(@"C:\work\my proj", "C--work-my-proj")]
    public void Folder_name_replaces_every_non_alphanumeric_character_with_a_dash(string path, string expected)
    {
        Assert.Equal(expected, ClaudeFolderName.For(path));
    }

    [MacTheory]
    [InlineData("/Users/x/repo", "-Users-x-repo")]
    [InlineData("/Users/x/repo/", "-Users-x-repo")]                          // 末尾の区切りは落とす
    [InlineData("/Users/x/my proj", "-Users-x-my-proj")]
    [InlineData("/Users/x/a.b/c_d", "-Users-x-a-b-c-d")]                     // . と _ も 1 文字ずつ -
    [InlineData("/Users/x/repo/.claude/worktrees/w1", "-Users-x-repo--claude-worktrees-w1")]   // 27-1 で実機の形を確認
    [InlineData("/Users/x/Documents/repo/github.com/po-oq/ebata", "-Users-x-Documents-repo-github-com-po-oq-ebata")]
    public void Mac_folder_name_replaces_every_non_alphanumeric_character_with_a_dash(string path, string expected)
    {
        Assert.Equal(expected, ClaudeFolderName.For(path));
    }

    [WindowsFact]
    public void Folder_name_does_not_collapse_runs_and_counts_japanese_and_surrogates_per_character()
    {
        Assert.Equal("C--dev" + new string('-', 9), ClaudeFolderName.For(@"C:\dev\日本語 フォルダ"));   // \ + 3 + 空白 + 4
        Assert.Equal("C--x--y", ClaudeFolderName.For(@"C:\x\.y"));   // \ と . が連なっても、まとめずに 2 つの -
        Assert.Equal("C--u--", ClaudeFolderName.For("C:\\u\uD842\uDFB7"));   // 𠮷（サロゲートペア）は 2 文字 = 2 つの -
    }

    [MacFact]
    public void Mac_folder_name_does_not_collapse_runs_and_counts_japanese_and_surrogates_per_character()
    {
        Assert.Equal("-dev" + new string('-', 9), ClaudeFolderName.For("/dev/日本語 フォルダ"));   // / + 3 + 空白 + 4
        Assert.Equal("-x--y", ClaudeFolderName.For("/x/.y"));   // / と . が連なっても、まとめずに 2 つの -
        Assert.Equal("-u--", ClaudeFolderName.For("/u\uD842\uDFB7"));   // 𠮷（サロゲートペア）は 2 文字 = 2 つの -
    }

    [Fact]
    public void Folder_name_of_an_unusable_path_is_null()
    {
        Assert.Null(ClaudeFolderName.For(""));
        Assert.Null(ClaudeFolderName.For("   "));
    }

    [Theory]
    [InlineData("C--work-proj", true)]
    [InlineData("c--WORK-Proj", true)]                          // 大文字小文字は無視
    [InlineData("C--work-proj--claude-worktrees-feature-a", true)]
    [InlineData("C--work-proj--claude-worktrees-", true)]
    [InlineData("C--work-proj-extra", false)]                   // 名前が前方一致するだけの別プロジェクト
    [InlineData("C--work-proj--claude-worktree", false)]
    [InlineData("C--work-proj--claude-other", false)]
    [InlineData("C--work", false)]
    [InlineData("X--other", false)]
    public void Candidate_folders_are_the_same_name_or_the_worktree_form(string folder, bool expected)
    {
        Assert.Equal(expected, ClaudeFolderName.IsCandidate(Os(folder), Project));
    }

    // ---- 探索先 ----

    [Fact]
    public void Candidate_dirs_are_the_existing_matching_folders_only()
    {
        MakeFolder(Os("C--work-proj"));
        MakeFolder(Os("C--work-proj--claude-worktrees-w1"));
        MakeFolder(Os("C--work-proj--claude-worktrees-w2"));
        MakeFolder(Os("C--work-proj-extra"));
        MakeFolder(Os("C--work-other"));

        var dirs = Locations().CandidateDirs().Select(Path.GetFileName).Order().ToList();

        Assert.Equal([Os("C--work-proj"), Os("C--work-proj--claude-worktrees-w1"), Os("C--work-proj--claude-worktrees-w2")], dirs.Order().ToList());
    }

    [Fact]
    public void A_missing_claude_dir_gives_no_candidates_and_is_never_created()
    {
        var locations = Locations();

        Assert.Empty(locations.CandidateDirs());
        Assert.Empty(locations.FindDirsByCwd());
        Assert.False(Directory.Exists(ClaudeDir));
        Assert.False(Directory.Exists(ProjectsDir));
    }

    [Fact]
    public void The_expected_folder_is_reported_even_when_it_does_not_exist_yet_for_watching()
    {
        Assert.Equal(Path.Combine(ProjectsDir, Os("C--work-proj")), Locations().ExpectedDir);
    }

    // ---- cwd の照合 ----

    [WindowsTheory]
    [InlineData(@"C:\work\proj", true)]
    [InlineData(@"c:\WORK\Proj\", true)]                              // 大文字小文字・末尾区切りは無視
    [InlineData("C:/work/proj", true)]
    [InlineData(@"C:\work\proj\.claude\worktrees\feature-a", true)]   // 作業ツリー
    [InlineData(@"C:\work\proj\.claude\worktrees\feature-a\src", true)]
    [InlineData(@"c:\work\PROJ\.CLAUDE\Worktrees\x", true)]
    [InlineData(@"C:\work\proj\docs", false)]                         // 本体のサブフォルダは別（完全一致）
    [InlineData(@"C:\work\proj\.claude", false)]
    [InlineData(@"C:\work\proj\.claude\worktrees", false)]            // 作業ツリーそのものではない
    [InlineData(@"C:\work\proj-extra", false)]
    [InlineData(@"C:\work\other\.claude\worktrees\x", false)]         // 別プロジェクトの作業ツリー
    [InlineData(@"D:\work\proj", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Cwd_matches_the_project_or_one_of_its_worktrees(string? cwd, bool expected)
    {
        Assert.Equal(expected, Locations().Matches(cwd));
    }

    [MacTheory]
    [InlineData("/work/proj", true)]
    [InlineData("/WORK/Proj/", true)]                                  // 大文字小文字・末尾区切りは無視（7.14）
    [InlineData("/work/proj/.claude/worktrees/feature-a", true)]       // 作業ツリー
    [InlineData("/work/proj/.claude/worktrees/feature-a/src", true)]
    [InlineData("/work/PROJ/.CLAUDE/Worktrees/x", true)]
    [InlineData("/work/proj/docs", false)]                             // 本体のサブフォルダは別（完全一致）
    [InlineData("/work/proj/.claude", false)]
    [InlineData("/work/proj/.claude/worktrees", false)]                // 作業ツリーそのものではない
    [InlineData("/work/proj-extra", false)]
    [InlineData("/work/other/.claude/worktrees/x", false)]             // 別プロジェクトの作業ツリー
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Mac_cwd_matches_the_project_or_one_of_its_worktrees(string? cwd, bool expected)
    {
        Assert.Equal(expected, Locations().Matches(cwd));
    }

    // ---- 最初の cwd ----

    [Fact]
    public void First_cwd_is_read_from_the_first_line_that_has_one()
    {
        var file = Path.Combine(_root, "a.jsonl");
        Directory.CreateDirectory(_root);
        WriteLines(file, """{"type":"file-history-snapshot","messageId":"m"}""", "{ not json", "", CwdLine(@"C:\first\cwd"), CwdLine(@"C:\second\cwd"));

        Assert.Equal(@"C:\first\cwd", ClaudeLocations.ReadFirstCwd(file));
    }

    [Fact]
    public void First_cwd_is_null_without_a_cwd_or_when_the_file_is_missing_or_locked()
    {
        var file = Path.Combine(_root, "a.jsonl");
        Directory.CreateDirectory(_root);
        WriteLines(file, """{"type":"file-history-snapshot"}""");

        Assert.Null(ClaudeLocations.ReadFirstCwd(file));
        Assert.Null(ClaudeLocations.ReadFirstCwd(Path.Combine(_root, "none.jsonl")));
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(ClaudeLocations.ReadFirstCwd(file));
    }

    [Fact]
    public void First_cwd_reading_stops_early_in_a_large_file()
    {
        var file = Path.Combine(_root, "a.jsonl");
        Directory.CreateDirectory(_root);
        WriteLines(file, [.. Enumerable.Repeat("""{"type":"attachment"}""", 5000), CwdLine(@"C:\late\cwd")]);

        Assert.Null(ClaudeLocations.ReadFirstCwd(file));   // 先頭の数百行に無ければ、あきらめる（全体は読まない）
    }

    // ---- 候補が無いときの探索 ----

    [Fact]
    public void Fallback_finds_folders_whose_files_have_a_matching_first_cwd_and_logs_it()
    {
        var odd = MakeFolder("some-odd-folder-name");
        WriteLines(Path.Combine(odd, "s1.jsonl"), """{"type":"queue-operation"}""", CwdLine(Project));
        var other = MakeFolder("another");
        WriteLines(Path.Combine(other, "s2.jsonl"), CwdLine(@"C:\work\elsewhere"));
        var worktree = MakeFolder("also-odd");
        WriteLines(Path.Combine(worktree, "s3.jsonl"), CwdLine(Path.Combine(Project, ".claude", "worktrees", "w1")));

        var dirs = Locations().FindDirsByCwd().Select(Path.GetFileName).Order().ToList();

        Assert.Equal(["also-odd", "some-odd-folder-name"], dirs);
        Assert.Contains(_logs, l => l.Contains("フォルダ名の規則"));
    }

    [Fact]
    public void Fallback_logs_nothing_when_nothing_is_found()
    {
        var other = MakeFolder("another");
        WriteLines(Path.Combine(other, "s2.jsonl"), CwdLine(@"C:\work\elsewhere"));

        Assert.Empty(Locations().FindDirsByCwd());
        Assert.Empty(_logs);
    }

    // ---- 場所 ----

    [Fact]
    public void The_claude_dir_defaults_to_the_user_profile_and_can_be_overridden()
    {
        var old = Environment.GetEnvironmentVariable(ClaudeLocations.ClaudeDirEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ClaudeLocations.ClaudeDirEnvVar, null);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), ClaudeLocations.ResolveClaudeDir());

            Environment.SetEnvironmentVariable(ClaudeLocations.ClaudeDirEnvVar, @"D:\custom\.claude");
            Assert.Equal(@"D:\custom\.claude", ClaudeLocations.ResolveClaudeDir());

            Environment.SetEnvironmentVariable(ClaudeLocations.ClaudeDirEnvVar, "  ");
            Assert.EndsWith(".claude", ClaudeLocations.ResolveClaudeDir());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClaudeLocations.ClaudeDirEnvVar, old);
        }
    }
}
