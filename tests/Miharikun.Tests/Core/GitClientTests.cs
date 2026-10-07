using System.Diagnostics;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class GitClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-git-" + Guid.NewGuid().ToString("N"));

    public GitClientTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // .git 内の読み取り専用ファイルを消せるようにする
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    private string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        // ユーザーのグローバル設定（署名など）に左右されないようにする
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false" })
            psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd().Trim();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return o;
    }

    private string InitRepo()
    {
        Git("init", "-b", "main");
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        Git("add", "-A");
        Git("commit", "-m", "first");
        return Git("rev-parse", "HEAD");
    }

    private string Commit(string file, string content, string message)
    {
        var path = Path.Combine(_dir, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Git("add", "-A");
        Git("commit", "-m", message);
        return Git("rev-parse", "HEAD");
    }

    private string P(string relative) => Path.Combine(_dir, relative);

    // ---- リンク経由のプロジェクト（mac。計画 7.14）----

    [MacFact]
    public void A_project_opened_through_a_symlink_reports_dirty_files_under_the_logical_path()
    {
        InitRepo();
        var link = _dir + "-lnk";
        Directory.CreateSymbolicLink(link, _dir);
        try
        {
            File.WriteAllText(P("a.txt"), "changed");
            File.WriteAllText(P("new.txt"), "n");

            var status = new GitClient(link).GetStatus()!;

            Assert.Equal(
                new[] { "a.txt", "new.txt" }.Select(f => Path.GetFullPath(Path.Combine(link, f))).Order(),
                status.DirtyFiles.Order());
            Assert.Equal(["a.txt"], Uncommitted.Files(status, [Path.Combine(link, "a.txt")], link)!.Select(Path.GetFileName));
            Assert.Equal(Path.GetFullPath(link), status.RepoRoot);
        }
        finally
        {
            File.Delete(link);
        }
    }

    [MacFact]
    public void A_subfolder_opened_through_a_symlink_still_reports_paths_from_the_logical_repo_root()
    {
        InitRepo();
        Directory.CreateDirectory(P("sub"));
        File.WriteAllText(P("sub/x.txt"), "x");
        var link = _dir + "-lnk";
        Directory.CreateSymbolicLink(link, _dir);
        try
        {
            var status = new GitClient(Path.Combine(link, "sub")).GetStatus()!;

            Assert.Contains(Path.GetFullPath(Path.Combine(link, "sub", "x.txt")), status.DirtyFiles);
            Assert.Equal(Path.GetFullPath(link), status.RepoRoot);
        }
        finally
        {
            File.Delete(link);
        }
    }

    // ---- status / 未コミット ----

    [Fact]
    public void Clean_repo_has_no_dirty_files()
    {
        InitRepo();

        var status = new GitClient(_dir).GetStatus();

        Assert.NotNull(status);
        Assert.Empty(status!.DirtyFiles);
    }

    [Fact]
    public void Reports_modified_untracked_deleted_and_staged_files()
    {
        InitRepo();
        Commit("keep.txt", "k", "second");
        Commit("gone.txt", "g", "third");
        File.WriteAllText(P("a.txt"), "changed");                  // 変更
        File.WriteAllText(P("new.txt"), "n");                      // 未追跡
        File.Delete(P("gone.txt"));                                // 削除
        File.WriteAllText(P("staged.txt"), "s"); Git("add", "staged.txt");   // ステージ済み

        var status = new GitClient(_dir).GetStatus()!;

        Assert.Equal(
            new[] { "a.txt", "gone.txt", "new.txt", "staged.txt" }.Select(f => Path.GetFullPath(P(f))).Order(),
            status.DirtyFiles.Order());
    }

    [Fact]
    public void Lists_files_inside_new_untracked_directories_one_by_one()
    {
        InitRepo();
        Directory.CreateDirectory(P("newdir/sub"));
        File.WriteAllText(P("newdir/sub/x.cs"), "x");

        var status = new GitClient(_dir).GetStatus()!;

        Assert.Contains(Path.GetFullPath(P("newdir/sub/x.cs")), status.DirtyFiles);
    }

    [Fact]
    public void Handles_japanese_and_spaces_in_paths_without_quoting()
    {
        InitRepo();
        File.WriteAllText(P("日本語 の ファイル.cs"), "x");
        Directory.CreateDirectory(P("フォルダ"));
        File.WriteAllText(P("フォルダ/テスト.md"), "y");

        var status = new GitClient(_dir).GetStatus()!;

        Assert.Contains(Path.GetFullPath(P("日本語 の ファイル.cs")), status.DirtyFiles);
        Assert.Contains(Path.GetFullPath(P("フォルダ/テスト.md")), status.DirtyFiles);
    }

    [Fact]
    public void Rename_reports_the_new_path_and_does_not_break_following_entries()
    {
        InitRepo();
        Commit("old.txt", "some longer content to keep similarity", "add old");
        Git("mv", "old.txt", "renamed.txt");
        File.WriteAllText(P("after.txt"), "z");   // リネームの直後の項目が正しく読めること

        var status = new GitClient(_dir).GetStatus()!;

        Assert.Contains(Path.GetFullPath(P("renamed.txt")), status.DirtyFiles);
        Assert.Contains(Path.GetFullPath(P("after.txt")), status.DirtyFiles);
        Assert.DoesNotContain(Path.GetFullPath(P("old.txt")), status.DirtyFiles.Where(f => !f.EndsWith("renamed.txt")));
    }

    [Fact]
    public void Works_from_a_subfolder_of_the_repo()
    {
        InitRepo();
        Directory.CreateDirectory(P("sub"));
        File.WriteAllText(P("top.txt"), "t");

        var status = new GitClient(P("sub")).GetStatus()!;

        Assert.Contains(Path.GetFullPath(P("top.txt")), status.DirtyFiles);   // リポジトリのルート基準で解決される
    }

    [Fact]
    public void Not_a_repo_missing_folder_and_garbage_return_null_without_throwing()
    {
        Assert.Null(new GitClient(_dir).GetStatus());                              // git init していない
        Assert.Null(new GitClient(Path.Combine(_dir, "no-such-folder")).GetStatus());
        Assert.Null(new GitClient(@"Z:\definitely\not\here").GetStatus());
        Assert.Null(new GitClient(_dir).GetCommits("abcdef1", "abcdef2"));
    }

    [Fact]
    public void Uncommitted_matches_changed_files_to_status()
    {
        InitRepo();
        File.WriteAllText(P("a.txt"), "changed");
        File.WriteAllText(P("b.txt"), "b");
        var status = new GitClient(_dir).GetStatus();

        var files = Uncommitted.Files(status, new[]
        {
            P("a.txt"),                     // 絶対パス・未コミット
            P("A.TXT"),                     // 大文字小文字違いでも一致（Windows）
            "b.txt",                        // 相対パスはプロジェクトフォルダ基準
            P("committed-only.txt"),        // 変更済みでなくクリーン → 対象外
            P("sub/../a.txt"),              // 正規化される
        }, _dir);

        Assert.Equal([P("a.txt"), P("A.TXT"), "b.txt", P("sub/../a.txt")], files);
    }

    [Fact]
    public void Uncommitted_is_null_when_git_is_unknown_and_empty_when_clean()
    {
        Assert.Null(Uncommitted.Files(null, ["a.txt"], _dir));

        InitRepo();
        Assert.Empty(Uncommitted.Files(new GitClient(_dir).GetStatus(), [P("a.txt")], _dir)!);
    }

    [Fact]
    public void A_file_committed_after_the_edit_is_no_longer_uncommitted()
    {
        InitRepo();
        File.WriteAllText(P("a.txt"), "changed");
        Assert.Single(Uncommitted.Files(new GitClient(_dir).GetStatus(), [P("a.txt")], _dir)!);

        Git("add", "-A"); Git("commit", "-m", "commit it");

        Assert.Empty(Uncommitted.Files(new GitClient(_dir).GetStatus(), [P("a.txt")], _dir)!);
    }

    // ---- コミット一覧 ----

    [Fact]
    public void Commits_in_range_are_listed_newest_first()
    {
        var start = InitRepo();
        Commit("b.txt", "b", "二つ目のコミット");
        Commit("c.txt", "c", "third\twith tab");
        var latest = Git("rev-parse", "HEAD");

        var commits = new GitClient(_dir).GetCommits(start, latest)!;

        Assert.Equal(2, commits.Count);
        Assert.Equal("third\twith tab", commits[0].Subject);      // タブを含む件名も最初のタブで分けるだけ
        Assert.Equal("二つ目のコミット", commits[1].Subject);
        Assert.All(commits, c => Assert.Matches("^[0-9a-f]{7,}$", c.Sha));
    }

    [Fact]
    public void Same_head_means_no_commits_and_unknown_ends_mean_unknown()
    {
        var head = InitRepo();
        var git = new GitClient(_dir);

        Assert.Empty(git.GetCommits(head, head)!);
        Assert.Empty(git.GetCommits(head, head.ToUpperInvariant())!);
        Assert.Null(git.GetCommits(null, head));
        Assert.Null(git.GetCommits(head, null));
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("HEAD~1; calc")]
    [InlineData("abc")]                     // 短すぎる
    [InlineData("main")]
    public void Anything_but_a_hex_sha_is_rejected_without_running_git(string bad)
    {
        var head = InitRepo();

        Assert.Null(new GitClient(_dir).GetCommits(bad, head));
        Assert.Null(new GitClient(_dir).GetCommits(head, bad));
    }

    [Fact]
    public void Unresolvable_range_is_unknown()
    {
        var head = InitRepo();

        Assert.Null(new GitClient(_dir).GetCommits("deadbeefdeadbeef", head));   // 履歴の書き換えなどで存在しない
    }

    [Fact]
    public void Start_head_that_is_not_an_ancestor_still_returns_what_git_reports()
    {
        var start = InitRepo();
        Git("checkout", "-b", "side");
        var side = Commit("s.txt", "s", "on side");
        Git("checkout", "main");
        var main = Commit("m.txt", "m", "on main");

        var commits = new GitClient(_dir).GetCommits(side, main)!;

        Assert.Equal(["on main"], commits.Select(c => c.Subject));   // side にないコミットだけ
        Assert.NotEqual(start, main);
    }
}

public sealed class BranchTextTests
{
    private static readonly SessionKey Key = new("cursor", "conv-1");

    private static SessionSummary With(params (string Evt, string Branch)[] snapshots)
    {
        var events = snapshots.Select((s, i) =>
            new CursorAgent().Normalize(Raw(s.Evt, i: i)).Single() with { Git = new GitSnapshot(s.Branch, "h" + i) }).ToList();
        return SessionAnalyzer.Analyze(Key, events);
    }

    private static RawEventRecord Raw(string evt, int i) => TestData.Raw(evt, line: i + 1, sec: i);

    [Fact]
    public void Same_branch_says_unchanged()
    {
        var s = With(("sessionStart", "feature/x"), ("stop", "feature/x"));

        Assert.Equal("feature/x（開始時と同じ）", SessionText.BranchText(s));
        Assert.Equal("feature/x", s.StartBranch);
    }

    [Fact]
    public void Changed_branch_shows_where_it_started()
    {
        var s = With(("sessionStart", "feature/x"), ("stop", "main"));

        Assert.Equal("main（開始時: feature/x）", SessionText.BranchText(s));
    }

    [Fact]
    public void Unknown_branch_is_null_and_a_single_snapshot_has_no_comparison()
    {
        Assert.Null(SessionText.BranchText(SessionAnalyzer.Analyze(Key, TestData.Events(TestData.E("sessionStart")))));
        Assert.Equal("main（開始時と同じ）", SessionText.BranchText(With(("stop", "main"))));
    }
}