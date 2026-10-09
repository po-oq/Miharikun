using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Miharikun.Core.Documents;
using Miharikun.Core.Storage;
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

    /// <summary>
    /// テスト用：true のとき WebView を作らず、案内の文字だけ出す。ヘッドレスのテストは STA でないスレッドで動くので、
    /// Windows で本物の WebView2 を作ると失敗する（RPC_E_CHANGED_MODE）。製品では使わない。
    /// </summary>
    public static bool WebViewDisabled { get; set; }

    private IPreviewHost? _vm;
    private NativeWebView? _web;
    private bool _initDone;
    private string? _pagePath;          // いま表示しているページ（html 本体、または md の一時 HTML）のパス
    private bool _loadingPage;          // 自分で開いたページを読み込んでいる間（iframe の読み込みを通すため。PreviewNavigationPolicy）
    private int? _restoreScrollY;       // 再読み込みの後に戻すスクロール位置
    private int _version;               // 連続した選択で、古い読み込みの結果を捨てるための番号
    private string? _pendingHeadingId;  // 読み込み中に頼まれた、読み込み後に移る見出し（目次。Issue #28）
    private bool _pageIsHtml;           // いま開くページが html か（html には読み込み後に Esc の受け口を入れる）

    public MarkdownPreview()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PreviewChanged -= OnPreviewChanged;
            _vm.HeadingScrollRequested -= OnHeadingScrollRequested;
        }
        _vm = DataContext as IPreviewHost;
        if (_vm is not null)
        {
            _vm.PreviewChanged += OnPreviewChanged;
            _vm.HeadingScrollRequested += OnHeadingScrollRequested;
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
                _vm?.OnOutline(null, []);
                ShowMessage(_vm?.EmptyText ?? "");
                return;
            case PreviewSource.Message message:
                _vm?.OnOutline(source, []);
                ShowMessage(message.Text);
                return;
        }

        if (source is PreviewSource.File file && !System.IO.File.Exists(file.FullPath))
        {
            _vm?.OnOutline(source, []);
            ShowMessage("ファイルが見つかりません");
            return;
        }

        if (!EnsureWebView())
        {
            _vm?.OnOutline(source, []);
            return;
        }

        string pagePath;
        IReadOnlyList<OutlineHeading> outline = [];
        try
        {
            switch (source)
            {
                case PreviewSource.File { Kind: DocumentKind.Html } html:
                    pagePath = html.FullPath;
                    break;
                case PreviewSource.File md:
                    (pagePath, outline) = await RenderMarkdownFileAsync(md);
                    break;
                case PreviewSource.Markdown memo:
                    pagePath = await RenderMarkdownTextAsync(memo);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm?.Log($"{NameOf(source)} を表示できない: {ex.Message}");
            _vm?.OnOutline(source, []);
            ShowMessage($"ファイルを読めません：{ex.Message}");
            return;
        }
        if (version != _version)
            return;                                   // 古い結果は、目次も渡さない
        _vm?.OnOutline(source, outline);
        _pageIsHtml = source is PreviewSource.File { Kind: DocumentKind.Html };

        MessageText.IsVisible = false;
        WebHost.IsVisible = true;

        var web = _web!;
        // md はダークの地を自分で持つので、読み込みの間に白く光らないよう同じ色にする。html はファイルの見た目を尊重する
        // （地を指定しないページは、暗い文字を白い地の前提で書いているので、ダークでも白のまま）。
        web.Background = BackgroundFor((_vm?.IsDark ?? false) && source is not PreviewSource.File { Kind: DocumentKind.Html });
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
            _pendingHeadingId = null;
            _loadingPage = true;
            web.Navigate(new Uri(MarkdownRenderer.FileUri(pagePath)));
        }
    }

    /// <summary>WebView の地の色（読み込み前・ページの外に見える色）。md のダークの地（MarkdownRenderer）と同じ。</summary>
    private static IBrush BackgroundFor(bool dark) => new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);

    private static string NameOf(PreviewSource source) => source switch
    {
        PreviewSource.File file => file.RelativePath,
        PreviewSource.Markdown memo => memo.Title,
        _ => "",
    };

    /// <summary>md を読んで HTML にし、一時ファイルに書く（重い md でも画面を止めないよう背景で）。</summary>
    private async Task<(string PagePath, IReadOnlyList<OutlineHeading> Outline)> RenderMarkdownFileAsync(PreviewSource.File target)
    {
        var isDark = _vm?.IsDark ?? false;
        var previewDir = _vm!.PreviewDir;
        return await Task.Run(() =>
        {
            string text;
            using (var stream = new FileStream(target.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd();

            var libFolder = EnsureLib(previewDir);
            var rendering = MarkdownRenderer.RenderWithOutline(text, Path.GetDirectoryName(target.FullPath)!, libFolder: libFolder,
                isDark: isDark, title: Path.GetFileName(target.FullPath));
            var path = PreviewFiles.Write(previewDir, target.FullPath, rendering.Html);
            // Docs の見出しを Presentation の型へ写す（Presentation は Docs を参照しない）。
            IReadOnlyList<OutlineHeading> outline = rendering.Headings
                .Select(h => new OutlineHeading(h.Level, h.Text, h.Id, h.Done, h.TasksDone, h.TasksTotal))
                .ToList();
            return (path, outline);
        });
    }

    /// <summary>文字を HTML にして、一時ファイルに書く。</summary>
    private async Task<string> RenderMarkdownTextAsync(PreviewSource.Markdown memo)
    {
        var isDark = _vm?.IsDark ?? false;
        var previewDir = _vm!.PreviewDir;
        return await Task.Run(() =>
            PreviewFiles.Write(previewDir, memo.CacheKey,
                MarkdownRenderer.Render(memo.Text, memo.BaseFolder, libFolder: EnsureLib(previewDir), isDark: isDark, title: memo.Title)));
    }

    /// <summary>同梱の mermaid・highlight.js が <c>preview/lib/&lt;版&gt;/</c> にあるようにして、そのフォルダを返す（毎回の描画の前に。存在と長さを見るだけで軽い）。</summary>
    private static string EnsureLib(string previewDir) =>
        MarkdownAssets.Ensure(Path.Combine(previewDir, "lib"), AtomicFile.WriteAllBytes, AppLog.Write);

    // ── WebView の準備 ────────────────────────────────────────────────

    /// <summary>最初に出すときに 1 つだけ作る（以降は隠すだけで作り直さない）。作れなければ案内を出して false。</summary>
    private bool EnsureWebView()
    {
        if (WebViewDisabled)
        {
            ShowMessage("（テスト：WebView は作りません）");
            return false;
        }
        if (_initDone)
            return _web is not null;
        _initDone = true;

        try
        {
            var web = new NativeWebView { Background = BackgroundFor(false) };
            web.EnvironmentRequested += OnEnvironmentRequested;
            web.NavigationStarted += OnNavigationStarted;
            web.NavigationCompleted += OnNavigationCompleted;
            web.WebMessageReceived += OnWebMessageReceived;
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
        if (e.Request is not { } uri)
            return;
        var action = PreviewNavigationPolicy.Decide(uri, _pagePath, _loadingPage, isMarkdownPage: !_pageIsHtml);
        if (action == PreviewNavigationAction.Allow)
            return;

        e.Cancel = true;
        if (action == PreviewNavigationAction.Route)
            Dispatcher.UIThread.Post(() => Route(uri));
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _loadingPage = false;
        if (_pageIsHtml)
            _ = _web?.InvokeScript(MarkdownRenderer.EscapeListenerScript);   // html は自前のページなので、Esc の受け口を後から入れる（md は最初から入っている）
        if (_pendingHeadingId is { } headingId)
        {
            _pendingHeadingId = null;
            _restoreScrollY = null;
            _ = _web?.InvokeScript(PreviewScripts.ScrollToHeading(headingId));
        }
        else if (_restoreScrollY is { } y)
        {
            _restoreScrollY = null;
            if (y > 0)
                _ = _web?.InvokeScript($"window.scrollTo(0,{y})");
        }
    }

    // ページからの知らせ（invokeCSharpAction）。受けるのは Esc だけで、ほかは捨てる（html は利用者のファイルの JS が動く。中身はログに書かない）。
    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (PreviewScripts.IsEscapeMessage(e.Body))
            Dispatcher.UIThread.Post(() => _vm?.OnPageEscape());
    }

    // 目次から頼まれた見出しへ移る。読み込み中なら終わってから（再読み込みの位置の復元より優先）。
    private void OnHeadingScrollRequested(string id)
    {
        if (_web is null)
            return;
        if (_loadingPage)
        {
            _pendingHeadingId = id;
            _restoreScrollY = null;
            return;
        }
        _ = _web.InvokeScript(PreviewScripts.ScrollToHeading(id));
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
        else if (uri.IsUnc)
            return;   // file://server/share/…（ネットワーク）は何もしない。あるかどうかも確かめない
        else if (uri.IsFile)
            _vm?.OpenLocalLink(uri.LocalPath);
    }
}
