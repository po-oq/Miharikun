using Miharikun.Core.Memo;

namespace Miharikun.Tests.Core;

public sealed class MemoEditorTests
{
    [Fact]
    public void Starts_in_preview_with_the_loaded_text()
    {
        var e = new MemoEditor("あ");
        Assert.Equal("あ", e.Saved);
        Assert.False(e.IsEditing);
        Assert.False(e.IsDirty);
    }

    [Fact]
    public void BeginEdit_copies_saved_into_draft_and_is_not_dirty()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        Assert.True(e.IsEditing);
        Assert.Equal("あ", e.Draft);
        Assert.False(e.IsDirty);
    }

    [Fact]
    public void Changing_the_draft_makes_it_dirty_and_restoring_it_clears_dirty()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.Draft = "あい";
        Assert.True(e.IsDirty);
        e.Draft = "あ";
        Assert.False(e.IsDirty);
    }

    [Fact]
    public void Line_ending_differences_are_not_a_change()
    {
        var e = new MemoEditor("a\nb\n");
        e.BeginEdit();
        e.Draft = "a\r\nb\r\n";
        Assert.False(e.IsDirty);
        e.Draft = "a\r\nb\r\nc";
        Assert.True(e.IsDirty);
    }

    [Fact]
    public void MarkSaved_stores_the_text_and_leaves_edit_mode()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.Draft = "い";
        e.MarkSaved("い");
        Assert.Equal("い", e.Saved);
        Assert.False(e.IsEditing);
        Assert.False(e.IsDirty);
    }

    [Fact]
    public void Draft_is_kept_as_typed_not_normalized()
    {
        var e = new MemoEditor("");
        e.BeginEdit();
        e.Draft = "a\r\nb";
        Assert.Equal("a\r\nb", e.Draft);
    }

    [Fact]
    public void Discard_without_external_change_does_not_ask_for_a_rebuild()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.Draft = "い";
        Assert.False(e.Discard());
        Assert.False(e.IsEditing);
        Assert.Equal("あ", e.Saved);
        Assert.False(e.IsDirty);
    }

    [Fact]
    public void Discard_when_not_editing_does_nothing()
    {
        var e = new MemoEditor("あ");
        Assert.False(e.Discard());
        Assert.Equal("あ", e.Saved);
    }

    [Fact]
    public void External_change_in_preview_updates_saved_and_rebuilds()
    {
        var e = new MemoEditor("あ");
        Assert.True(e.OnExternalChange("い"));
        Assert.Equal("い", e.Saved);
    }

    [Fact]
    public void External_change_with_the_same_text_in_preview_does_not_rebuild()
    {
        var e = new MemoEditor("あ");
        Assert.False(e.OnExternalChange("あ"));
    }

    [Fact]
    public void External_change_while_editing_keeps_the_draft_and_does_not_rebuild()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.Draft = "い";
        Assert.False(e.OnExternalChange("う"));
        Assert.Equal("い", e.Draft);
        Assert.True(e.IsDirty);
        Assert.Equal("う", e.Saved);
    }

    [Fact]
    public void Discard_after_an_external_change_rebuilds_and_shows_the_new_text()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.Draft = "い";
        e.OnExternalChange("う");
        Assert.True(e.Discard());
        Assert.Equal("う", e.Saved);
        Assert.False(e.IsEditing);
    }

    [Fact]
    public void Dirty_is_judged_against_the_text_at_edit_start_even_after_an_external_change()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.OnExternalChange("う");
        Assert.False(e.IsDirty);
        e.Draft = "あ！";
        Assert.True(e.IsDirty);
    }

    [Fact]
    public void Saving_after_an_external_change_wins_over_it()
    {
        var e = new MemoEditor("あ");
        e.BeginEdit();
        e.Draft = "い";
        e.OnExternalChange("う");
        e.MarkSaved("い");
        Assert.Equal("い", e.Saved);
        Assert.False(e.Discard());
    }
}
