using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

public sealed class TextPreviewTests
{
    [Fact]
    public void Short_text_is_shown_as_is()
    {
        Assert.Equal(("短い返事です。", false), TextPreview.Make("短い返事です。"));
        Assert.Equal(("", false), TextPreview.Make(""));
    }

    [Fact]
    public void Text_at_exactly_the_limits_is_not_cut()
    {
        var eightLines = string.Join('\n', Enumerable.Range(1, 8).Select(i => "行" + i));
        var fiveHundred = new string('あ', 500);

        Assert.False(TextPreview.Make(eightLines).Truncated);
        Assert.False(TextPreview.Make(fiveHundred).Truncated);
    }

    [Fact]
    public void Too_many_lines_keeps_the_first_lines_and_marks_the_cut()
    {
        var text = string.Join('\n', Enumerable.Range(1, 20).Select(i => "行" + i));

        var (shown, cut) = TextPreview.Make(text);

        Assert.True(cut);
        Assert.Equal(string.Join('\n', Enumerable.Range(1, 8).Select(i => "行" + i)) + "…", shown);
    }

    [Fact]
    public void Too_many_characters_is_cut_and_marked()
    {
        var (shown, cut) = TextPreview.Make(new string('あ', 800));

        Assert.True(cut);
        Assert.Equal(new string('あ', 500) + "…", shown);
    }

    [Fact]
    public void A_surrogate_pair_is_not_split()
    {
        var text = new string('a', 499) + "😀" + "tail";

        var (shown, cut) = TextPreview.Make(text);

        Assert.True(cut);
        Assert.Equal(new string('a', 499) + "…", shown);
    }

    [Fact]
    public void Trailing_blank_before_the_mark_is_trimmed_and_limits_are_adjustable()
    {
        var (shown, _) = TextPreview.Make("abc  \n\n\n\n\nxyz", maxLines: 2);

        Assert.Equal("abc…", shown.Replace("\n", ""));
        Assert.True(TextPreview.Make("12345678", maxLines: 8, maxLength: 5).Truncated);
    }
}