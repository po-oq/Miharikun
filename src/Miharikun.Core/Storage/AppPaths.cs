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