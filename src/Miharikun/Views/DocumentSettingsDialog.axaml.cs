using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Miharikun.Views;

/// <summary>プロジェクトごとの除外パターン（gitignore 形式）を編集するダイアログ。WebView の上に重ならないよう、別ウィンドウにしている。</summary>
public partial class DocumentSettingsDialog : Window
{
    private readonly string _defaultText = "";

    public DocumentSettingsDialog()
    {
        InitializeComponent();
    }

    public DocumentSettingsDialog(string projectFolder, string currentText, string defaultText) : this()
    {
        _defaultText = defaultText;
        ProjectText.Text = $"プロジェクト：{projectFolder}";
        IgnoreBox.Text = currentText;
    }

    /// <summary>設定を開く。「保存」なら内容、キャンセル・閉じたなら null。</summary>
    public static Task<string?> ShowAsync(Window owner, string projectFolder, string currentText, string defaultText) =>
        new DocumentSettingsDialog(projectFolder, currentText, defaultText).ShowDialog<string?>(owner);

    private void OnResetClick(object? sender, RoutedEventArgs e) => IgnoreBox.Text = _defaultText;

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(IgnoreBox.Text ?? "");

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
