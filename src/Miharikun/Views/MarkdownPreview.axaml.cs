using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Miharikun.Core.Documents;
using Miharikun.Docs;
using Miharikun.ViewModels;

namespace Miharikun.Views;

/// <summary>
/// md / html のプレビュー（要件 12.7・12.10。ドキュメントタブとメモタブで共用）。html は元のファイルを file:/// でそのまま開き、
/// md は HTML にして一時ファイルに書いて開く。リンクはここで振り分ける（http は既定ブラウザ、ローカルのファイルはホストへ）。
/// </summary>
public partial class MarkdownPreview : UserControl
{
    /// <summary>Windows の WebView2 の作業フォルダ（App 起動時に決める）。mac では使わない。</summary>
    public static string? WebView2UserDataFolder { get; set; }

    private IPreviewHost? _vm;
    private NativeWebView? _web;
    private bool _initDone;
    private string? _pagePath;          // いま表示しているページ（html 本体、または md の一時 HTML）のパス
    private bool _loadingPage;          // 自分で開いたページを読み込んでいる間（iframe の読み込みを通すため。PreviewNavigationPolicy）
    private int? _restoreScrollY;       // 再読み込みの後に戻すスクロール位置
    private int _version;               // 連続した選択で、古い読み込みの結果を捨てるための番号

    public MarkdownPreview()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PreviewChanged -= OnPreviewChanged;
        _vm = DataContext as IPreviewHost;
        if (_vm is not null)
        {
            _vm.PreviewChanged += OnPreviewChanged;
            MessageText.Text = _vm.EmptyText;
        }
    }

    private async void OnPreviewChanged(PreviewSource? source, bool reload)
    {
        try
        {
            await ShowAsync(source, reload);
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
        MessageText.IsVisible = true;
        WebHost.IsVisible = false;
    }

    private async Task ShowAsync(PreviewSource? source, bool reload)
    {
        var version = ++_version;
        switch (source)
        {
            case null:
                ShowMessage(_vm?.EmptyText ?? "");
                return;
            case PreviewSource.Message message:
                ShowMessage(message.Text);
                return;
        }

        if (!EnsureWebView())
            return;

        if (source is PreviewSource.File file && !System.IO.File.Exists(file.FullPath))
        {
            ShowMessage("ファイルが見つかりません");
            return;
        }

        string pagePath;
        try
        {
            pagePath = source switch
            {
                PreviewSource.File { Kind: DocumentKind.Html } html => html.FullPath,
                PreviewSource.File md => await RenderMarkdownFileAsync(md),
                PreviewSource.Markdown memo => await RenderMarkdownTextAsync(memo),
                _ => throw new InvalidOperationException(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm?.Log($"{NameOf(source)} を表示できない: {ex.Message}");
            ShowMessage($"ファイルを読めません：{ex.Message}");
            return;
        }
        if (version != _version)
            return;

        MessageText.IsVisible = false;
        WebHost.IsVisible = true;

        var web = _web!;
        var sameAsCurrent = string.Equals(_pagePath, pagePath, StringComparison.OrdinalIgnoreCase);
        _pagePath = pagePath;
        if (sameAsCurrent && reload)
        {
            // Refresh() ではスクロール位置が保たれない（mac の WKWebView。要件 12.7）ので、前に取っておいて、読み込み後に戻す。
            _restoreScrollY = PreviewNavigationPolicy.ParseScrollY(await web.InvokeScript("window.scrollY"));
            if (version != _version)
                return;
            _loadingPage = true;
            web.Refresh();
        }
        else
        {
            _restoreScrollY = null;
            _loadingPage = true;
            web.Navigate(new Uri(MarkdownRenderer.FileUri(pagePath)));
        }
    }

    private static string NameOf(PreviewSource source) => source switch
    {
        PreviewSource.File file => file.RelativePath,
        PreviewSource.Markdown memo => memo.Title,
        _ => "",
    };

    /// <summary>md を読んで HTML にし、一時ファイルに書く（重い md でも画面を止めないよう背景で）。</summary>
    private async Task<string> RenderMarkdownFileAsync(PreviewSource.File target)
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

    /// <summary>文字を HTML にして、一時ファイルに書く。</summary>
    private async Task<string> RenderMarkdownTextAsync(PreviewSource.Markdown memo)
    {
        var isDark = _vm?.IsDark ?? false;
        var previewDir = _vm!.PreviewDir;
        return await Task.Run(() =>
            PreviewFiles.Write(previewDir, memo.CacheKey, MarkdownRenderer.Render(memo.Text, memo.BaseFolder, isDark, memo.Title)));
    }

    // ── WebView の準備 ────────────────────────────────────────────────

    /// <summary>最初に出すときに 1 つだけ作る（以降は隠すだけで作り直さない）。作れなければ案内を出して false。</summary>
    private bool EnsureWebView()
    {
        if (_initDone)
            return _web is not null;
        _initDone = true;

        try
        {
            var web = new NativeWebView();
            web.EnvironmentRequested += OnEnvironmentRequested;
            web.NavigationStarted += OnNavigationStarted;
            web.NavigationCompleted += OnNavigationCompleted;
            web.NewWindowRequested += OnNewWindowRequested;
            _web = web;
            WebHost.Children.Add(web);
            WebHost.IsVisible = true;
            MessageText.IsVisible = false;

            // 未導入だと、ツリー・一覧・概要などは使えるまま、案内だけを出す（Windows だけ）。
            if (!IsEngineAvailable(web.AdapterInfo))
                return FailWithRuntimeMissing();
            web.AdapterCreated += (_, _) =>
            {
                if (!IsEngineAvailable(web.AdapterInfo))
                    FailWithRuntimeMissing();
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            WebHost.Children.Clear();
            _web = null;
            _vm?.Log($"WebView を始められない: {ex.Message}");
            ShowMessage($"プレビューを始められませんでした：{ex.Message}");
            _initDone = false;                                          // 次の選択でやり直す
            return false;
        }
    }

    /// <summary>
    /// Windows は WebView2 の Runtime が必要。無いと Avalonia.Controls.WebView は WebView1（EdgeHTML）に切り替えようとするので、
    /// 作れたかどうかでなく種類で見分ける。ほかの OS は OS の部品を使うので確かめない。
    /// </summary>
    private static bool IsEngineAvailable(WebViewAdapterInfo? info) =>
        !OperatingSystem.IsWindows() || info is null || info.Type == WebViewAdapterType.WebView2;

    private bool FailWithRuntimeMissing()
    {
        _vm?.Log($"WebView2 Runtime が見つからない（{(_web?.AdapterInfo?.Type.ToString() ?? "不明")}）");
        WebHost.Children.Clear();
        _web = null;
        ShowMessage("プレビューには「Microsoft Edge WebView2 Runtime」が必要ですが、このパソコンには入っていません。\n" +
                    _vm!.RuntimeMissingNote);
        return false;
    }

    private static void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        // 作業フォルダ（Cookie 等）をデータのフォルダの中に置く。ドキュメントとメモの 2 つで同じ場所を使う。
        if (e is WindowsWebView2EnvironmentRequestedEventArgs windows && WebView2UserDataFolder is { } folder)
            windows.UserDataFolder = folder;
    }

    // ── リンクの振り分け ──────────────────────────────────────────────

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (e.Request is not { } uri
            || PreviewNavigationPolicy.Decide(uri, _pagePath, _loadingPage) == PreviewNavigationAction.Allow)
            return;

        e.Cancel = true;
        Dispatcher.UIThread.Post(() => Route(uri));
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _loadingPage = false;
        if (_restoreScrollY is { } y)
        {
            _restoreScrollY = null;
            if (y > 0)
                _ = _web?.InvokeScript($"window.scrollTo(0,{y})");
        }
    }

    // target="_blank" や window.open：同じ振り分けにして、新しいウィンドウは作らない。
    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.Request is { } uri)
            Dispatcher.UIThread.Post(() => Route(uri));
    }

    private void Route(Uri uri)
    {
        if (PreviewNavigationPolicy.IsExternal(uri))
            ShellOpen.Open(uri.AbsoluteUri);
        else if (uri.IsFile)
            _vm?.OpenLocalLink(uri.LocalPath);
    }
}
