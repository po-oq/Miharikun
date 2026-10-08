using Avalonia.Controls;

namespace Miharikun.Views;

/// <summary>
/// ドキュメントタブ（要件 12.7）。監視と走査は、タブが最初に見えたとき <c>DocumentsViewModel.Start()</c> で始める（起動を遅くしない。MainWindow が呼ぶ）。
/// 画面は載せたまま隠すだけなので、プレビューの WebView は作り直されない（計画 7.17）。
/// </summary>
public partial class DocumentsView : UserControl
{
    public DocumentsView() => InitializeComponent();
}
