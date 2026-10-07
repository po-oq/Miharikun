namespace Miharikun.Core.Git;

/// <summary>
/// 使う git の場所（計画 7.13）。mac で Command Line Tools も Xcode も無いと、<c>/usr/bin/git</c> は「インストールしますか」の
/// ダイアログを出す（App は 5 秒ごと、Hook は sessionStart・stop で git を呼ぶので出続ける）。そこで、中継の
/// <c>/usr/bin/git</c> は使わず、開発フォルダの git の実体などを直接使う。プロセスは起動せず、ファイルの有無だけ見る（Hook を遅くしない）。
/// </summary>
public static class GitLocator
{
    private const string UsrBinGit = "/usr/bin/git";
    private const string XcodeSelectLink = "/var/db/xcode_select_link";

    private static readonly Lazy<string?> Cached = new(() => Find(
        OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("PATH"), File.Exists, ReadLink));

    /// <summary>使う git の実行ファイル。Windows は <c>git</c>（PATH から）。mac で見つからなければ null（git は「不明」として扱う）。結果はプロセスの中で覚える。</summary>
    public static string? Find() => Cached.Value;

    /// <summary>
    /// 場所の決め方（OS・PATH・ファイルの有無・リンクの読み取りは差し替えて試せる）。mac の順：
    /// ①PATH の中の <c>git</c>（<c>/usr/bin/git</c> 以外）②<c>/opt/homebrew/bin/git</c> ③<c>/usr/local/bin/git</c>
    /// ④開発フォルダ（<c>xcode_select_link</c> のリンク先・<c>/Library/Developer/CommandLineTools</c>・<c>/Applications/Xcode.app/Contents/Developer</c>）の <c>usr/bin/git</c>。
    /// </summary>
    public static string? Find(bool isWindows, string? pathEnv, Func<string, bool> fileExists, Func<string, string?> readLink)
    {
        if (isWindows)
            return "git";

        foreach (var dir in (pathEnv ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = dir.TrimEnd('/') + "/git";
            if (candidate != UsrBinGit && Path.IsPathRooted(candidate) && fileExists(candidate))
                return candidate;
        }

        string?[] fixedCandidates =
        [
            "/opt/homebrew/bin/git",
            "/usr/local/bin/git",
            readLink(XcodeSelectLink) is { Length: > 0 } selected ? selected.TrimEnd('/') + "/usr/bin/git" : null,
            "/Library/Developer/CommandLineTools/usr/bin/git",
            "/Applications/Xcode.app/Contents/Developer/usr/bin/git",
        ];
        foreach (var candidate in fixedCandidates)
        {
            if (candidate is not null && fileExists(candidate))
                return candidate;
        }
        return null;
    }

    private static string? ReadLink(string path)
    {
        try { return new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
