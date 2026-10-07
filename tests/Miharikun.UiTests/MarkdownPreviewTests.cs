using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.ViewModels;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>
/// MarkdownPreview の画面の部分（案内の出し入れ）。WebView の中身（読み込み・リンク・スクロール）は OS の部品なので、
/// ヘッドレスでは確かめられない（実機で確認する）。リンクの判断そのものは PreviewNavigationPolicyTests。
/// </summary>
public sealed class MarkdownPreviewTests
{
    private sealed class FakeHost : IPreviewHost
    {
        public string PreviewDir { get; init; } = Path.GetTempPath();
        public bool IsDark => false;
        public string EmptyText => "空の案内";
        public string RuntimeMissingNote => "";
        public event Action<PreviewSource?, bool>? PreviewChanged;
        public List<string> Logs { get; } = [];
        public void Log(string message) => Logs.Add(message);
        public void OpenLocalLink(string fullPath) { }
        public void Raise(PreviewSource? source, bool reload = false) => PreviewChanged?.Invoke(source, reload);
    }

    private static (Window Window, FakeHost Host, MarkdownPreview Preview) Open()
    {
        var host = new FakeHost();
        var preview = new MarkdownPreview { DataContext = host };
        var window = new Window { Width = 600, Height = 400, Content = preview };
        window.Show();
        Flush();
        return (window, host, preview);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock Message(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().First(t => Avalonia.Automation.AutomationProperties.GetAutomationId(t) == "PreviewMessage");

    [AvaloniaFact]
    public void Shows_the_empty_text_at_first()
    {
        var (w, _, _) = Open();
        Assert.Equal("空の案内", Message(w).Text);
    }

    [AvaloniaFact]
    public async Task A_message_source_shows_the_text_without_creating_a_web_view()
    {
        var (w, host, _) = Open();
        host.Raise(new PreviewSource.Message("案内です"));
        await Task.Delay(50);
        Flush();

        var msg = Message(w);
        Assert.Equal("案内です", msg.Text);
        Assert.True(msg.IsVisible);
        Assert.Empty(w.GetVisualDescendants().OfType<NativeWebView>());
    }

    [AvaloniaFact]
    public async Task A_missing_file_shows_a_message()
    {
        var (w, host, _) = Open();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".html");
        host.Raise(new PreviewSource.File(missing, "x.html", Miharikun.Core.Documents.DocumentKind.Html));
        await Task.Delay(100);
        Flush();

        Assert.Equal("ファイルが見つかりません", Message(w).Text);
        Assert.True(Message(w).IsVisible);
    }
}
