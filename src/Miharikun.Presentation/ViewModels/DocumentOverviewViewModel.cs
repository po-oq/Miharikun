using Miharikun.Core.Documents;

namespace Miharikun.ViewModels;

/// <summary>「ドキュメント概要」カード（要件 12.7）：タイトル・相対パス・更新/作成日時・行数・サイズ。</summary>
public sealed class DocumentOverviewViewModel(DocumentOverview overview)
{
    public string Title => overview.Title;

    public string RelativePath => overview.RelativePath;

    public string MetaLine =>
        $"{overview.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm} に更新  {overview.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} に作成  " +
        $"{overview.LineCount:N0} 行  {FormatSize(overview.Size)}";

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}
