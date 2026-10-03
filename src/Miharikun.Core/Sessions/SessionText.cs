namespace Miharikun.Core.Sessions;

/// <summary>カードや詳細に出す文言。画面に依存しないのでテストできる。</summary>
public static class SessionText
{
    /// <summary>絵文字つきの表記（要件 10章の「表示」列）。</summary>
    public static string StateLabel(SessionState state) => state switch
    {
        SessionState.Running => "🔵 " + StateName(state),
        SessionState.YourTurn => "🟢 " + StateName(state),
        SessionState.Aborted => "🟡 " + StateName(state),
        SessionState.Error => "🔴 " + StateName(state),
        _ => "⚪ " + StateName(state),
    };

    /// <summary>絵文字なしの名前。WPF では絵文字が単色になるため、画面では色つきの丸＋この名前で表示する。</summary>
    public static string StateName(SessionState state) => state switch
    {
        SessionState.Running => "実行中",
        SessionState.YourTurn => "ボスの番",
        SessionState.Aborted => "中断",
        SessionState.Error => "エラー",
        SessionState.Closed => "閉じた",
        SessionState.Imported => "閉じた（導入前）",
        _ => state.ToString(),
    };

    public static string RelativeTime(DateTimeOffset at, DateTimeOffset now)
    {
        var d = now - at;
        if (d < TimeSpan.FromMinutes(1)) return "たった今";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}分前";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}時間前";
        return $"{(int)d.TotalDays}日前";
    }

    /// <summary>📝 最後に頼んだこと。複数行なら最初の空でない行。</summary>
    public static string? PromptLine(SessionSummary s) => FirstLine(s.LastPrompt?.Text);

    /// <summary>🔧 最後にやってたこと。実行中ツールがあればそれ（経過秒つき）、なければ最後のツール結果。</summary>
    public static string? ToolLine(SessionSummary s, DateTimeOffset now)
    {
        if (s.RunningTools.Count > 0)
        {
            var t = s.RunningTools.MaxBy(x => x.Seq)!;
            var seconds = Math.Max(0, (int)(now - t.StartedAt).TotalSeconds);
            return $"{ToolLabel(t.ToolName, t.Command)}（実行中・{seconds}秒）";
        }
        return s.LastToolResult is { } e ? ToolLabel(e.ToolName, e.Command) : null;
    }

    /// <summary>💬 最後の返事の先頭2行。</summary>
    public static string? ResponseLines(SessionSummary s)
    {
        var lines = (s.LastResponse?.Text ?? "")
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(2).ToList();
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static string ToolLabel(string? name, string? command)
    {
        var cmd = FirstLine(command);
        return (name, cmd) switch
        {
            (null, null) => "（不明なツール）",
            (null, _) => cmd!,
            (_, null) => name,
            _ => $"{name}: {cmd}",
        };
    }

    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}