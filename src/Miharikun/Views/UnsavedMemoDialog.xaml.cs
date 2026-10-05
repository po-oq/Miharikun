using System.Windows;
using Wpf.Ui.Controls;

namespace Miharikun.Views;

public enum UnsavedMemoChoice
{
    Cancel,
    Save,
    Discard,
}

/// <summary>未保存のメモを残したままアプリを閉じるときの確認（要件 12.10）。［保存］［保存しない］［キャンセル］。閉じるだけ（×・Esc）はキャンセル。</summary>
public partial class UnsavedMemoDialog : FluentWindow
{
    public UnsavedMemoDialog() => InitializeComponent();

    public UnsavedMemoChoice Choice { get; private set; } = UnsavedMemoChoice.Cancel;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedMemoChoice.Save;
        DialogResult = true;
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        Choice = UnsavedMemoChoice.Discard;
        DialogResult = true;
    }
}
