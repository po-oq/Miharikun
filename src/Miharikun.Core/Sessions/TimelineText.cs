namespace Miharikun.Core.Sessions;

/// <summary>タイムラインの検索（行の絞り込み）とコピーの文言（要件 12.4）。画面に依存しないのでテストできる。</summary>
public static class TimelineText
{
    /// <summary>検索語を含む行か。画面で省略表示している行も、全文を対象にする。空の語は全件一致。</summary>
    public static bool Matches(TimelineItem item, string? query) => SessionSearch.Matches(item.Text, query);

    public static string KindLabel(TimelineKind kind) => kind switch
    {
        TimelineKind.Input => "入力",
        TimelineKind.Response => "返事",
        TimelineKind.Thought => "思考",
        TimelineKind.Tool => "ツール",
        TimelineKind.Compaction => "圧縮",
        _ => "記録",
    };

    /// <summary>
    /// 「全部コピー」の本文。1行ごとに「時刻　種別：本文（全文）」で、行と行の間は空行。
    /// 時刻のない行（導入前に取り込んだセッション）は時刻を省く。
    /// </summary>
    public static string CopyAll(IEnumerable<TimelineItem> items) =>
        string.Join("\n\n", items.Select(i =>
            (i.HasTime ? i.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "　" : "") + KindLabel(i.Kind) + "：" + i.Text));

    /// <summary>「3/48件」。検索語がないときは出さない（全件表示なので件数は要らない）。</summary>
    public static string CountText(int shown, int total, string? query) =>
        string.IsNullOrWhiteSpace(query) ? "" : $"{shown}/{total}件";
}
