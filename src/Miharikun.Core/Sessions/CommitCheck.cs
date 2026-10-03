namespace Miharikun.Core.Sessions;

/// <summary>完了チェックの「コミット済み」の判定。対象はそのセッションが編集したファイルだけ（要件 10章）。</summary>
public static class CommitCheck
{
    /// <summary>Level は ok / ng / na（不明・該当なし）。uncommitted は git が使えないとき null。</summary>
    public static (string Level, string Text) Evaluate(int changedFileCount, IReadOnlyList<string>? uncommitted)
    {
        if (uncommitted is null)
            return ("na", "コミット済み（git が使えないため不明）");
        if (changedFileCount == 0)
            return ("na", "コミットの確認なし（このセッションの編集なし）");
        if (uncommitted.Count == 0)
            return ("ok", "このセッションの変更はコミット済み");
        return ("ng", $"未コミットあり（{uncommitted.Count}ファイル）");
    }
}
