namespace Miharikun.Tests.Presentation;

public class PreviewNavigationPolicyTests
{
    private const string Page = "/Users/x/proj/doc.html";

    [Fact]
    public void 自分で開いたページとそのページ内の移動は通す()
    {
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri("file:///Users/x/proj/doc.html"), Page, isLoadingPage: false));
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri("file:///Users/x/proj/doc.html#sec"), Page, isLoadingPage: false));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hi")]
    [InlineData("blob:null/abc")]
    public void 内部のスキームは通す(string url) =>
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri(url), Page, isLoadingPage: false));

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("mailto:a@example.com")]
    [InlineData("file:///Users/x/proj/other.md")]
    public void 読み込みが終わってからのほかへの移動は振り分ける(string url) =>
        Assert.Equal(PreviewNavigationAction.Route,
            PreviewNavigationPolicy.Decide(new Uri(url), Page, isLoadingPage: false));

    [Theory]
    [InlineData("https://example.com/embed")]
    [InlineData("file:///Users/x/proj/frame.html")]
    public void 読み込み中の_iframe_などは通す(string url) =>
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri(url), Page, isLoadingPage: true));

    [Fact]
    public void ページを開く前は_ファイルの移動もリンクとして振り分ける()
    {
        Assert.Equal(PreviewNavigationAction.Route,
            PreviewNavigationPolicy.Decide(new Uri("file:///Users/x/proj/doc.html"), null, isLoadingPage: false));
    }

    [Theory]
    [InlineData("https://a/", true)]
    [InlineData("http://a/", true)]
    [InlineData("mailto:a@b", true)]
    [InlineData("file:///a", false)]
    public void 外部のリンクの見分け(string url, bool expected) =>
        Assert.Equal(expected, PreviewNavigationPolicy.IsExternal(new Uri(url)));

    [Theory]
    [InlineData("123.5", 124)]
    [InlineData("\"42\"", 42)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("undefined", 0)]
    public void スクロール位置の読み取り(string? script, int expected) =>
        Assert.Equal(expected, PreviewNavigationPolicy.ParseScrollY(script));

    // ── md のページ：読み込み中の移動は取り消す（meta refresh などで勝手に開かない。計画 9.6）──

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("file:///Users/x/proj/run.bat")]
    [InlineData("file:///Users/x/proj/other.md")]
    public void md_の読み込み中に自分のページ以外へ移るのは取り消す(string url) =>
        Assert.Equal(PreviewNavigationAction.Cancel,
            PreviewNavigationPolicy.Decide(new Uri(url), Page, isLoadingPage: true, isMarkdownPage: true));

    [Fact]
    public void md_の読み込み中でも自分のページと内部のスキームは通す()
    {
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri("file:///Users/x/proj/doc.html"), Page, isLoadingPage: true, isMarkdownPage: true));
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri("about:blank"), Page, isLoadingPage: true, isMarkdownPage: true));
    }

    [Fact]
    public void html_の読み込み中は今までどおり通す()
    {
        Assert.Equal(PreviewNavigationAction.Allow,
            PreviewNavigationPolicy.Decide(new Uri("https://example.com/embed"), Page, isLoadingPage: true, isMarkdownPage: false));
    }

    [Fact]
    public void md_の読み込み後の移動は今までどおり振り分ける()
    {
        Assert.Equal(PreviewNavigationAction.Route,
            PreviewNavigationPolicy.Decide(new Uri("https://example.com/"), Page, isLoadingPage: false, isMarkdownPage: true));
    }
}
