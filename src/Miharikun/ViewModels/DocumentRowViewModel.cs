using Miharikun.Core.Documents;

namespace Miharikun.ViewModels;

/// <summary>ドキュメントタブの一覧の 1 行（ファイル名・所属フォルダ・更新日時）。</summary>
public sealed class DocumentRowViewModel(DocumentEntry entry)
{
    public DocumentEntry Entry { get; } = entry;

    public string RelativePath => Entry.RelativePath;

    public string Name => Entry.Name;

    /// <summary>ファイル名の横に出す所属フォルダ（直下のファイルは空）。</summary>
    public string FolderLabel => Entry.Folder;

    public string Icon => Entry.Kind == DocumentKind.Html ? "🌐" : "📄";

    public string ModifiedText => Entry.ModifiedUtc.ToLocalTime().ToString("MM/dd HH:mm");
}
