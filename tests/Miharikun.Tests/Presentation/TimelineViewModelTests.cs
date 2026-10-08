using System.Collections.Specialized;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.ViewModels;

namespace Miharikun.Tests.Presentation;

/// <summary>28-3：タイムライン（種別・検索・追記・全部コピー・行のコピー・ジャンプ）。</summary>
public sealed class TimelineViewModelTests
{
    private static readonly SessionKey Key = new("claude", "s1");
    private static readonly DateTimeOffset At = new(2026, 10, 3, 11, 0, 0, TimeSpan.FromHours(9));
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.5);

    private readonly FakeUiServices _ui = new();
    private readonly TimelineViewModel _tl;

    public TimelineViewModelTests() => _tl = new TimelineViewModel(_ui);

    private static AgentEvent Ev(long seq, AgentEventKind kind, string? text = null, string? toolUseId = null, string? tool = null, string? command = null) =>
        new(Key, seq, At.AddSeconds(seq), kind, Text: text, ToolUseId: toolUseId, ToolName: tool, Command: command);

    /// <summary>依頼 → 思考 → ツール（開始・成功）→ 返事 → 終わり、を <paramref name="turns"/> 回。</summary>
    private static List<AgentEvent> Turns(int turns)
    {
        var events = new List<AgentEvent> { Ev(1, AgentEventKind.SessionStarted) };
        long seq = 1;
        for (var n = 1; n <= turns; n++)
        {
            events.Add(Ev(++seq, AgentEventKind.PromptSubmitted, $"依頼 {n}"));
            events.Add(Ev(++seq, AgentEventKind.AssistantThought, $"考え {n}"));
            events.Add(Ev(++seq, AgentEventKind.ToolStarted, toolUseId: $"t{n}", tool: "Bash", command: $"echo {n}"));
            events.Add(Ev(++seq, AgentEventKind.ToolSucceeded, toolUseId: $"t{n}", tool: "Bash"));
            events.Add(Ev(++seq, AgentEventKind.AssistantMessage, $"返事 {n}"));
            events.Add(Ev(++seq, AgentEventKind.TurnEnded));
        }
        return events;
    }

    private IEnumerable<TimelineKind> Kinds() => _tl.VisibleItems.Select(i => i.Kind);

    // ---------------------------------------------------------------- 種別・検索

    [Fact]
    public void Default_filters_show_input_and_response_but_not_thought_or_tool()
    {
        _tl.SetEvents(Turns(2), At);

        Assert.Contains(TimelineKind.Input, Kinds());
        Assert.Contains(TimelineKind.Response, Kinds());
        Assert.DoesNotContain(TimelineKind.Thought, Kinds());
        Assert.DoesNotContain(TimelineKind.Tool, Kinds());
        Assert.Contains(TimelineKind.Other, Kinds());   // セッション開始は常に表示
    }

    [Fact]
    public void Changing_a_kind_chip_replaces_the_collection_as_a_whole_with_one_notification()
    {
        _tl.SetEvents(Turns(2), At);
        var before = _tl.VisibleItems;
        var changes = 0;
        before.CollectionChanged += (_, _) => changes++;
        var replaced = 0;
        _tl.PropertyChanged += (_, e) => replaced += e.PropertyName == nameof(TimelineViewModel.VisibleItems) ? 1 : 0;

        _tl.ShowTool = true;

        Assert.NotSame(before, _tl.VisibleItems);
        Assert.Equal(0, changes);    // 1 件ずつの通知は出さない
        Assert.Equal(1, replaced);   // 入れ物ごとの差し替え 1 回
        Assert.Contains(TimelineKind.Tool, Kinds());
        _tl.ShowTool = false;
        Assert.DoesNotContain(TimelineKind.Tool, Kinds());
    }

    [Fact]
    public void Search_narrows_the_rows_and_the_count_text_shows_shown_over_total()
    {
        _tl.SetEvents(Turns(3), At);
        Assert.Equal("", _tl.CountText);

        _tl.SearchText = "依頼 2";

        Assert.Equal("1/7件", _tl.CountText.Replace(" ", ""));   // 分母は種別で絞った後：開始 1・依頼 3・返事 3
    }

    // ---------------------------------------------------------------- 追記・差し替え

    [Fact]
    public void Appending_a_few_rows_adds_to_the_same_collection_without_Reset()
    {
        _tl.SetEvents(Turns(1), At);
        var collection = _tl.VisibleItems;
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);
        var rowsBefore = collection.Count;

        _tl.SetEvents(Turns(2), At);

        Assert.Same(collection, _tl.VisibleItems);
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Add, e.Action));
        Assert.Equal(rowsBefore + 2, collection.Count);   // 依頼 2・返事 2（思考・ツールは表示しない）
        Assert.Equal("依頼 2", collection[^2].Item.Text);
    }

    [Fact]
    public void Appended_rows_that_do_not_match_the_filters_are_not_shown_until_the_filter_turns_on()
    {
        _tl.SetEvents(Turns(1), At);
        _tl.SetEvents(Turns(2), At);
        Assert.DoesNotContain(TimelineKind.Tool, Kinds());

        _tl.ShowTool = true;

        Assert.Equal(2, _tl.VisibleItems.Count(i => i.Kind == TimelineKind.Tool));
    }

    [Fact]
    public void Appending_more_than_thirty_rows_replaces_the_collection_and_keeps_the_common_rows()
    {
        _tl.SetEvents(Turns(1), At);
        var first = _tl.VisibleItems[0];
        var before = _tl.VisibleItems;

        _tl.SetEvents(Turns(20), At);   // 19 回分の追記（190 件を超える）

        Assert.NotSame(before, _tl.VisibleItems);
        Assert.Same(first, _tl.VisibleItems[0]);   // 共通の先頭は、同じ行のまま
        Assert.Equal(1 + 20 * 2, _tl.VisibleItems.Count);
    }

    [Fact]
    public void Shrinking_the_events_removes_the_tail_rows()
    {
        _tl.SetEvents(Turns(3), At);
        var collection = _tl.VisibleItems;

        _tl.SetEvents(Turns(2), At);

        Assert.Same(collection, _tl.VisibleItems);
        Assert.Equal(1 + 2 * 2, collection.Count);
    }

    [Fact]
    public void The_same_events_change_nothing()
    {
        _tl.SetEvents(Turns(2), At);
        var changes = 0;
        _tl.VisibleItems.CollectionChanged += (_, _) => changes++;
        var rows = _tl.VisibleItems.ToList();

        _tl.SetEvents(Turns(2), At);

        Assert.Equal(0, changes);
        Assert.Equal(rows, _tl.VisibleItems);
    }

    [Fact]
    public void Clear_empties_the_timeline()
    {
        _tl.SetEvents(Turns(2), At);

        _tl.Clear();

        Assert.Empty(_tl.VisibleItems);
        Assert.Equal("", _tl.CopyAllText());
    }

    // ---------------------------------------------------------------- 全部コピー

    [Fact]
    public async Task Copy_all_puts_the_shown_rows_on_the_clipboard_and_shows_the_copied_mark_for_a_while()
    {
        _tl.SetEvents(Turns(2), At);
        _tl.SearchText = "返事";

        await _tl.CopyAllCommand.ExecuteAsync(null);

        var text = Assert.Single(_ui.Clipboard);
        Assert.Equal(_tl.CopyAllText(), text);
        Assert.Contains("返事 1", text);
        Assert.Contains("返事 2", text);
        Assert.DoesNotContain("依頼", text);   // 絞った結果だけ
        Assert.True(_tl.CopiedAll);

        var timer = _ui.Timers[1];
        Assert.True(timer.IsEnabled);
        timer.Fire();
        Assert.False(_tl.CopiedAll);
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public async Task Copy_all_does_nothing_when_nothing_is_shown_or_the_clipboard_fails()
    {
        await _tl.CopyAllCommand.ExecuteAsync(null);   // 空
        Assert.Empty(_ui.Clipboard);
        Assert.False(_tl.CopiedAll);

        _tl.SetEvents(Turns(1), At);
        _ui.ClipboardSucceeds = false;
        await _tl.CopyAllCommand.ExecuteAsync(null);
        Assert.False(_tl.CopiedAll);
    }

    // ---------------------------------------------------------------- 行のコピー

    [Fact]
    public async Task Copying_a_row_shows_the_copied_mark_and_one_timer_takes_it_back()
    {
        _tl.SetEvents(Turns(2), At);
        var row = _tl.VisibleItems.First(i => i.Kind == TimelineKind.Input);

        await row.CopyCommand.ExecuteAsync(null);

        Assert.Equal([row.Item.Text], _ui.Clipboard);
        Assert.True(row.JustCopied);
        _ui.Timers[0].Fire();
        Assert.False(row.JustCopied);
    }

    [Fact]
    public async Task Copying_another_row_takes_back_the_previous_mark_at_once()
    {
        _tl.SetEvents(Turns(2), At);
        var inputs = _tl.VisibleItems.Where(i => i.Kind == TimelineKind.Input).ToList();

        await inputs[0].CopyCommand.ExecuteAsync(null);
        await inputs[1].CopyCommand.ExecuteAsync(null);

        Assert.False(inputs[0].JustCopied);
        Assert.True(inputs[1].JustCopied);
        Assert.Single(_ui.Timers, t => t.Interval == CopiedFor && t.IsEnabled);   // 行ごとのタイマーは作らない
    }

    [Fact]
    public async Task A_failed_row_copy_shows_nothing()
    {
        _tl.SetEvents(Turns(1), At);
        _ui.ClipboardSucceeds = false;
        var row = _tl.VisibleItems.First(i => i.Kind == TimelineKind.Input);

        await row.CopyCommand.ExecuteAsync(null);

        Assert.False(row.JustCopied);
    }

    [Fact]
    public async Task Copying_a_long_row_undoes_the_expansion_the_first_click_of_the_double_click_made()
    {
        var longText = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"行 {i}"));
        var events = new List<AgentEvent> { Ev(1, AgentEventKind.PromptSubmitted, longText) };
        _tl.SetEvents(events, At);
        var row = _tl.VisibleItems.Single();
        Assert.True(row.IsTruncated);

        row.ToggleExpandedCommand.Execute(null);   // ダブルクリックの 1 回目
        Assert.True(row.IsExpanded);
        await row.CopyCommand.ExecuteAsync(null);  // 2 回目

        Assert.False(row.IsExpanded);
        Assert.Equal([longText], _ui.Clipboard);   // 全文をコピーする
    }

    // ---------------------------------------------------------------- ジャンプ

    [Fact]
    public void JumpTo_turns_the_kind_filter_on_clears_a_hiding_search_highlights_and_asks_to_scroll()
    {
        _tl.SetEvents(Turns(2), At);
        var requested = new List<TimelineItemViewModel>();
        _tl.ScrollRequested += requested.Add;
        _tl.SearchText = "ない言葉";
        Assert.False(_tl.ShowTool);

        var toolSeq = Turns(2).First(e => e.Kind == AgentEventKind.ToolStarted).Seq;
        Assert.True(_tl.JumpTo(toolSeq, TimelineKind.Tool));

        Assert.True(_tl.ShowTool);
        Assert.Equal("", _tl.SearchText);
        var target = Assert.Single(requested);
        Assert.True(target.IsHighlighted);
        Assert.Contains(target, _tl.VisibleItems);

        Assert.True(_tl.JumpTo(Turns(2).First(e => e.Kind == AgentEventKind.PromptSubmitted).Seq, TimelineKind.Input));
        Assert.False(target.IsHighlighted);   // 強調は 1 つだけ
    }

    [Fact]
    public void JumpTo_returns_false_for_a_row_that_does_not_exist()
    {
        _tl.SetEvents(Turns(1), At);

        Assert.False(_tl.JumpTo(9999, TimelineKind.Input));
    }

    [Fact]
    public void ScrollToEnd_asks_to_scroll_to_the_last_shown_row()
    {
        _tl.SetEvents(Turns(2), At);
        var requested = new List<TimelineItemViewModel>();
        _tl.ScrollRequested += requested.Add;

        _tl.ScrollToEnd();

        Assert.Same(_tl.VisibleItems[^1], Assert.Single(requested));
    }

    [Fact]
    public void Dispose_releases_the_timers()
    {
        _tl.Dispose();

        Assert.All(_ui.Timers, t => Assert.True(t.Disposed));
    }
}
