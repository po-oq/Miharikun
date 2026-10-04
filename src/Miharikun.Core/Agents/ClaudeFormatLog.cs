namespace Miharikun.Core.Agents;

/// <summary>
/// 会話ログの形式の変化に気づくためのログ（要件 11.1・計画 8.5）。起動ごとに 1 つを、すべてのファイルで共有する。
/// <b>行の中身（会話の本文）は書かない</b>。書くのは、記録の種類・version・件数だけ。
/// </summary>
public sealed class ClaudeFormatLog(Action<string>? log)
{
    /// <summary>形式を知っていて、イベントにしない（または別に扱う）記録の種類。</summary>
    private static readonly HashSet<string> KnownSkipped = new(StringComparer.Ordinal)
    {
        "attachment", "queue-operation", "last-prompt", "agent-name", "mode", "permission-mode", "cost-state",
        "pr-link", "relocated", "worktree-state", "system", "custom-title", "ai-title",
    };

    private readonly HashSet<string> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknownTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedTypes = new(StringComparer.Ordinal);

    /// <summary>user / assistant 以外で、形式を知っている種類（file-history-* を含む）。</summary>
    public static bool IsKnownSkipped(string type) => KnownSkipped.Contains(type) || type.StartsWith("file-history-", StringComparison.Ordinal);

    /// <summary>初めて見る version を、起動ごとに 1 回だけ書く。</summary>
    public void NoteVersion(string version, string fileName)
    {
        if (_versions.Add(version))
            Write($"Claude Code の会話ログで、初めて見る version: {version}（{fileName}）");
    }

    /// <summary>知らない種類の記録を数える（書くのは <see cref="Flush"/>）。</summary>
    public void NoteUnknownType(string type) =>
        _unknownTypes[type] = _unknownTypes.GetValueOrDefault(type) + 1;

    /// <summary>知らない種類の記録を、種類ごとに起動ごとに 1 回だけ、件数つきで書く。</summary>
    public void Flush()
    {
        foreach (var (type, count) in _unknownTypes)
        {
            if (_reportedTypes.Add(type))
                Write($"Claude Code の会話ログに、知らない種類の記録: type={type}（{count} 件。読み飛ばした）");
        }
    }

    internal void Write(string message) => log?.Invoke(message);
}
