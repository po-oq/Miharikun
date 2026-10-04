using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Miharikun.Core.Documents;
using Miharikun.Docs;
using Miharikun.ViewModels;

namespace Miharikun.Views;

/// <summary>
/// ドキュメントのプレビュー（要件 12.7）。html は元のファイルを file:/// でそのまま開き、md は HTML にして一時ファイルに書いて開く。
/// リンクはここで振り分ける（http は既定ブラウザ、対象フォルダ内の md/html はアプリ内で選択、それ以外は既定のアプリ）。
/// </summary>
public partial class DocumentPreview : UserControl
{
    private DocumentsViewModel? _vm;
    private WebView2? _web;
    private Task<bool>? _initTask;
    private string? _pagePath;          // いま表示しているページ（html 本体、または md の一時 HTML）のパス
    private int _version;               // 連続した選択で、古い読み込みの結果を捨てるための番号

    public DocumentPreview()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PreviewChanged -= OnPreviewChanged;
        _vm = e.NewValue as DocumentsViewModel;
        if (_vm is not null)
            _vm.PreviewChanged += OnPreviewChanged;
    }

    private async void OnPreviewChanged(PreviewTarget? target, bool reload)
    {
        try
        {
            await ShowAsync(target, reload);
        }
        catch (Exception ex)
        {
            // 非同期の例外はここで受けないと、見えないまま消える（プレビューが出ない原因が分からなくなる）。
            _vm?.Log($"プレビューの表示で例外: {ex}");
            ShowMessage($"プレビューを表示できませんでした：{ex.Message}");
        }
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
        WebHost.Visibility = Visibility.Collapsed;
    }

    private async Task ShowAsync(PreviewTarget? target, bool reload)
    {
        var version = ++_version;
        if (target is null)
        {
            ShowMessage("ファイルを選ぶと、ここに表示します");
            return;
        }

        if (!await EnsureWebViewAsync())
            return;
        if (version != _version)
            return;

        if (!File.Exists(target.FullPath))
        {
            ShowMessage("ファイルが見つかりません");
            return;
        }

        string pagePath;
        try
        {
            pagePath = target.Kind == DocumentKind.Html ? target.FullPath : await RenderMarkdownAsync(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm?.Log($"{target.RelativePath} を表示できない: {ex.Message}");
            ShowMessage($"ファイルを読めません：{ex.Message}");
            return;
        }
        if (version != _version)
            return;

        MessageText.Visibility = Visibility.Collapsed;
        WebHost.Visibility = Visibility.Visible;

        var core = _web!.CoreWebView2;
        var sameAsCurrent = string.Equals(_pagePath, pagePath, StringComparison.OrdinalIgnoreCase);
        _pagePath = pagePath;
        if (sameAsCurrent && reload)
            core.Reload();                                              // スクロール位置を保ったまま作り直す
        else
            core.Navigate(MarkdownRenderer.FileUri(pagePath));
    }

    /// <summary>md を読んで HTML にし、一時ファイルに書く（重い md でも画面を止めないよう背景で）。</summary>
    private async Task<string> RenderMarkdownAsync(PreviewTarget target)
    {
        var isDark = _vm?.IsDark ?? false;
        var previewDir = _vm!.PreviewDir;
        return await Task.Run(() =>
        {
            string text;
            using (var stream = new FileStream(target.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd();

            var html = MarkdownRenderer.Render(text, Path.GetDirectoryName(target.FullPath)!, isDark, Path.GetFileName(target.FullPath));
            return PreviewFiles.Write(previewDir, target.FullPath, html);
        });
    }

    // ── WebView2 の準備 ───────────────────────────────────────────────

    private Task<bool> EnsureWebViewAsync() => _initTask ??= InitializeAsync();

    private async Task<bool> InitializeAsync()
    {
        try
        {
            // 未導入だと例外になる。このとき、ツリー・一覧・概要は使えるまま、案内だけを出す。
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowMessage("プレビューには「Microsoft Edge WebView2 Runtime」が必要ですが、このパソコンには入っていません。\n" +
                        "ツリー・一覧・概要は使えます。入れると、md / html をここに表示できます（https://developer.microsoft.com/microsoft-edge/webview2/）。");
            return false;
        }

        try
        {
            var dark = _vm?.IsDark ?? false;
            _web = new WebView2
            {
                DefaultBackgroundColor = dark ? System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E) : System.Drawing.Color.White,
            };
            // 初期化には、画面に載った（ウィンドウハンドルのある）コントロールが要る。先に載せてから始める。
            WebHost.Children.Add(_web);
            WebHost.Visibility = Visibility.Visible;
            MessageText.Visibility = Visibility.Collapsed;

            var env = await CoreWebView2Environment.CreateAsync(null, _vm!.WebViewDataDir);
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or IOException)
        {
            WebHost.Children.Clear();
            _web = null;
            _vm?.Log($"WebView2 を始められない: {ex.Message}");
            ShowMessage($"プレビューを始められませんでした：{ex.Message}");
            _initTask = null;                                            // 次の選択でやり直す
            return false;
        }
    }

    // ── リンクの振り分け ──────────────────────────────────────────────

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            return;
        if (uri.Scheme is "about" or "data" or "blob")
            return;

        // 自分で開いたページ（および同じページ内の #リンク）は通す。
        if (uri.IsFile && _pagePath is not null
            && string.Equals(uri.LocalPath, _pagePath, StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        Route(uri);
    }

    // target="_blank" や window.open：同じ振り分けにして、新しいウィンドウは作らない。
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            Route(uri);
    }

    private void Route(Uri uri)
    {
        if (uri.Scheme is "http" or "https" or "mailto")
            ShellOpen.Open(uri.AbsoluteUri);
        else if (uri.IsFile)
            _vm?.OpenLocalLink(uri.LocalPath);
    }
}
