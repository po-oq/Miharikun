namespace Miharikun.Core.Sessions;

/// <summary>完了チェックの「コミット済み」の判定。対象はそのセッションが編集したファイルだけ（要件 10章）。</summary>
public static class CommitCheck
{
    /// <summary>
    /// Level は ok / ng / na（不明・該当なし）。uncommitted は git が使えないとき null。
    /// worktreeFileCount は、変更ファイルのうち作業ツリー（Claude Code）のファイルの数。uncommitted はそれを除いて数えたもの（要件 10.1）。
    /// すべてが作業ツリーのファイルなら判定できない（na）。一部なら、本体のファイルだけで判定する。
    /// </summary>
    public static (string Level, string Text) Evaluate(int changedFileCount, IReadOnlyList<string>? uncommitted, int worktreeFileCount = 0)
    {
        if (uncommitted is null)
            return ("na", "コミット済み（git が使えないため不明）");
        if (changedFileCount == 0)
            return ("na", "コミットの確認なし（このセッションの編集なし）");
        if (worktreeFileCount >= changedFileCount)
            return ("na", "コミットの確認なし（作業ツリーの変更のため不明）");
        if (uncommitted.Count == 0)
            return ("ok", "このセッションの変更はコミット済み");
        return ("ng", $"未コミットあり（{uncommitted.Count}ファイル）");
    }
}
