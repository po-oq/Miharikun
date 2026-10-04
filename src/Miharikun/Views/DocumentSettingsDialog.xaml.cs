using System.Windows;
using Wpf.Ui.Controls;

namespace Miharikun.Views;

/// <summary>プロジェクトごとの除外パターン（gitignore 形式）を編集するダイアログ。WebView2 の上に重ならないよう、別ウィンドウにしている。</summary>
public partial class DocumentSettingsDialog : FluentWindow
{
    private readonly string _defaultText;

    public DocumentSettingsDialog(string projectFolder, string currentText, string defaultText)
    {
        InitializeComponent();
        _defaultText = defaultText;
        ProjectText.Text = $"プロジェクト：{projectFolder}";
        IgnoreBox.Text = currentText;
    }

    /// <summary>「保存」で閉じたときの内容。</summary>
    public string IgnoreText { get; private set; } = "";

    private void OnResetClick(object sender, RoutedEventArgs e) => IgnoreBox.Text = _defaultText;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        IgnoreText = IgnoreBox.Text;
        DialogResult = true;
    }
}
