using System.Collections.Specialized;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.ViewModels;

namespace Miharikun.Tests.Presentation;

/// <summary>28-3：一覧（絞り込み・並び・選択の保ち方）・時計・git・「Hook なし」の帯。</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly MainVmHarness _h = new();

    private static readonly DateTimeOffset T = MainVmHarness.Now;

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    // ---------------------------------------------------------------- 一覧：並び・絞り込み

    [Fact]
    public void Cards_are_listed_newest_activity_first_and_ties_keep_arrival_order()
    {
        _h.Add(_h.Snap("old", "古い依頼", T.AddMinutes(-30)),
               _h.Snap("new", "新しい依頼", T.AddMinutes(-1)),
               _h.Snap("tieA", "同じ時刻 A", T.AddMinutes(-10)),
               _h.Snap("tieB", "同じ時刻 B", T.AddMinutes(-10)));

        Assert.Equal("new,tieA,tieB,old", _h.Order());
    }

    [Fact]
    public void Search_filters_by_every_prompt_title_summary_and_memo()
    {
        _h.Add(_h.Snap("a", "ログインの修正", T.AddMinutes(-3)), _h.Snap("b", "ほかの作業", T.AddMinutes(-2)));
        _h.Meta.Update(_h.Card("b").Key, (m, at) => m.WithMemo("ログイン周りのメモ", at));

        Vm.SearchText = "ログイン";

        Assert.Equal("b,a", _h.Order());   // b はメモに含む
        Vm.SearchText = "修正";
        Assert.Equal("a", _h.Order());
        Vm.SearchText = "";
        Assert.Equal("b,a", _h.Order());
    }

    [Fact]
    public void RunningOnly_MemoOnly_and_UncommittedOnly_filter_the_list()
    {
        var edited = Path.Combine(_h.Project, "a.txt");
        _h.GitStatus = MainVmHarness.Dirty(_h.Project, edited);
        _h.Add(_h.Snap("run", "実行中の依頼", T, running: true),
               _h.Snap("dirty", "ファイルを編集", T.AddMinutes(-5), editedFile: edited),
               _h.Snap("memo", "メモあり", T.AddMinutes(-6)));
        _h.Meta.Update(_h.Card("memo").Key, (m, at) => m.WithMemo("メモ", at));
        Assert.True(SpinWait.SpinUntil(() => _h.Card("dirty").UncommittedCount == 1, 5000));   // Add で始まる背景の git の更新を待つ

        Vm.RunningOnly = true;
        Assert.Equal("run", _h.Order());
        Vm.RunningOnly = false;

        Vm.MemoOnly = true;
        Assert.Equal("memo", _h.Order());
        Vm.MemoOnly = false;

        Vm.UncommittedOnly = true;
        Assert.Equal("dirty", _h.Order());
        Vm.UncommittedOnly = false;
        Assert.Equal("run,dirty,memo", _h.Order());
    }

    [Fact]
    public void Status_chips_are_OR_and_counts_come_from_all_cards()
    {
        _h.Add(_h.Snap("a", "A", T.AddMinutes(-1)), _h.Snap("b", "B", T.AddMinutes(-2)), _h.Snap("c", "C", T.AddMinutes(-3)));
        _h.Meta.Update(_h.Card("a").Key, (m, at) => m.WithStatus(SessionStatus.Working, at));
        _h.Meta.Update(_h.Card("b").Key, (m, at) => m.WithStatus(SessionStatus.Done, at));

        var chip = (StatusTab t) => Vm.StatusTabs.Single(x => x.Tab == t);
        chip(StatusTab.Working).IsChecked = true;
        Assert.Equal("a", _h.Order());
        chip(StatusTab.Done).IsChecked = true;
        Assert.Equal("a,b", _h.Order());   // 複数選ぶと OR
        Assert.False(chip(StatusTab.All).IsChecked);

        Vm.SearchText = "C";   // 件数は絞り込み・検索に関係なく全カードから数える
        Assert.Equal(1, chip(StatusTab.Working).Count);
        Assert.Equal(1, chip(StatusTab.Done).Count);
        Assert.Equal(1, chip(StatusTab.Unset).Count);

        chip(StatusTab.All).IsChecked = true;   // 「全て」を押すと他を外す
        Assert.False(chip(StatusTab.Working).IsChecked);
        Assert.False(chip(StatusTab.Done).IsChecked);
    }

    [Fact]
    public void Agent_chip_is_a_single_choice_and_counts_come_from_all_cards()
    {
        _h.Add(_h.Snap("c1", "Cursor の依頼", T.AddMinutes(-1), agent: "cursor"), _h.Snap("k1", "Claude の依頼", T.AddMinutes(-2)));

        Vm.AgentKey = "claude";
        Assert.Equal("k1", _h.Order());
        Vm.AgentKey = "cursor";
        Assert.Equal("c1", _h.Order());
        Assert.Equal(1, Vm.AgentTabs.Single(t => t.Key == "cursor").Count);
        Assert.Equal(2, Vm.AgentTabs.Single(t => t.Key == "").Count);
    }

    // ---------------------------------------------------------------- 一覧：選択の保ち方

    private List<NotifyCollectionChangedEventArgs> Watch()
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        Vm.VisibleCards.CollectionChanged += (_, e) => events.Add(e);
        return events;
    }

    [Fact]
    public void A_selected_card_that_moves_up_keeps_the_selection_and_is_not_moved()
    {
        _h.Add(_h.Snap("a", "A", T.AddMinutes(-1)), _h.Snap("b", "B", T.AddMinutes(-2)), _h.Snap("c", "C", T.AddMinutes(-3)));
        Vm.Selected = _h.Card("c");
        var events = Watch();

        _h.Add(_h.Snap("c", "C", T));   // c が一番新しくなる

        Assert.Equal("c,a,b", _h.Order());
        Assert.Same(_h.Card("c"), Vm.Selected);
        Assert.NotNull(Vm.Detail);
        var c = _h.Card("c");
        Assert.All(events, e =>
        {
            Assert.NotEqual(NotifyCollectionChangedAction.Reset, e.Action);
            Assert.DoesNotContain(c, (e.OldItems ?? Array.Empty<object>()).Cast<SessionCardViewModel>());
            Assert.DoesNotContain(c, (e.NewItems ?? Array.Empty<object>()).Cast<SessionCardViewModel>());
        });
    }

    [Fact]
    public void A_selected_card_pushed_down_by_another_one_keeps_the_selection()
    {
        _h.Add(_h.Snap("a", "A", T.AddMinutes(-1)), _h.Snap("b", "B", T.AddMinutes(-2)));
        Vm.Selected = _h.Card("a");

        _h.Add(_h.Snap("b", "B", T));

        Assert.Equal("b,a", _h.Order());
        Assert.Same(_h.Card("a"), Vm.Selected);
    }

    [Fact]
    public void A_selected_card_that_gets_hidden_clears_the_selection_detail_timeline_search_and_expansion()
    {
        _h.Add(_h.Snap("a", "ログイン", T.AddMinutes(-1)), _h.Snap("b", "ほかの作業", T.AddMinutes(-2)));
        Vm.Selected = _h.Card("a");
        Vm.Timeline.SearchText = "返事";
        Vm.ToggleTimelineExpandedCommand.Execute(null);
        Assert.True(Vm.IsTimelineExpanded);

        Vm.SearchText = "ほか";   // a が隠れる

        Assert.Equal("b", _h.Order());
        Assert.Null(Vm.Selected);
        Assert.Null(Vm.Detail);
        Assert.Empty(Vm.Timeline.VisibleItems);
        Assert.Equal("", Vm.Timeline.SearchText);
        Assert.False(Vm.IsTimelineExpanded);
    }

    [Fact]
    public void The_list_control_clearing_the_selection_while_the_list_is_synced_is_ignored_when_the_card_stays()
    {
        // 画面の一覧が Move・Remove で選択を外す（Avalonia の ListBox）のを真似る：合わせている間に Selected = null が来る
        _h.Add(_h.Snap("a", "A", T.AddMinutes(-1)), _h.Snap("b", "B", T.AddMinutes(-2)), _h.Snap("c", "C", T.AddMinutes(-3)));
        Vm.Selected = _h.Card("b");
        var changed = new List<string?>();
        Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Vm.VisibleCards.CollectionChanged += (_, _) => Vm.Selected = null;

        _h.Add(_h.Snap("c", "C", T));   // c が上へ移る（b は残る）

        Assert.Equal("c,a,b", _h.Order());
        Assert.Same(_h.Card("b"), Vm.Selected);   // 選択は外れない
        Assert.NotNull(Vm.Detail);
        Assert.Contains(nameof(MainViewModel.Selected), changed);   // 画面の一覧が選び直せるよう、通知は出る
    }

    [Fact]
    public void The_list_control_clearing_the_selection_while_the_list_is_synced_still_clears_when_the_card_is_hidden()
    {
        _h.Add(_h.Snap("a", "ログイン", T.AddMinutes(-1)), _h.Snap("b", "ほかの作業", T.AddMinutes(-2)));
        Vm.Selected = _h.Card("a");
        Vm.VisibleCards.CollectionChanged += (_, _) => Vm.Selected = null;

        Vm.SearchText = "ほか";

        Assert.Null(Vm.Selected);
        Assert.Null(Vm.Detail);
    }

    [Fact]
    public void Removing_the_selected_session_clears_the_selection()
    {
        _h.Add(_h.Snap("a", "A", T.AddMinutes(-1)), _h.Snap("b", "B", T.AddMinutes(-2)));
        Vm.Selected = _h.Card("a");
        var a = _h.Card("a").Key;

        _h.Remove(a);

        Assert.Null(Vm.Selected);
        Assert.Equal("b", _h.Order());
    }

    [Fact]
    public void Selecting_a_recent_row_of_a_hidden_card_removes_the_filters_and_selects_it()
    {
        _h.Add(_h.Snap("a", "ログイン", T.AddMinutes(-1)), _h.Snap("b", "ほかの作業", T.AddMinutes(-2)));
        Vm.SearchText = "ほか";
        Vm.AgentKey = "claude";
        Assert.Equal("b", _h.Order());
        var scrolled = new List<SessionCardViewModel>();
        Vm.CardScrollRequested += scrolled.Add;

        Vm.SelectRecentCommand.Execute(new RecentRow(_h.Card("a").Key, "ログイン", "1分前"));

        Assert.Equal("", Vm.SearchText);
        Assert.Equal("", Vm.AgentKey);
        Assert.Equal("a,b", _h.Order());
        Assert.Same(_h.Card("a"), Vm.Selected);
        Assert.Same(_h.Card("a"), Assert.Single(scrolled));
    }

    [Fact]
    public void Selecting_a_card_shows_its_detail_and_loads_its_commits_in_the_background()
    {
        _h.Commits = [new Miharikun.Core.Git.GitCommit("abc1234", "コミットです")];
        _h.Add(_h.Snap("a", "A の依頼", T.AddMinutes(-1)));

        Vm.Selected = _h.Card("a");

        Assert.NotNull(Vm.Detail);
        Assert.Equal(_h.Card("a").Key, Vm.Detail!.Key);
        Assert.True(SpinWait.SpinUntil(() => _h.CommitCalls >= 1, 5000));
    }

    // ---------------------------------------------------------------- 時計・停止

    [Fact]
    public void The_clock_is_a_one_second_timer_that_starts_on_StartClock_and_stops_on_Dispose()
    {
        Assert.DoesNotContain(_h.Ui.Timers, t => t.Interval == TimeSpan.FromSeconds(1));

        Vm.StartClock();
        var clock = _h.Ui.TimerOf(TimeSpan.FromSeconds(1));
        Assert.True(clock.IsEnabled);

        Vm.StartClock();   // 2 回呼んでも 1 つ
        Assert.Single(_h.Ui.Timers, t => t.Interval == TimeSpan.FromSeconds(1));

        Vm.Dispose();
        Assert.False(clock.IsEnabled);
    }

    [Fact]
    public void A_running_card_that_goes_quiet_turns_to_stopped_on_a_tick_and_leaves_the_RunningOnly_list()
    {
        using var h = new MainVmHarness(runningTimeoutMinutes: 1);
        // 59.7 秒前の入力：作った直後はまだ実行中。0.5 秒待てば 1 分を越え、次の 1 秒の更新で停止に変わる（時間は進むだけなので、順序は変わらない）
        h.Add(h.Snap("run", "実行中の依頼", DateTimeOffset.Now.AddSeconds(-59.7), running: true));
        h.Vm.RunningOnly = true;
        h.Vm.StartClock();
        h.Vm.Selected = h.Card("run");
        Assert.Equal(SessionState.Running, h.Card("run").State);
        Assert.Equal("run", h.Order());

        Thread.Sleep(500);
        h.Ui.TimerOf(TimeSpan.FromSeconds(1)).Fire();

        Assert.NotEqual(SessionState.Running, h.Card("run").State);
        Assert.Equal("", h.Order());
        Assert.Null(h.Vm.Selected);
        Assert.Equal(0, h.Vm.Counts.Single(c => c.State == SessionState.Running).Count);
    }

    [Fact]
    public void A_stopped_selected_card_leaves_the_RunningOnly_list_and_clears_the_selection_when_the_timeout_shrinks()
    {
        using var h = new MainVmHarness(runningTimeoutMinutes: 10);
        h.Add(h.Snap("run", "実行中の依頼", DateTimeOffset.Now.AddMinutes(-3), running: true));
        h.Vm.RunningOnly = true;
        h.Vm.Selected = h.Card("run");
        Assert.Equal("run", h.Order());

        h.Vm.SetRunningTimeout(1);   // 3 分動きなし → 停止（絞り込みと件数も取り直す）

        Assert.Equal("", h.Order());
        Assert.Null(h.Vm.Selected);
        Assert.Null(h.Vm.Detail);
        Assert.Equal(0, h.Vm.Counts.Single(c => c.State == SessionState.Running).Count);
    }

    [Fact]
    public void Git_is_refreshed_every_five_ticks_and_the_card_count_follows()
    {
        var edited = Path.Combine(_h.Project, "a.txt");
        _h.GitStatus = MainVmHarness.Dirty(_h.Project);   // いまは未コミットなし
        _h.Add(_h.Snap("dirty", "ファイルを編集", T.AddMinutes(-5), editedFile: edited));
        Assert.True(SpinWait.SpinUntil(() => _h.Card("dirty").UncommittedCount == 0, 5000));   // Add で始まる背景の git の更新を待つ
        Assert.Equal(0, _h.Card("dirty").UncommittedCount);
        Vm.StartClock();
        var clock = _h.Ui.TimerOf(TimeSpan.FromSeconds(1));
        var before = _h.GitStatusCalls;

        _h.GitStatus = MainVmHarness.Dirty(_h.Project, edited);
        for (var i = 0; i < 4; i++) clock.Fire();
        Assert.Equal(before, _h.GitStatusCalls);   // 5 回目までは見直さない

        clock.Fire();
        Assert.True(SpinWait.SpinUntil(() => _h.GitStatusCalls == before + 1, 3000));
        Assert.True(SpinWait.SpinUntil(() => _h.Card("dirty").UncommittedCount == 1, 3000));
    }

    [Fact]
    public async Task Git_refresh_does_not_overlap_and_a_failure_is_logged_not_thrown()
    {
        // 実際に背景で動かす（重なりを止める動きは、ほかのテストの同期実行では確かめられない）。一覧は触らないので競合しない。
        using var h = new MainVmHarness(background: new Miharikun.Services.ThreadPoolRunner());
        var Vm = h.Vm;
        var gate = new TaskCompletionSource();
        var calls = 0;
        h.GitStatusOverride = () =>
        {
            Interlocked.Increment(ref calls);
            gate.Task.Wait();
            return null;
        };

        var first = Vm.RefreshGitAsync();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, 3000));
        await Vm.RefreshGitAsync();   // 実行中なら何もしない
        Assert.Equal(1, Volatile.Read(ref calls));
        gate.SetResult();
        await first;

        h.GitStatusOverride = () => throw new InvalidOperationException("壊れた");
        await Vm.RefreshGitAsync();
        Assert.Contains(h.Log, l => l.Contains("git の更新に失敗") && l.Contains("壊れた"));
    }

    [Fact]
    public async Task Commits_are_loaded_again_only_when_the_range_changes_and_a_failure_is_logged()
    {
        _h.Add(_h.Snap("a", "A", T.AddMinutes(-1)));
        var summary = _h.Card("a").Snapshot.Summary;
        Vm.Selected = _h.Card("a");
        Assert.True(SpinWait.SpinUntil(() => _h.CommitCalls == 1, 5000));   // 選んだとき読む

        await Vm.LoadCommitsAsync(summary);   // 同じ範囲
        Assert.Equal(1, _h.CommitCalls);

        var later = _h.Snap("a", "A", T).Summary;   // head を持たないセッションは、最後の動きが変わると範囲も変わる
        await Vm.LoadCommitsAsync(later);
        Assert.Equal(2, _h.CommitCalls);

        _h.Commits = null;   // 取得できなくても例外にしない
        var failing = _h.Snap("a", "A", T.AddMinutes(1)).Summary;
        await Vm.LoadCommitsAsync(failing);
        Assert.Equal(3, _h.CommitCalls);
    }

    // ---------------------------------------------------------------- 「Hook なし」の帯

    [Fact]
    public void The_hook_warning_counts_NoHook_sessions_from_all_cards_and_has_its_own_text()
    {
        Assert.False(Vm.HasNoHook);

        var key = MainVmHarness.KeyOf("nohook", "cursor");
        var events = new List<Miharikun.Core.Agents.AgentEvent>
        {
            new(key, 1, T, Miharikun.Core.Agents.AgentEventKind.PromptSubmitted, Text: "依頼", Imported: true, HookMissing: true),
        };
        var summary = SessionAnalyzer.Analyze(key, events);
        _h.Add(new SessionSnapshot(summary, "依頼"), _h.Snap("k", "普通のセッション", T.AddMinutes(-1)));

        Assert.Equal(1, Vm.NoHookCount);
        Assert.True(Vm.HasNoHook);
        Assert.Contains("Hook なし 1 件", Vm.HookWarningText);
        Assert.DoesNotContain(Vm.Counts, c => c.State == SessionState.NoHook);   // どの状態の件数にも入れない

        Vm.SearchText = "普通";   // 絞り込み・検索に関係なく数える
        Assert.Equal(1, Vm.NoHookCount);

        _h.Remove(key);
        Assert.False(Vm.HasNoHook);
    }

    [Fact]
    public void OpenHookErrorLog_is_disabled_without_the_file_and_opens_it_with_the_default_app()
    {
        var log = Path.Combine(_h.Dir, "hook-error.log");
        using var h = new MainVmHarness(hookErrorLogPath: log);
        Assert.False(h.Vm.OpenHookErrorLogCommand.CanExecute(null));

        File.WriteAllText(log, "error");
        h.Vm.OpenHookErrorLogCommand.NotifyCanExecuteChanged();
        Assert.True(h.Vm.OpenHookErrorLogCommand.CanExecute(null));

        h.Vm.OpenHookErrorLogCommand.Execute(null);
        Assert.Equal([log], h.Ui.Opened);
    }

    [Fact]
    public void A_tick_makes_the_log_button_available_when_the_log_appears_later_while_NoHook_is_shown()
    {
        var log = Path.Combine(_h.Dir, "late-hook-error.log");
        using var h = new MainVmHarness(hookErrorLogPath: log);
        var key = MainVmHarness.KeyOf("nohook", "cursor");
        var events = new List<Miharikun.Core.Agents.AgentEvent>
        {
            new(key, 1, T, Miharikun.Core.Agents.AgentEventKind.PromptSubmitted, Text: "依頼", Imported: true, HookMissing: true),
        };
        h.Add(new SessionSnapshot(SessionAnalyzer.Analyze(key, events), "依頼"));
        Assert.True(h.Vm.HasNoHook);
        Assert.False(h.Vm.OpenHookErrorLogCommand.CanExecute(null));

        File.WriteAllText(log, "late");
        h.Vm.Tick();

        Assert.True(h.Vm.OpenHookErrorLogCommand.CanExecute(null));
    }
}
