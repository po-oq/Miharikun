using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Miharikun.ViewModels;

namespace Miharikun.Views;

/// <summary>
/// メモタブ（要件 12.10）。読み込みと監視は、タブが最初に見えたとき <c>MemoViewModel.Start()</c> で始める（MainWindow が呼ぶ）。
/// </summary>
public partial class MemoView : UserControl
{
    private readonly KeyBinding _saveKey;
    private MemoViewModel? _vm;

    public MemoView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 保存：Windows は Ctrl+S、mac は ⌘+S。編集中だけ効く（SaveCommand の CanExecute）。コマンドは DataContext が決まってから渡す。
        var commandModifiers = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        KeyBindings.Add(_saveKey = new KeyBinding { Gesture = new KeyGesture(Key.S, commandModifiers) });
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.FocusEditorRequested -= OnFocusEditorRequested;
        _vm = DataContext as MemoViewModel;
        if (_vm is not null)
            _saveKey.Command = _vm.SaveCommand;
        if (_vm is not null)
            _vm.FocusEditorRequested += OnFocusEditorRequested;
    }

    // 「✎ 編集」の後：入力欄にフォーカスし、カーソルは先頭。表示が切り替わってから行う。
    private void OnFocusEditorRequested() =>
        Dispatcher.UIThread.Post(() =>
        {
            Editor.Focus();
            Editor.CaretIndex = 0;
        }, DispatcherPriority.Input);
}
