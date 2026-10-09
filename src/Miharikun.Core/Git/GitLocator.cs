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
        OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("PATH"), File.Exists, ReadLink, Environment.GetEnvironmentVariable));

    /// <summary>使う git の実行ファイルのフルパス。見つからなければ null（git は「不明」として扱う）。結果はプロセスの中で覚える。</summary>
    public static string? Find() => Cached.Value;

    /// <summary>
    /// 場所の決め方（OS・PATH・ファイルの有無・リンクの読み取りは差し替えて試せる）。mac の順：
    /// ①PATH の中の <c>git</c>（<c>/usr/bin/git</c> 以外）②<c>/opt/homebrew/bin/git</c> ③<c>/usr/local/bin/git</c>
    /// ④開発フォルダ（<c>xcode_select_link</c> のリンク先・<c>/Library/Developer/CommandLineTools</c>・<c>/Applications/Xcode.app/Contents/Developer</c>）の <c>usr/bin/git</c>。
    /// Windows の順（計画 9.1）：①PATH の使える形の絶対パス（ドライブ・UNC）の項目の <c>git.exe</c>（PATH の順で最初のもの）
    /// ②決まった場所（ProgramFiles・ProgramW6432・ProgramFiles(x86) の <c>Git\cmd</c>、LOCALAPPDATA の <c>Programs\Git\cmd</c>）。
    /// 名前だけ（<c>git</c>）では起動しない：CreateProcess はカレントフォルダを先に探すので、プロジェクトに置かれた <c>git.exe</c> が動いてしまう（診断 #2）。
    /// </summary>
    public static string? Find(bool isWindows, string? pathEnv, Func<string, bool> fileExists, Func<string, string?> readLink,
        Func<string, string?> getEnv)
    {
        if (isWindows)
            return FindWindows(pathEnv, fileExists, getEnv);

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

    private static string? FindWindows(string? pathEnv, Func<string, bool> fileExists, Func<string, string?> getEnv)
    {
        foreach (var raw in (pathEnv ?? "").Split(';'))
        {
            var dir = raw.Trim(' ', '\t', '"');
            if (IsUsableAbsolute(dir) && fileExists(JoinGit(dir, "")))
                return JoinGit(dir, "");
        }

        (string Var, string Sub)[] knownPlaces =
        [
            ("ProgramFiles", @"Git\cmd"),
            ("ProgramW6432", @"Git\cmd"),
            ("ProgramFiles(x86)", @"Git\cmd"),
            ("LOCALAPPDATA", @"Programs\Git\cmd"),
        ];
        foreach (var (name, sub) in knownPlaces)
        {
            var root = getEnv(name)?.Trim(' ', '\t', '"');
            if (string.IsNullOrEmpty(root) || !IsUsableAbsolute(root))
                continue;
            var candidate = JoinGit(root, sub);
            if (fileExists(candidate))
                return candidate;
        }
        return null;
    }

    private static string JoinGit(string dir, string sub) =>
        dir.TrimEnd('\\', '/') + "\\" + (sub.Length > 0 ? sub + "\\" : "") + "git.exe";

    /// <summary>使える形の絶対パスか（文字列だけで判定）。ドライブ文字で始まる、または UNC（先頭の区切り 2 つの次が <c>?</c>・<c>.</c>・区切りでない）。
    /// 相対・<c>.</c>・空・ドライブなしのルート・<c>\\?\</c>/<c>\\.\</c>（デバイスの形）は使わない。<c>Path.IsPathFullyQualified</c> は mac で <c>C:\a</c> を相対と見るので使わない。</summary>
    private static bool IsUsableAbsolute(string path)
    {
        if (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]))
            return true;
        return path.Length >= 3 && IsSeparator(path[0]) && IsSeparator(path[1])
            && path[2] is not ('?' or '.') && !IsSeparator(path[2]);
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';
    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static string? ReadLink(string path)
    {
        try { return new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
