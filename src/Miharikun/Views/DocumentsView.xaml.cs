using System.Windows;
using System.Windows.Controls;
using Miharikun.ViewModels;

namespace Miharikun.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView() => InitializeComponent();

    // タブが最初に表示されたとき、監視と走査を始める（起動を遅くしない）。
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as DocumentsViewModel)?.Start();
}
