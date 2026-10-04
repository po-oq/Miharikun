namespace Miharikun.Core.Agents;

/// <summary>エージェントをまたいで同じ意味で使う、共通のツール名（AgentEvent.ToolName）。</summary>
public static class CommonTools
{
    /// <summary>コマンドの実行（Cursor の Shell、Claude Code の Bash / PowerShell をこの名前にそろえる）。</summary>
    public const string Shell = "Shell";
}
