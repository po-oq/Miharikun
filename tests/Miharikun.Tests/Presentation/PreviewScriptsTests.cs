namespace Miharikun.Tests.Presentation;

public sealed class PreviewScriptsTests
{
    [Fact]
    public void ScrollToHeading_builds_a_getElementById_call()
    {
        var js = PreviewScripts.ScrollToHeading("phase-1");
        Assert.Contains("document.getElementById(\"phase-1\")", js);
        Assert.Contains("scrollIntoView()", js);
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\\b")]
    [InlineData("a'b")]
    [InlineData("</script><script>alert(1)</script>")]
    [InlineData("a\nb\r\nc")]
    [InlineData("日本語の見出し")]
    [InlineData("a\u2028b\u2029c")]
    public void ScrollToHeading_keeps_the_id_inside_a_string_literal(string id)
    {
        var js = PreviewScripts.ScrollToHeading(id);

        // 文字列リテラルの外に出る文字が、生のまま入っていない
        Assert.DoesNotContain("</script>", js, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', js);
        Assert.DoesNotContain('\r', js);
        Assert.DoesNotContain('\u2028', js);
        Assert.DoesNotContain('\u2029', js);
        var inner = js[(js.IndexOf("getElementById(\"", StringComparison.Ordinal) + "getElementById(\"".Length)..];
        inner = inner[..inner.IndexOf("\")", StringComparison.Ordinal)];
        Assert.DoesNotContain("\"", inner.Replace("\\\"", "").Replace("\\\\", ""));   // 逃がしていない " が無い
    }

    [Theory]
    [InlineData("key:Escape", true)]
    [InlineData("\"key:Escape\"", true)]
    [InlineData("key:Enter", false)]
    [InlineData("\"key:Enter\"", false)]
    [InlineData("key:escape", false)]
    [InlineData(" key:Escape", false)]
    [InlineData("\"key:Escape", false)]
    [InlineData("'key:Escape'", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEscapeMessage_accepts_only_key_Escape(string? body, bool expected) =>
        Assert.Equal(expected, PreviewScripts.IsEscapeMessage(body));
}
