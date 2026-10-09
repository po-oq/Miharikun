using System.Text.Json;
using System.Text.Json.Nodes;
using Miharikun.Core.Git;
using Miharikun.Core.Projects;

namespace Miharikun.Core.Sessions;

/// <summary>
/// Claude Code の会話ログの探索と照合（要件 9.1）。<b>読み取りだけ</b>で、<c>.claude</c> にはフォルダも作らない。
/// <list type="bullet">
/// <item>探索先：<c>&lt;.claude&gt;\projects\</c> のうち、名前が「対象フォルダの Claude 式の名前」と同じか、作業ツリーの形のフォルダだけ。</item>
/// <item>照合：各ファイルの最初の <c>cwd</c> を、対象フォルダ（または、その作業ツリー <c>&lt;対象&gt;\.claude\worktrees\&lt;名前&gt;</c>）と比べる。フォルダ名の規則の違いに頼らない。</item>
/// </list>
/// </summary>
public sealed class ClaudeLocations
{
    public const string ClaudeDirEnvVar = "MIHARIKUN_CLAUDE_DIR";

    /// <summary>最初の cwd を探すとき、先頭から読む行数の上限（cwd の無い記録が長く続くファイルで、全体を読まないため）。</summary>
    private const int MaxLinesForFirstCwd = 200;

    private readonly string _claudeDir;
    private readonly string _projectFolder;
    private readonly Action<string>? _log;

    public ClaudeLocations(string claudeDir, string projectFolder, Action<string>? log = null)
    {
        _claudeDir = claudeDir;
        _projectFolder = projectFolder;
        _log = log;
    }

    /// <summary>Claude Code の設定フォルダ。既定は <c>%USERPROFILE%\.claude</c>（<c>MIHARIKUN_CLAUDE_DIR</c> で上書き可）。</summary>
    public static string ResolveClaudeDir()
    {
        var overridden = Environment.GetEnvironmentVariable(ClaudeDirEnvVar);
        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : overridden;
    }

    public string ProjectsDir => Path.Combine(_claudeDir, "projects");

    /// <summary>本体のセッションが置かれるはずのフォルダ（まだ無くてもよい。監視先として、できたら拾うために使う）。</summary>
    public string? ExpectedDir => ClaudeFolderName.For(_projectFolder) is { } name ? Path.Combine(ProjectsDir, name) : null;

    /// <summary>名前の規則で絞った、いま存在する候補のフォルダ（本体と作業ツリー）。呼ぶたびに取り直す。</summary>
    public IReadOnlyList<string> CandidateDirs()
    {
        var dirs = ListSubDirs();
        return [.. dirs.Where(d => ClaudeFolderName.IsCandidate(Path.GetFileName(d), _projectFolder))];
    }

    /// <summary>
    /// 名前の規則が合わなかったときの保険：<b>フォルダの名前の骨組み（英数字だけの並び）が、対象のパス（そのまま・実パス）の骨組みと
    /// 前方一致するフォルダだけ</b>を開き、各ファイルの<b>先頭の cwd だけ</b>を読んで、対象のものがあるフォルダを探す。
    /// ほかのプロジェクトのフォルダは一覧を取るだけで、中のファイル（会話ログ）は開かない。
    /// 開いたフォルダの中は、合わないファイルがあっても打ち切らない（フォルダ名は英数字以外を区別しないので、別のプロジェクトのセッションが混ざりうる）。
    /// 見つかったらログに残す（日本語・記号を含むパスで、規則が違ったかもしれない）。
    /// </summary>
    public IReadOnlyList<string> FindDirsByCwd()
    {
        var wanted = TargetSkeletons();
        var found = new List<string>();
        foreach (var dir in ListSubDirs())
        {
            if (!SkeletonMatches(ClaudeFolderName.Skeleton(Path.GetFileName(dir)), wanted))
                continue;
            foreach (var file in ListFiles(dir))
            {
                if (Matches(ReadFirstCwd(file)))
                {
                    found.Add(dir);
                    _log?.Invoke($"Claude Code の会話ログのフォルダ名の規則が、想定と違った（cwd で見つけた）: {Path.GetFileName(dir)}");
                    break;
                }
            }
        }
        return found;
    }

    // 対象のパス（そのまま）と、実パス（違うときだけ）の骨組み。Claude Code の cwd は実パスで来る。
    private List<string> TargetSkeletons()
    {
        var result = new List<string>();
        if (ProjectPath.Normalize(_projectFolder) is not { } normalized)
            return result;
        result.Add(ClaudeFolderName.Skeleton(normalized));
        var real = ClaudeFolderName.Skeleton(RealPath.Resolve(normalized));
        if (!result.Contains(real))
            result.Add(real);
        return result;
    }

    // どちらかがどちらかの前方一致（同じ名前・作業ツリーの形・記号の数だけ違う名前、または、長いパスが短く切られた名前）。空の骨組みは開かない。
    private static bool SkeletonMatches(string folder, List<string> wanted) =>
        folder.Length > 0 && wanted.Any(w => w.Length > 0 && (folder.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(folder, StringComparison.Ordinal)));

    /// <summary>cwd が、対象フォルダそのもの、またはその作業ツリー（<c>&lt;対象&gt;\.claude\worktrees\</c> の配下）か。</summary>
    public bool Matches(string? cwd) =>
        !string.IsNullOrWhiteSpace(cwd) &&
        (ProjectPath.Matches(_projectFolder, [cwd]) || Uncommitted.IsInWorktree(cwd, _projectFolder));

    /// <summary>
    /// ファイルの最初の <c>cwd</c>（先頭から数百行のうち、最初に cwd を持つ行）。無い・読めない（排他・権限など）ときは null。
    /// 他のプロセスが書き込み中でも読めるよう、共有を最大にして開く。
    /// </summary>
    public static string? ReadFirstCwd(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            for (var i = 0; i < MaxLinesForFirstCwd && reader.ReadLine() is { } line; i++)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (CwdOf(line) is { } cwd)
                    return cwd;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読めないファイルは、照合できないものとして扱う。
        }
        return null;
    }

    /// <summary>1 行の JSON から cwd を取り出す。cwd が無い・文字でない・JSON として読めないときは null。</summary>
    public static string? CwdOf(string line)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject o && o["cwd"] is JsonValue v && v.TryGetValue<string>(out var cwd) &&
                   !string.IsNullOrWhiteSpace(cwd)
                ? cwd
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string[] ListSubDirs()
    {
        try
        {
            return Directory.Exists(ProjectsDir) ? Directory.GetDirectories(ProjectsDir) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"{ProjectsDir} の一覧取得に失敗: {ex.Message}");
            return [];
        }
    }

    private string[] ListFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*.jsonl");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"{dir} の一覧取得に失敗: {ex.Message}");
            return [];
        }
    }
}
