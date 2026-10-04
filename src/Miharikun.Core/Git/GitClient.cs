using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Miharikun.Core.Git;

public sealed record GitCommit(string Sha, string Subject);

/// <summary>git status の結果。DirtyFiles は絶対パスに正規化済み（大文字小文字は無視して比較する）。</summary>
public sealed class GitStatus(string repoRoot, IReadOnlySet<string> dirtyFiles)
{
    public string RepoRoot { get; } = repoRoot;
    public IReadOnlySet<string> DirtyFiles { get; } = dirtyFiles;
}

/// <summary>
/// App が git を呼ぶための薄いラッパー（要件 10章：コミット・未コミットは Hook ではなく App が実行する）。
/// git が無い・リポジトリでない・フォルダが無い・タイムアウトのときは例外にせず null（不明）を返す。
/// </summary>
public sealed partial class GitClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxCommits = 500;

    private readonly string _workDir;
    private readonly TimeSpan _timeout;
    private string? _repoRoot;

    public GitClient(string workDir, TimeSpan? timeout = null)
    {
        _workDir = workDir;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>未コミットのファイル一覧。リポジトリでない／失敗時は null。</summary>
    public GitStatus? GetStatus()
    {
        var root = _repoRoot ??= FindRepoRoot();
        if (root is null)
            return null;

        // --no-optional-locks: 定期的に呼んでも、IDE など他の git 操作の index ロックと競合しない。
        // -z: 日本語や空白を含むパスがクォートされない。--untracked-files=all: 新規ディレクトリ内のファイルも1つずつ出す。
        var result = Run("--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (result is not { ExitCode: 0 } r)
            return null;

        var dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = r.Output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.Length < 4)
                continue;

            if (NormalizePath(Path.Combine(root, e[3..])) is { } path)
                dirty.Add(path);

            // リネーム・コピーは「新しいパス」のあとに「元のパス」が続く。元のパスは読み飛ばす。
            if (e[0] is 'R' or 'C' || e[1] is 'R' or 'C')
                i++;
        }
        return new GitStatus(root, dirty);
    }

    /// <summary>
    /// from..to の新しいコミット順の一覧（git log --oneline 相当）。開始・終了のどちらかが不明、
    /// 履歴の書き換えなどで範囲が解決できない場合は null（不明）。同じ位置なら空。
    /// </summary>
    public IReadOnlyList<GitCommit>? GetCommits(string? fromHead, string? toHead)
    {
        // 値は Hook が記録した git rev-parse の出力。オプション注入を避けるため 16進数以外は受け付けない。
        if (fromHead is null || toHead is null || !Sha().IsMatch(fromHead) || !Sha().IsMatch(toHead))
            return null;
        if (string.Equals(fromHead, toHead, StringComparison.OrdinalIgnoreCase))
            return [];

        var result = Run("log", "--format=%h%x09%s", $"--max-count={MaxCommits}", $"{fromHead}..{toHead}");
        if (result is not { ExitCode: 0 } r)
            return null;

        var commits = new List<GitCommit>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.TrimEnd('\r');
            var tab = text.IndexOf('\t');
            commits.Add(tab < 0 ? new GitCommit(text, "") : new GitCommit(text[..tab], text[(tab + 1)..]));
        }
        return commits;
    }

    /// <summary>
    /// ブランチの、指定した時刻（含む）より前の最後のコミットの hash（40 桁の 16 進）。取れなければ null（不明）。
    /// head を持たない Claude Code のセッションの、開始時と最後の動きの時点の head を求めるのに使う（計画 8.4）。
    /// 名前は、null・空・空白を含む・<c>HEAD</c>・<c>-</c> で始まる・<c>@{</c> を含む・<c>git check-ref-format --branch</c> に通らないものは使わない
    /// （オプションやリビジョン式として解釈されないように）。
    /// </summary>
    public string? GetHeadAt(string? branch, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch == "HEAD" || branch.StartsWith('-') ||
            branch.Contains("@{", StringComparison.Ordinal) || branch.Any(char.IsWhiteSpace))
            return null;
        if (Run("check-ref-format", "--branch", branch) is not { ExitCode: 0 })
            return null;

        var before = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var result = Run("rev-list", "-1", $"--before={before}", branch, "--");
        return result is { ExitCode: 0 } r && r.Output.Trim() is var sha && Sha().IsMatch(sha) ? sha : null;
    }

    public static string? NormalizePath(string path)
    {
        return Projects.ProjectPath.Normalize(path);
    }

    private string? FindRepoRoot()
    {
        var result = Run("rev-parse", "--show-toplevel");
        return result is { ExitCode: 0 } r && r.Output.Trim() is { Length: > 0 } root ? root : null;
    }

    private (int ExitCode, string Output)? Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = _workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();   // 読み捨て（詰まり防止）
            if (!process.WaitForExit(_timeout))
            {
                try { process.Kill(true); } catch (Exception) { }
                return null;
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult());
        }
        catch (Exception)
        {
            // git が無い、フォルダが無い、など。画面では「不明」として扱う。
            return null;
        }
    }

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex Sha();
}

/// <summary>
/// セッションの変更ファイルと git status の突き合わせ（要件 10章）。
/// <c>&lt;対象フォルダ&gt;\.claude\worktrees\</c> 配下（Claude Code の作業ツリー）のファイルは、判定から外す（要件 10.1）。
/// </summary>
public static class Uncommitted
{
    /// <summary>
    /// 変更ファイルのうち未コミットのもの。status が null（git 不明）のときは null。
    /// 相対パスはプロジェクトフォルダ基準で解決する。シェル経由の変更は変更ファイルに入らないので対象外。
    /// </summary>
    public static IReadOnlyList<string>? Files(GitStatus? status, IEnumerable<string> changedFiles, string projectFolder)
    {
        if (status is null)
            return null;

        var result = new List<string>();
        foreach (var file in changedFiles)
        {
            if (IsInWorktree(file, projectFolder))
                continue;   // 作業ツリーのファイルは、本体の git status では個別に出ない（.claude/ ごと未追跡）ので判定できない（要件 10.1）

            var absolute = Path.IsPathRooted(file) ? file : Path.Combine(projectFolder, file);
            if (GitClient.NormalizePath(absolute) is { } normalized && status.DirtyFiles.Contains(normalized))
                result.Add(file);
        }
        return result;
    }

    /// <summary>Claude Code の作業ツリー（&lt;対象フォルダ&gt;\.claude\worktrees\ 配下）のファイルか。相対パスは対象フォルダ基準。大文字小文字は無視する。</summary>
    public static bool IsInWorktree(string file, string projectFolder)
    {
        var absolute = Path.IsPathRooted(file) ? file : Path.Combine(projectFolder, file);
        var worktrees = GitClient.NormalizePath(Path.Combine(projectFolder, ".claude", "worktrees"));
        return GitClient.NormalizePath(absolute) is { } path && worktrees is not null &&
               path.StartsWith(worktrees + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>変更ファイルのうち、作業ツリーのファイルの数（完了チェックで「コミットの確認なし」を出すかの判断に使う）。</summary>
    public static int WorktreeFileCount(IEnumerable<string> changedFiles, string projectFolder) =>
        changedFiles.Count(f => IsInWorktree(f, projectFolder));
}
