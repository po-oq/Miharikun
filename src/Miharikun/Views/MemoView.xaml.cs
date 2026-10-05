using System.Windows;
using System.Windows.Controls;
using Miharikun.ViewModels;

namespace Miharikun.Views;

public partial class MemoView : UserControl
{
    private MemoViewModel? _vm;

    public MemoView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.FocusEditorRequested -= OnFocusEditorRequested;
        _vm = e.NewValue as MemoViewModel;
        if (_vm is not null)
            _vm.FocusEditorRequested += OnFocusEditorRequested;
    }

    // タブが表示されたとき、読み込みと監視を始める（最初の 1 回。起動を遅くしない）。
    private void OnLoaded(object sender, RoutedEventArgs e) => _vm?.Start();

    // 「✎ 編集」の後：入力欄にフォーカスし、カーソルは先頭。表示が切り替わってから行う。
    private void OnFocusEditorRequested() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            Editor.Focus();
            Editor.CaretIndex = 0;
            Editor.ScrollToHome();
        });
}
