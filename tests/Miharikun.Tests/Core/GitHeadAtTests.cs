using System.Diagnostics;
using Miharikun.Core.Git;
using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

/// <summary>Phase 19-5：ブランチと時刻からの HEAD（計画 8.4）。Claude Code のログには head が無いため、App が git で求める。</summary>
public sealed class GitHeadAtTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-headat-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset D1 = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset D2 = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset D3 = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset D4 = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

    public GitHeadAtTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    private string Git(DateTimeOffset? when, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false" })
            psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (when is { } w)
        {
            // コミットの時刻（--before が見る committer date）を指定する
            psi.Environment["GIT_AUTHOR_DATE"] = w.ToString("o");
            psi.Environment["GIT_COMMITTER_DATE"] = w.ToString("o");
        }
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd().Trim();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return o;
    }

    private string CommitAt(DateTimeOffset when, string file, string message)
    {
        File.WriteAllText(Path.Combine(_dir, file), message);
        Git(null, "add", "-A");
        Git(when, "commit", "-m", message);
        return Git(null, "rev-parse", "HEAD");
    }

    /// <summary>main に D1・D2・D3 の 3 コミットを作る。</summary>
    private (string C1, string C2, string C3) ThreeCommits()
    {
        Git(null, "init", "-b", "main");
        var c1 = CommitAt(D1, "a.txt", "one");
        var c2 = CommitAt(D2, "b.txt", "two");
        var c3 = CommitAt(D3, "c.txt", "three");
        return (c1, c2, c3);
    }

    // ---- 時刻 ----

    [Fact]
    public void Returns_the_latest_commit_at_or_before_the_time()
    {
        var (c1, c2, c3) = ThreeCommits();
        var git = new GitClient(_dir);

        Assert.Equal(c2, git.GetHeadAt("main", D2.AddHours(2)));
        Assert.Equal(c3, git.GetHeadAt("main", D3.AddDays(30)));
        Assert.Equal(c1, git.GetHeadAt("main", D1.AddMinutes(1)));
    }

    [Fact]
    public void A_commit_made_exactly_at_the_time_is_included()
    {
        var (_, c2, _) = ThreeCommits();

        Assert.Equal(c2, new GitClient(_dir).GetHeadAt("main", D2));
    }

    [Fact]
    public void Nothing_before_the_time_is_unknown()
    {
        ThreeCommits();

        Assert.Null(new GitClient(_dir).GetHeadAt("main", D1.AddHours(-1)));
    }

    [Fact]
    public void The_time_can_be_in_any_offset()
    {
        var (_, c2, _) = ThreeCommits();

        // D2 + 2 時間 = 2026-10-02 12:00Z = 21:00+09:00
        Assert.Equal(c2, new GitClient(_dir).GetHeadAt("main", new DateTimeOffset(2026, 10, 2, 21, 0, 0, TimeSpan.FromHours(9))));
        Assert.Null(new GitClient(_dir).GetHeadAt("main", new DateTimeOffset(2026, 10, 1, 18, 59, 59, TimeSpan.FromHours(9))));   // D1 の 1 秒前
    }

    // ---- ブランチ ----

    [Fact]
    public void Follows_the_given_branch_not_the_checked_out_one()
    {
        var (_, c2, c3) = ThreeCommits();
        Git(null, "branch", "feature/x", c2);
        Git(null, "checkout", "-q", "feature/x");
        var c4 = CommitAt(D4, "d.txt", "four on feature");
        Git(null, "checkout", "-q", "main");
        var git = new GitClient(_dir);

        Assert.Equal(c4, git.GetHeadAt("feature/x", D4.AddDays(1)));
        Assert.Equal(c3, git.GetHeadAt("main", D4.AddDays(1)));
    }

    [Fact]
    public void A_branch_that_does_not_exist_is_unknown()
    {
        ThreeCommits();

        Assert.Null(new GitClient(_dir).GetHeadAt("no-such-branch", D3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("HEAD")]
    [InlineData("-n")]
    [InlineData("--all")]
    [InlineData("--output=evil.txt")]
    [InlineData("foo..bar")]
    [InlineData("a b")]
    [InlineData("@{-1}")]
    [InlineData("main; calc")]
    [InlineData("ma~1in")]
    public void Names_that_are_not_safe_branch_names_are_not_used(string? branch)
    {
        ThreeCommits();

        Assert.Null(new GitClient(_dir).GetHeadAt(branch, D3.AddDays(1)));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
    }

    [Fact]
    public void Not_a_repo_or_missing_folder_is_unknown_without_throwing()
    {
        Assert.Null(new GitClient(_dir).GetHeadAt("main", D3));                                  // git init していない
        Assert.Null(new GitClient(Path.Combine(_dir, "no-such-folder")).GetHeadAt("main", D3));
    }

    [Fact]
    public void The_two_heads_feed_the_existing_commit_list()
    {
        var (c1, _, c3) = ThreeCommits();
        var git = new GitClient(_dir);

        var from = git.GetHeadAt("main", D1.AddMinutes(1));   // セッションの開始時
        var to = git.GetHeadAt("main", D3.AddMinutes(1));     // 最後の動き

        var commits = git.GetCommits(from, to)!;
        Assert.Equal(["three", "two"], commits.Select(c => c.Subject));
        Assert.Equal(c1, from);
        Assert.Equal(c3, to);
    }

    // ---- 作業ツリーのファイルは未コミットの判定から外す（8.6） ----

    private string Wt(params string[] parts) => Path.Combine([_dir, ".claude", "worktrees", .. parts]);

    [Fact]
    public void Worktree_files_are_recognized_under_the_project_worktrees_folder()
    {
        Assert.True(Uncommitted.IsInWorktree(Wt("feature-a", "src", "x.cs"), _dir));
        Assert.True(Uncommitted.IsInWorktree(Wt("feature-a", "x.cs").ToUpperInvariant(), _dir));   // 大文字小文字は無視（Windows）
        Assert.True(Uncommitted.IsInWorktree(Path.Combine(".claude", "worktrees", "a", "x.cs"), _dir));   // 相対パスはプロジェクト基準

        Assert.False(Uncommitted.IsInWorktree(Path.Combine(_dir, "src", "x.cs"), _dir));
        Assert.False(Uncommitted.IsInWorktree(Path.Combine(_dir, ".claude", "settings.json"), _dir));
        Assert.False(Uncommitted.IsInWorktree(Path.Combine(_dir, ".claude", "worktrees-notes.md"), _dir));   // 名前が似ているだけ
        Assert.False(Uncommitted.IsInWorktree(@"D:\elsewhere\.claude\worktrees\a\x.cs", _dir));
    }

    [Fact]
    public void Uncommitted_files_leaves_out_worktree_files_and_counts_them()
    {
        Git(null, "init", "-b", "main");
        CommitAt(D1, "a.txt", "one");
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "changed");
        var status = new GitClient(_dir).GetStatus();
        var changed = new[] { Path.Combine(_dir, "a.txt"), Wt("w1", "x.cs"), Wt("w1", "y.cs") };

        var files = Uncommitted.Files(status, changed, _dir);

        Assert.Equal([Path.Combine(_dir, "a.txt")], files);
        Assert.Equal(2, Uncommitted.WorktreeFileCount(changed, _dir));
    }

    [Fact]
    public void Commit_check_is_not_applicable_when_every_changed_file_is_in_a_worktree()
    {
        var (level, text) = CommitCheck.Evaluate(changedFileCount: 2, uncommitted: [], worktreeFileCount: 2);

        Assert.Equal("na", level);
        Assert.Contains("作業ツリー", text);
        Assert.DoesNotContain("コミット済み", text);
    }

    [Fact]
    public void Commit_check_judges_by_the_main_files_when_only_some_are_in_a_worktree()
    {
        Assert.Equal("ok", CommitCheck.Evaluate(3, [], worktreeFileCount: 1).Level);
        Assert.Equal("ng", CommitCheck.Evaluate(3, ["a.cs"], worktreeFileCount: 1).Level);
    }

    [Fact]
    public void Commit_check_keeps_its_old_answers_without_worktree_files()
    {
        Assert.Equal("na", CommitCheck.Evaluate(2, null, worktreeFileCount: 2).Level);   // git が使えないのが先
        Assert.Contains("不明", CommitCheck.Evaluate(2, null, worktreeFileCount: 2).Text);
        Assert.Contains("編集なし", CommitCheck.Evaluate(0, [], worktreeFileCount: 0).Text);
        Assert.Equal("ok", CommitCheck.Evaluate(2, []).Level);
    }

    // ---- セッションのコミット一覧（Phase 21-3） ----

    private static SessionSummary ClaudeSession(int stepSeconds, string? branch = "main")
    {
        var b = new ClaudeLogBuilder("s", @"C:\work\proj", branch) { Step = stepSeconds };
        var normalizer = new Miharikun.Core.Agents.ClaudeTranscriptNormalizer("s", "s.jsonl");
        var events = new List<Miharikun.Core.Agents.AgentEvent>();
        var i = 0;
        foreach (var line in new[] { b.User("やって"), b.AssistantText("途中"), b.AssistantText("おわり", stopReason: "end_turn") })
            events.AddRange(normalizer.NormalizeLine(++i, line));
        return SessionAnalyzer.Analyze(new Miharikun.Core.Agents.SessionKey("claude", "s"), events);
    }

    [Fact]
    public void A_claude_session_without_heads_gets_its_commits_from_the_branch_and_the_times()
    {
        ThreeCommits();   // main: one (10/1 10:00Z), two (10/2 10:00Z), three (10/3 10:00Z)
        // 開始 10/3 06:00Z（head は two）、最後の動き 10/3 14:00Z（head は three）→ その間の three
        var s = ClaudeSession(stepSeconds: 4 * 3600);

        Assert.Null(s.StartHead);
        Assert.Equal("main", s.Branch);
        var commits = SessionCommits.Load(s, new GitClient(_dir));

        Assert.Equal(["three"], commits!.Select(c => c.Subject));
    }

    [Fact]
    public void A_session_with_heads_uses_them_and_does_not_ask_for_the_branch()
    {
        var (c1, _, c3) = ThreeCommits();
        var cursor = new Miharikun.Core.Agents.CursorAgent();
        var events = new[]
        {
            cursor.Normalize(TestData.Raw("sessionStart", line: 1, sec: 0, git: new Miharikun.Core.Agents.GitSnapshot("not-a-branch", c1))).Single(),
            cursor.Normalize(TestData.Raw("stop", "\"status\":\"completed\"", line: 2, sec: 5, git: new Miharikun.Core.Agents.GitSnapshot("not-a-branch", c3))).Single(),
        };
        var s = SessionAnalyzer.Analyze(new Miharikun.Core.Agents.SessionKey("cursor", "conv-1"), events);

        var commits = SessionCommits.Load(s, new GitClient(_dir));

        Assert.Equal(["three", "two"], commits!.Select(c => c.Subject));
    }

    [Fact]
    public void A_session_started_before_the_first_commit_gets_every_commit_up_to_its_last_activity()
    {
        // 実ログで見つけた形：セッションの途中で、リポジトリの最初のコミットができた（開始より前のコミットが無い）
        Git(null, "init", "-b", "main");
        CommitAt(D3, "c.txt", "three");   // 10/3 10:00Z（セッション：開始 06:00Z・最後 14:00Z の間）
        CommitAt(D3.AddHours(2), "d.txt", "three-b");
        CommitAt(D4, "e.txt", "after the session");   // セッションのあと
        var s = ClaudeSession(stepSeconds: 4 * 3600);

        var git = new GitClient(_dir);
        Assert.Null(git.GetHeadAt("main", s.StartedAt));   // 開始より前のコミットは無い
        Assert.Equal(["three-b", "three"], SessionCommits.Load(s, git)!.Select(c => c.Subject));
    }

    [Fact]
    public void Commits_up_to_a_head_are_all_of_them_newest_first_and_unknown_for_a_bad_head()
    {
        var (_, _, c3) = ThreeCommits();
        var git = new GitClient(_dir);

        Assert.Equal(["three", "two", "one"], git.GetCommitsUpTo(c3)!.Select(c => c.Subject));
        Assert.Null(git.GetCommitsUpTo(null));
        Assert.Null(git.GetCommitsUpTo("--all"));
        Assert.Null(git.GetCommitsUpTo("deadbeef"));   // 存在しない
    }

    [Fact]
    public void Without_heads_and_without_a_branch_the_commits_are_unknown()
    {
        ThreeCommits();

        Assert.Null(SessionCommits.Load(ClaudeSession(4 * 3600, branch: null), new GitClient(_dir)));
    }

    [Fact]
    public void Commit_key_changes_with_the_last_activity_only_for_sessions_without_heads()
    {
        var a = ClaudeSession(60);
        var b = ClaudeSession(120);

        Assert.Equal(SessionCommits.Key(a), SessionCommits.Key(ClaudeSession(60)));
        Assert.NotEqual(SessionCommits.Key(a), SessionCommits.Key(b));   // 最後の動きが違う → 取り直す

        var cursor = new Miharikun.Core.Agents.CursorAgent();
        Miharikun.Core.Agents.AgentEvent Ev(int sec) =>
            cursor.Normalize(TestData.Raw("stop", "\"status\":\"completed\"", line: 1, sec: sec, git: new Miharikun.Core.Agents.GitSnapshot("main", "abc1234"))).Single();
        var k1 = SessionCommits.Key(SessionAnalyzer.Analyze(new Miharikun.Core.Agents.SessionKey("cursor", "c"), [Ev(0)]));
        var k2 = SessionCommits.Key(SessionAnalyzer.Analyze(new Miharikun.Core.Agents.SessionKey("cursor", "c"), [Ev(0), Ev(30)]));
        Assert.Equal(k1, k2);   // head を持つセッションは、時刻が進んでも同じ（git log を実行し直さない）
    }
}
