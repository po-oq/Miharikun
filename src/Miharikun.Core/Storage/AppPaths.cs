namespace Miharikun.Core.Storage;

/// <summary>保存データの配置（要件 6章）。ルートは MIHARIKUN_DATA_DIR で上書きできる（テスト用）。</summary>
public sealed class AppPaths(string root)
{
    public const string DataDirEnvVar = "MIHARIKUN_DATA_DIR";
    public const string AppSessionId = "_app";

    public string Root { get; } = root;

    public static AppPaths Default()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overridden))
            return new AppPaths(overridden);

        return new AppPaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Miharikun"));
    }

    public string EventsDir(string agentId) => Path.Combine(Root, "events", agentId);

    /// <summary>セッション ID をファイル名にする。パス区切り等は '_' に置換する。</summary>
    public string EventFile(string agentId, string sessionId) =>
        Path.Combine(EventsDir(agentId), SanitizeFileName(sessionId) + ".jsonl");

    public string MetaDir(string agentId) => Path.Combine(Root, "meta", agentId);

    public string MetaFile(string agentId, string sessionId) =>
        Path.Combine(MetaDir(agentId), SanitizeFileName(sessionId) + ".json");

    public string ProjectsDir => Path.Combine(Root, "projects");

    /// <summary>
    /// プロジェクトごとの設定ファイル（要件 6章）：projects\{slug}-{hash8}.json。
    /// 元はプロジェクトのフルパスを小文字・末尾区切りなしにしたもの。slug は読みやすさ用、hash8（SHA-256 の先頭 8 桁）で別のパスと区別する。
    /// </summary>
    public string ProjectSettingsFile(string projectPath)
    {
        var key = (Projects.ProjectPath.Normalize(projectPath) ?? projectPath).ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
        return Path.Combine(ProjectsDir, $"{Slug(key)}-{hash}.json");
    }

    /// <summary>英数字以外の連なりを「-」にする。何も残らなければ "project"。</summary>
    private static string Slug(string key)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in key)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60)
            slug = slug[..60].Trim('-');
        return slug.Length == 0 ? "project" : slug;
    }

    public string HookErrorLog => Path.Combine(Root, "logs", "hook-error.log");

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        var result = new string(chars);
        return result is "" or "." or ".." ? AppSessionId : result;
    }
}