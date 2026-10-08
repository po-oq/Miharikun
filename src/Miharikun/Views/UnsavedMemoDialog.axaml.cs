using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Miharikun.Views;

public enum UnsavedMemoChoice { Save, Discard, Cancel }

/// <summary>未保存のメモがあるまま閉じるときの確認（要件 12.10。［保存］［保存しない］［キャンセル］）。</summary>
public partial class UnsavedMemoDialog : Window
{
    public UnsavedMemoDialog()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(UnsavedMemoChoice.Save);

    private void OnDiscard(object? sender, RoutedEventArgs e) => Close(UnsavedMemoChoice.Discard);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(UnsavedMemoChoice.Cancel);

    /// <summary>ダイアログを閉じた（×・Esc）ときは［キャンセル］と同じ。</summary>
    public static async Task<UnsavedMemoChoice> ShowAsync(Window owner) =>
        await new UnsavedMemoDialog().ShowDialog<UnsavedMemoChoice?>(owner) ?? UnsavedMemoChoice.Cancel;
}
