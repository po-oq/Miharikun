namespace Miharikun.Core.Sessions;

/// <summary>画面に出す状態。IdleMinutes があるときは「N 分動きなし」を添える。</summary>
public sealed record DisplayStatus(SessionState State, int? IdleMinutes)
{
    public string? Note => IdleMinutes is { } m ? $"{m}分動きなし" : null;
}

/// <summary>
/// 「実行中」のまま新しい記録が来ないものを「停止」と表示する（要件 10.1・計画 8.3）。
/// Claude Code には終了の記録が無く、プロセスが落ちても「実行中」のまま残るため。Cursor のセッションにも同じ設定が効く。
/// 時刻（いま）が要るので、要約（SessionSummary）の計算には入れず、画面側が 1 秒ごとの更新で適用する。
/// <b>表示用の状態は、画面のどこでもこの結果を使う</b>（丸・文字・「実行中のみ」の絞り込み・件数・詳細ヘッダー・拡大時のタイトル）。
/// </summary>
public static class StalledRule
{
    public const int DefaultTimeoutMinutes = 10;

    /// <param name="timeoutMinutes">0 以下なら無効（要約の状態のまま）。</param>
    public static DisplayStatus DisplayState(SessionSummary summary, DateTimeOffset now, int timeoutMinutes)
    {
        // 1. 実行中でない、またはしきい値が 0 以下 → 要約の状態のまま
        if (summary.State != SessionState.Running || timeoutMinutes <= 0)
            return new DisplayStatus(summary.State, null);

        // 2. 「いま − 最後の動き」がしきい値未満 → 実行中
        var idle = now - summary.LastActivityAt;
        if (idle < TimeSpan.FromMinutes(timeoutMinutes))
            return new DisplayStatus(SessionState.Running, null);

        var minutes = (int)idle.TotalMinutes;

        // 3. 動いているサブエージェント、または結果待ちのツールがある → 実行中のまま（本体は無音になる。長いコマンド・承認待ちもここ）
        if (summary.SubagentsRunning > 0 || summary.RunningTools.Count > 0)
            return new DisplayStatus(SessionState.Running, minutes);

        // 4. それ以外 → 🟡 停止（ボスが押した停止と区別するため、「N 分動きなし」を添える）
        return new DisplayStatus(SessionState.Aborted, minutes);
    }
}
