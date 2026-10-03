using System.Text.Json.Serialization;
using Miharikun.Core.Sessions;

namespace Miharikun.Core.Meta;

/// <summary>概要。Text は取り込んだ返事または手で編集した文章。SourceTurn は取り込み元のターン番号（編集したものは null）。</summary>
public sealed record SummaryEntry(string Text, DateTimeOffset ImportedAt, int? SourceTurn);

/// <summary>
/// セッションごとにアプリが書くメタ（要件 6章）。不変で、変更は With* / 各操作が新しい値を返す。
/// </summary>
public sealed record SessionMeta
{
    public int V { get; init; } = 1;

    /// <summary>手動タイトル。TitleIsManual が false のときは null（自動タイトルを使う）。</summary>
    public string? Title { get; init; }
    public bool TitleIsManual { get; init; }

    public SummaryEntry? Summary { get; init; }

    /// <summary>1つ前の概要（1件のみ保持）。</summary>
    public SummaryEntry? PreviousSummary { get; init; }

    public string Memo { get; init; } = "";

    /// <summary>ユーザーが設定するステータス。null は未設定。自動では変わらない。</summary>
    [JsonConverter(typeof(SessionStatusJsonConverter))]
    public SessionStatus? Status { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonIgnore]
    public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);
    [JsonIgnore]
    public bool CanRevertSummary => PreviousSummary is not null;

    public string? DisplayTitle(string? autoTitle) => TitleIsManual && !string.IsNullOrEmpty(Title) ? Title : autoTitle;

    /// <summary>空・空白だけなら手動タイトルを解除して自動タイトルに戻す。</summary>
    public SessionMeta WithManualTitle(string? title, DateTimeOffset now)
    {
        var t = title?.Trim();
        return string.IsNullOrEmpty(t)
            ? this with { Title = null, TitleIsManual = false, UpdatedAt = now }
            : this with { Title = t, TitleIsManual = true, UpdatedAt = now };
    }

    /// <summary>最後の返事を概要にする。いまの概要は1つ前に退避する（それより古いものは捨てる）。</summary>
    public SessionMeta ImportSummary(string text, int? sourceTurn, DateTimeOffset now) =>
        this with { Summary = new SummaryEntry(text.Trim(), now, sourceTurn), PreviousSummary = Summary, UpdatedAt = now };

    /// <summary>手で編集した概要。取り込みと同じく、編集前のものは1つ前に退避するので「戻す」で取り消せる。空にすると概要なし。</summary>
    public SessionMeta EditSummary(string text, DateTimeOffset now)
    {
        var t = text.Trim();
        return this with
        {
            Summary = t.Length == 0 ? null : new SummaryEntry(t, now, null),
            PreviousSummary = Summary,
            UpdatedAt = now,
        };
    }

    /// <summary>概要と1つ前を入れ替える。もう一度呼ぶと元に戻る。</summary>
    public SessionMeta RevertSummary(DateTimeOffset now) =>
        PreviousSummary is null ? this : this with { Summary = PreviousSummary, PreviousSummary = Summary, UpdatedAt = now };

    public SessionMeta WithMemo(string? memo, DateTimeOffset now) => this with { Memo = memo ?? "", UpdatedAt = now };

    public SessionMeta WithStatus(SessionStatus? status, DateTimeOffset now) => this with { Status = status, UpdatedAt = now };

    /// <summary>ボタン操作用。別の値なら切り替え、選択中の値をもう一度押したら未設定に戻す。</summary>
    public SessionMeta ToggleStatus(SessionStatus status, DateTimeOffset now) => WithStatus(Status == status ? null : status, now);

    /// <summary>全文検索の対象にする文字列（手動タイトル・概要・メモ）。</summary>
    [JsonIgnore]
    public string SearchText => string.Join('\n',
        new[] { TitleIsManual ? Title : null, Summary?.Text, Memo }.Where(s => !string.IsNullOrEmpty(s)));
}

public static class SummaryImport
{
    /// <summary>最後の返事を取り込めるか、取り込むならどのターンの返事か。</summary>
    public static (string Text, int? SourceTurn)? FromLastResponse(SessionSummary s)
    {
        var e = s.LastResponse;
        if (e is null || string.IsNullOrWhiteSpace(e.Text))
            return null;

        // その返事より前に始まった最後のターン
        var turn = s.Turns.LastOrDefault(t => t.StartSeq < e.Seq)?.Number;
        return (e.Text, turn);
    }
}