namespace Miharikun.Core.Memo;

/// <summary>
/// メモタブの編集の状態（画面に依存しない。要件 12.10・実装計画 7.1）。
/// 読み込みの失敗はここに渡さない（呼び手が扱う）。保存は呼び手が書いてから <see cref="MarkSaved"/> を呼ぶ。
/// </summary>
public sealed class MemoEditor(string saved = "")
{
    private string _editBase = "";
    private string _draft = "";

    /// <summary>最後に読んだ／保存した内容（プレビューに出すもの）。</summary>
    public string Saved { get; private set; } = saved;

    public bool IsEditing { get; private set; }

    /// <summary>入力欄の内容。入力されたまま持つ（改行コードを直さない）。</summary>
    public string Draft
    {
        get => _draft;
        set => _draft = value;
    }

    /// <summary>編集を始めた時点の内容と比べて変わっているか。改行コードの違いは変更とみなさない。</summary>
    public bool IsDirty => IsEditing && NormalizeNewLines(_draft) != NormalizeNewLines(_editBase);

    public void BeginEdit()
    {
        IsEditing = true;
        _editBase = Saved;
        _draft = Saved;
    }

    /// <summary>保存できた。編集を終えて、プレビューに戻る。</summary>
    public void MarkSaved(string text)
    {
        Saved = text;
        IsEditing = false;
        _draft = "";
    }

    /// <summary>入力を捨ててプレビューに戻る。編集中に外で変わっていたら true（表示を作り直す）。</summary>
    public bool Discard()
    {
        if (!IsEditing)
            return false;
        IsEditing = false;
        _draft = "";
        return Saved != _editBase;
    }

    /// <summary>メモのファイルが外で書き換わった。表示を作り直すなら true。編集中は入力を変えない。</summary>
    public bool OnExternalChange(string text)
    {
        if (text == Saved)
            return false;
        Saved = text;
        return !IsEditing;
    }

    private static string NormalizeNewLines(string s) => s.Replace("\r\n", "\n");
}
