using Miharikun.Core.Meta;

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
        SessionState.Aborted => "停止",
        SessionState.Error => "エラー",
        SessionState.Closed => "閉じた",
        SessionState.Imported => "閉じた（導入前）",
        _ => state.ToString(),
    };

    /// <summary>ユーザー設定のステータスの名前。null は未設定（絞り込みタブの「未設定」）。</summary>
    public static string StatusName(SessionStatus? status) => status switch
    {
        SessionStatus.Working => "作業中",
        SessionStatus.Paused => "中断",
        SessionStatus.Done => "完了",
        _ => "未設定",
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

    public static string ToolLabel(string? name, string? command)
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

    /// <summary>ブランチ名と、開始時から変わったか。例: feature/x（開始時と同じ） / main（開始時: feature/x）。取れていなければ null。</summary>
    public static string? BranchText(SessionSummary s)
    {
        if (s.Branch is null) return null;
        if (s.StartBranch is null) return s.Branch;
        return s.StartBranch == s.Branch ? $"{s.Branch}（開始時と同じ）" : $"{s.Branch}（開始時: {s.StartBranch}）";
    }

    /// <summary>1時間12分 / 3分 / 42秒。</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalHours >= 1) return $"{(int)d.TotalHours}時間{d.Minutes}分";
        if (d.TotalMinutes >= 1) return $"{(int)d.TotalMinutes}分";
        return $"{(int)d.TotalSeconds}秒";
    }

    /// <summary>同じ日なら 11:19、違う日なら 10/02 11:19。</summary>
    public static string Clock(DateTimeOffset at, DateTimeOffset now)
    {
        var a = at.ToLocalTime();
        return a.Date == now.ToLocalTime().Date ? a.ToString("HH:mm") : a.ToString("MM/dd HH:mm");
    }

    public static string TurnStatusLabel(TurnStatus status) => status switch
    {
        TurnStatus.Completed => "済み",
        TurnStatus.Aborted => "停止",
        TurnStatus.Error => "エラー",
        TurnStatus.Running => "実行中",
        _ => "結果不明",
    };

    /// <summary>モデル名。model_params は [{id, value}] 形式なら "id: value" に、それ以外はそのまま添える。</summary>
    public static string? ModelText(SessionSummary s)
    {
        if (s.Model is null) return null;
        var p = FormatModelParams(s.ModelParams);
        return p is null ? s.Model : $"{s.Model}（{p}）";
    }

    private static string? FormatModelParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonArray arr || arr.Count == 0)
                return null;

            var parts = new List<string>();
            foreach (var item in arr)
            {
                if (item is System.Text.Json.Nodes.JsonObject o && o["id"] is { } id && o["value"] is { } value)
                    parts.Add($"{id.ToString()}: {value.ToString()}");
                else if (item is not null)
                    parts.Add(item.ToJsonString());
            }
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }

    /// <summary>圧縮：なし / 1回（直近 85%・auto）。</summary>
    public static string CompactionText(SessionSummary s)
    {
        if (s.CompactionCount == 0) return "なし";
        var detail = new List<string>();
        if (s.LastCompaction?.ContextUsagePercent is { } pct) detail.Add($"直近 {pct}%");
        if (s.LastCompaction?.Trigger is { } trig) detail.Add(trig);
        return detail.Count == 0 ? $"{s.CompactionCount}回" : $"{s.CompactionCount}回（{string.Join("・", detail)}）";
    }

    /// <summary>テスト実行：なし / 5回（成功3 / 失敗2）。成否が取れなかった分は「不明」。</summary>
    public static string TestRunsText(SessionSummary s)
    {
        if (s.TestRuns.Count == 0) return "なし";
        var ok = s.TestRuns.Count(t => t.Succeeded == true);
        var ng = s.TestRuns.Count(t => t.Succeeded == false);
        var unknown = s.TestRuns.Count - ok - ng;
        var detail = $"成功{ok} / 失敗{ng}" + (unknown > 0 ? $" / 不明{unknown}" : "");
        return $"{s.TestRuns.Count}回（{detail}）";
    }

    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}