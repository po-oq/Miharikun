using System.Collections.Specialized;
using System.ComponentModel;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.ViewModels;

namespace Miharikun.Tests.Presentation;

/// <summary>39：ドキュメントタブの拡大モードと目次（Issue #28。計画 7.1・7.7）。</summary>
public sealed class DocumentsExpandViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-docexp-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly DocumentsViewModel _vm;

    public DocumentsExpandViewModelTests()
    {
        _project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(Path.Combine(_project, "docs"));
        File.WriteAllText(Path.Combine(_project, "docs", "a.md"), "# a");
        File.WriteAllText(Path.Combine(_project, "docs", "b.md"), "# b");
        File.WriteAllText(Path.Combine(_project, "docs", "page.html"), "<p>x</p>");
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        _vm = new DocumentsViewModel(_project, new ProjectSettingsStore(paths), paths, () => false,
            new ImmediateSynchronizationContext(), new FakeUiServices());
    }

    public void Dispose()
    {
        _vm.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void Open(string rel)
    {
        _vm.Start();
        Assert.True(SpinWait.SpinUntil(() => !_vm.IsScanning, 10000), "走査が終わらない");
        _vm.OpenFromOutside(rel);
        Assert.NotNull(_vm.CurrentTarget);
    }

    private static OutlineHeading H(int level, string text, bool? done = null, int d = 0, int t = 0, string? id = null) =>
        new(level, text, id ?? text.ToLowerInvariant(), done, d, t);

    private void Feed(params OutlineHeading[] headings) => _vm.OnOutline(_vm.CurrentTarget, headings);

    private static string[] Texts(DocumentsViewModel vm) => vm.Outline.Select(i => i.DisplayText).ToArray();

    // ── 拡大の状態（7.1）─────────────────────────────────────────

    [Fact]
    public void Starts_collapsed_and_cannot_expand_without_a_selection()
    {
        Assert.False(_vm.IsExpanded);
        Assert.False(_vm.ToggleExpandedCommand.CanExecute(null));

        _vm.ToggleExpandedCommand.Execute(null);

        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public void Can_expand_once_a_file_is_selected_and_toggle_goes_back()
    {
        Open("docs/a.md");
        Assert.True(_vm.ToggleExpandedCommand.CanExecute(null));

        _vm.ToggleExpandedCommand.Execute(null);
        Assert.True(_vm.IsExpanded);

        _vm.ToggleExpandedCommand.Execute(null);
        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public void Outline_column_is_visible_only_when_expanded_on_a_markdown_file()
    {
        Open("docs/a.md");
        Assert.False(_vm.IsOutlineVisible);

        _vm.ToggleExpandedCommand.Execute(null);
        Assert.True(_vm.IsOutlineVisible);

        _vm.OpenFromOutside("docs/page.html");
        Assert.True(_vm.IsExpanded);
        Assert.False(_vm.IsOutlineVisible);

        _vm.OpenFromOutside("docs/b.md");
        Assert.True(_vm.IsExpanded);
        Assert.True(_vm.IsOutlineVisible);
    }

    [Fact]
    public void Expanding_and_selecting_notify_IsExpanded_and_IsOutlineVisible()
    {
        Open("docs/a.md");
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string?>();   // 概要の読み込み（背景）からも通知が来る
        ((INotifyPropertyChanged)_vm).PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        _vm.ToggleExpandedCommand.Execute(null);
        Assert.Contains(nameof(DocumentsViewModel.IsExpanded), changed);
        Assert.Contains(nameof(DocumentsViewModel.IsOutlineVisible), changed);

        changed.Clear();
        _vm.OpenFromOutside("docs/page.html");
        Assert.Contains(nameof(DocumentsViewModel.IsOutlineVisible), changed);
    }

    [Fact]
    public void Collapse_returns_true_only_when_it_actually_collapsed()
    {
        Open("docs/a.md");
        Assert.False(_vm.Collapse());                      // 通常のときの Esc は何もしない

        _vm.ToggleExpandedCommand.Execute(null);
        Assert.True(_vm.Collapse());
        Assert.False(_vm.IsExpanded);

        Assert.False(_vm.Collapse());                      // 2 回目も戻すだけ（切り替えない）
        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public void Escape_from_the_page_collapses_but_never_expands()
    {
        Open("docs/a.md");
        _vm.OnPageEscape();
        Assert.False(_vm.IsExpanded);

        _vm.ToggleExpandedCommand.Execute(null);
        _vm.OnPageEscape();
        Assert.False(_vm.IsExpanded);

        _vm.OnPageEscape();
        Assert.False(_vm.IsExpanded);
    }

    [Fact]
    public void Collapsing_keeps_the_open_file()
    {
        Open("docs/a.md");
        _vm.ToggleExpandedCommand.Execute(null);

        _vm.Collapse();

        Assert.Equal("docs/a.md", _vm.CurrentTarget!.RelativePath);
        Assert.True(_vm.HasSelection);
    }

    [Fact]
    public void Collapses_when_the_open_file_disappears()
    {
        Open("docs/a.md");
        _vm.ToggleExpandedCommand.Execute(null);

        File.Delete(Path.Combine(_project, "docs", "a.md"));
        _vm.Rescan();
        // IsScanning が false になった後に選択が解かれるので、結果そのものを待つ
        Assert.True(SpinWait.SpinUntil(() => !_vm.IsScanning && _vm.CurrentTarget is null, 10000), "選択が解かれない");

        Assert.Null(_vm.CurrentTarget);
        Assert.False(_vm.IsExpanded);
        Assert.False(_vm.ToggleExpandedCommand.CanExecute(null));
    }

    [Fact]
    public void Stays_expanded_through_a_reload_and_a_theme_change()
    {
        Open("docs/a.md");
        _vm.ToggleExpandedCommand.Execute(null);

        _vm.ReloadPreviewCommand.Execute(null);
        _vm.OnThemeChanged();

        Assert.True(_vm.IsExpanded);
    }

    // ── 目次（7.2・7.7）──────────────────────────────────────────

    [Fact]
    public void Outline_items_show_the_mark_text_and_task_counts()
    {
        Open("docs/a.md");
        Feed(H(1, "計画"), H(2, "済み", true, 2, 5), H(2, "未了", false), H(2, "印なし", null, 0, 0));

        Assert.Equal(["計画", "✅ 済み", "⬜ 未了", "印なし"], Texts(_vm));
        Assert.Equal(["", "2/5", "", ""], _vm.Outline.Select(i => i.TaskText).ToArray());
    }

    [Fact]
    public void Depth_is_relative_to_the_shallowest_level_in_the_document()
    {
        Open("docs/a.md");
        Feed(H(2, "x"), H(3, "y"), H(4, "z"), H(2, "w"));

        Assert.Equal([0, 1, 2, 0], _vm.Outline.Select(i => i.Depth).ToArray());
    }

    [Fact]
    public void Progress_counts_only_headings_with_a_mark()
    {
        Open("docs/a.md");
        Feed(H(2, "a", true), H(2, "b", true), H(2, "c", false), H(2, "d"), H(3, "e", false, 1, 3));

        Assert.True(_vm.HasProgress);
        Assert.Equal("✅ 2 / 4", _vm.ProgressText);
    }

    [Fact]
    public void No_progress_without_any_marked_heading()
    {
        Open("docs/a.md");
        Feed(H(2, "a", null, 1, 2), H(2, "b"));

        Assert.False(_vm.HasProgress);
        Assert.Equal("", _vm.ProgressText);
    }

    [Fact]
    public void Outline_is_not_reported_empty_until_the_result_arrives_and_is_empty_with_no_headings()
    {
        Open("docs/a.md");
        Assert.False(_vm.IsOutlineEmpty);

        Feed();

        Assert.True(_vm.IsOutlineEmpty);

        Feed(H(1, "x"));
        Assert.False(_vm.IsOutlineEmpty);
    }

    [Fact]
    public void An_unchanged_outline_keeps_the_same_items_and_raises_nothing()
    {
        Open("docs/a.md");
        Feed(H(1, "a"), H(2, "b", true, 1, 2));
        var items = _vm.Outline.ToArray();
        var collection = _vm.Outline;
        var collectionEvents = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => collectionEvents.Add(e);
        var itemEvents = new List<string?>();
        foreach (var i in items)
            i.PropertyChanged += (_, e) => itemEvents.Add(e.PropertyName);

        Feed(H(1, "a"), H(2, "b", true, 1, 2));

        Assert.Same(collection, _vm.Outline);
        Assert.Equal(items, _vm.Outline.ToArray());
        Assert.Empty(collectionEvents);
        Assert.Empty(itemEvents);
    }

    [Fact]
    public void A_changed_heading_is_updated_in_place_and_only_what_changed_is_notified()
    {
        Open("docs/a.md");
        Feed(H(1, "a"), H(2, "b", false, 0, 2));
        var second = _vm.Outline[1];
        var collectionEvents = 0;
        _vm.Outline.CollectionChanged += (_, _) => collectionEvents++;
        var changed = new List<string?>();
        second.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Feed(H(1, "a"), H(2, "b", true, 1, 2));

        Assert.Same(second, _vm.Outline[1]);
        Assert.Equal(0, collectionEvents);
        Assert.Equal("✅ b", second.DisplayText);
        Assert.Equal("1/2", second.TaskText);
        Assert.Contains(nameof(OutlineItemViewModel.DisplayText), changed);
        Assert.Contains(nameof(OutlineItemViewModel.TaskText), changed);
        Assert.DoesNotContain(nameof(OutlineItemViewModel.Depth), changed);
    }

    [Fact]
    public void Added_and_removed_headings_are_appended_or_trimmed_at_the_end_without_Clear()
    {
        Open("docs/a.md");
        Feed(H(1, "a"), H(2, "b"));
        var first = _vm.Outline[0];
        var actions = new List<NotifyCollectionChangedAction>();
        _vm.Outline.CollectionChanged += (_, e) => actions.Add(e.Action);

        Feed(H(1, "a"), H(2, "b"), H(2, "c"));
        Assert.Equal(["a", "b", "c"], Texts(_vm));
        Assert.Same(first, _vm.Outline[0]);

        Feed(H(1, "a"));
        Assert.Equal(["a"], Texts(_vm));
        Assert.Same(first, _vm.Outline[0]);

        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void A_result_for_another_file_is_discarded()
    {
        Open("docs/a.md");
        var stale = _vm.CurrentTarget;
        _vm.OpenFromOutside("docs/b.md");

        _vm.OnOutline(stale, [H(1, "古い")]);

        Assert.Empty(_vm.Outline);
        Assert.False(_vm.IsOutlineEmpty);
    }

    [Fact]
    public void A_result_with_no_source_is_discarded()
    {
        Open("docs/a.md");

        _vm.OnOutline(null, [H(1, "x")]);

        Assert.Empty(_vm.Outline);
    }

    [Fact]
    public void Switching_to_another_file_clears_the_outline_at_once()
    {
        Open("docs/a.md");
        Feed(H(1, "a"), H(2, "b", true));

        _vm.OpenFromOutside("docs/b.md");

        Assert.Empty(_vm.Outline);
        Assert.False(_vm.HasProgress);
        Assert.Equal("", _vm.ProgressText);
        Assert.False(_vm.IsOutlineEmpty);
    }

    [Fact]
    public void Reload_and_theme_change_keep_the_outline()
    {
        Open("docs/a.md");
        Feed(H(1, "a"), H(2, "b"));
        var items = _vm.Outline.ToArray();

        _vm.ReloadPreviewCommand.Execute(null);
        _vm.OnThemeChanged();

        Assert.Equal(items, _vm.Outline.ToArray());
    }

    [Fact]
    public void Selecting_the_same_file_again_keeps_the_outline()
    {
        Open("docs/a.md");
        Feed(H(1, "a"));

        _vm.OpenFromOutside("docs/a.md");

        Assert.Single(_vm.Outline);
    }

    [Fact]
    public void A_link_to_an_html_file_empties_the_outline_and_an_empty_result_keeps_it_empty()
    {
        Open("docs/a.md");
        _vm.ToggleExpandedCommand.Execute(null);
        Feed(H(1, "a"));

        _vm.OpenLocalLink(Path.Combine(_project, "docs", "page.html"));
        Feed();

        Assert.True(_vm.IsExpanded);
        Assert.Empty(_vm.Outline);
        Assert.False(_vm.IsOutlineVisible);
    }

    [Fact]
    public void The_outline_is_emptied_when_the_selection_is_lost()
    {
        Open("docs/a.md");
        Feed(H(1, "a"));

        File.Delete(Path.Combine(_project, "docs", "a.md"));
        _vm.Rescan();
        // IsScanning が false になった後に選択が解かれるので、結果そのものを待つ
        Assert.True(SpinWait.SpinUntil(() => !_vm.IsScanning && _vm.CurrentTarget is null, 10000), "選択が解かれない");

        Assert.Empty(_vm.Outline);
    }

    // ── 見出しへ移る（7.3）─────────────────────────────────────────

    [Fact]
    public void Jumping_raises_the_heading_id_every_time_even_for_the_same_item()
    {
        Open("docs/a.md");
        Feed(H(1, "a", id: "id-a"), H(2, "b", id: "id-b"));
        var requested = new List<string>();
        _vm.HeadingScrollRequested += requested.Add;

        _vm.JumpToHeadingCommand.Execute(_vm.Outline[1]);
        _vm.JumpToHeadingCommand.Execute(_vm.Outline[1]);
        _vm.JumpToHeadingCommand.Execute(_vm.Outline[0]);

        Assert.Equal(["id-b", "id-b", "id-a"], requested);
    }

    [Fact]
    public void Jumping_to_an_item_without_an_id_or_with_nothing_does_nothing()
    {
        Open("docs/a.md");
        _vm.OnOutline(_vm.CurrentTarget, [new OutlineHeading(1, "x", null, null, 0, 0)]);
        var requested = new List<string>();
        _vm.HeadingScrollRequested += requested.Add;

        _vm.JumpToHeadingCommand.Execute(_vm.Outline[0]);
        _vm.JumpToHeadingCommand.Execute(null);

        Assert.Empty(requested);
    }

    [Fact]
    public void Selecting_an_outline_row_by_itself_does_not_scroll()
    {
        Open("docs/a.md");
        Feed(H(1, "a"));
        var requested = new List<string>();
        _vm.HeadingScrollRequested += requested.Add;

        Feed(H(1, "a"), H(2, "b"));                         // 作り直しで目次がずれても、プレビューは動かさない

        Assert.Empty(requested);
    }
}
