using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Miharikun.Services;

namespace Miharikun.Tests.Presentation;

/// <summary>計画 7.3 の表の行ごと。Reset を出さない・同じインスタンスを使い回す・pinned（選択中）には Move も Remove も出ない。</summary>
public class ViewListTests
{
    private sealed class Item(string name)
    {
        public string Name { get; } = name;
        public override string ToString() => Name;
    }

    private static (Item[] items, ObservableCollection<Item> target, List<NotifyCollectionChangedEventArgs> events) Make(params string[] names)
    {
        var items = names.Select(n => new Item(n)).ToArray();
        var target = new ObservableCollection<Item>();
        var events = new List<NotifyCollectionChangedEventArgs>();
        target.CollectionChanged += (_, e) => events.Add(e);
        return (items, target, events);
    }

    private static string Order(IEnumerable<Item> items) => string.Concat(items.Select(i => i.Name));

    private static Item[] Pick(Item[] all, string order) => order.Select(c => all.Single(i => i.Name == c.ToString())).ToArray();

    private static void AssertPinnedUntouched(IEnumerable<NotifyCollectionChangedEventArgs> events, Item pinned)
    {
        foreach (var e in events)
        {
            Assert.NotEqual(NotifyCollectionChangedAction.Reset, e.Action);
            Assert.DoesNotContain(pinned, (e.OldItems ?? Array.Empty<object>()).Cast<Item>());
            Assert.DoesNotContain(pinned, (e.NewItems ?? Array.Empty<object>()).Cast<Item>());
        }
    }

    [Fact]
    public void Initial_fill_inserts_in_the_desired_order()
    {
        var (items, target, events) = Make("A", "B", "C");
        ViewList.SyncTo(target, items);

        Assert.Equal("ABC", Order(target));
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Add, e.Action));
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public void Filtering_one_item_out_only_removes_that_item()
    {
        var (items, target, events) = Make("A", "B", "C");
        ViewList.SyncTo(target, items);
        events.Clear();

        ViewList.SyncTo(target, [items[0], items[2]]);

        Assert.Equal("AC", Order(target));
        var removed = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removed.Action);
        Assert.Same(items[1], removed.OldItems![0]);
        Assert.Same(items[0], target[0]);   // ほかの要素は同じインスタンス
        Assert.Same(items[2], target[1]);
    }

    [Fact]
    public void Bringing_an_item_back_inserts_it_at_the_right_place()
    {
        var (items, target, events) = Make("A", "B", "C");
        ViewList.SyncTo(target, [items[0], items[2]]);
        events.Clear();

        ViewList.SyncTo(target, items);

        Assert.Equal("ABC", Order(target));
        var added = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, added.Action);
        Assert.Equal(1, added.NewStartingIndex);
        Assert.Same(items[1], added.NewItems![0]);
    }

    [Fact]
    public void Another_item_moving_up_is_one_Move_not_Remove_and_Insert()
    {
        var (items, target, events) = Make("A", "B", "C");
        ViewList.SyncTo(target, items);
        events.Clear();

        ViewList.SyncTo(target, [items[2], items[0], items[1]]);

        Assert.Equal("CAB", Order(target));
        var moved = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Move, moved.Action);
        Assert.Same(items[2], moved.NewItems![0]);
    }

    [Fact]
    public void Pinned_item_moving_up_is_not_moved_the_others_move_behind_it()
    {
        var (items, target, events) = Make("A", "B", "P");
        var pinned = items[2];
        ViewList.SyncTo(target, items, pinned);
        events.Clear();

        ViewList.SyncTo(target, [pinned, items[0], items[1]], pinned);

        Assert.Equal("PAB", Order(target));
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Move, e.Action));
        AssertPinnedUntouched(events, pinned);
    }

    [Fact]
    public void Pinned_item_moving_down_is_not_moved_either()
    {
        var (items, target, events) = Make("P", "A", "B");
        var pinned = items[0];
        ViewList.SyncTo(target, items, pinned);
        events.Clear();

        ViewList.SyncTo(target, [items[1], items[2], pinned], pinned);

        Assert.Equal("ABP", Order(target));
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Move, e.Action));
        AssertPinnedUntouched(events, pinned);
    }

    [Fact]
    public void No_change_raises_no_notification()
    {
        var (items, target, events) = Make("A", "B", "C");
        ViewList.SyncTo(target, items, items[1]);
        events.Clear();

        ViewList.SyncTo(target, items, items[1]);

        Assert.Empty(events);
        Assert.Equal("ABC", Order(target));
    }

    [Fact]
    public void Replacing_everything_uses_Remove_and_Insert_only_never_Reset()
    {
        var (items, target, events) = Make("A", "B", "C", "D");
        ViewList.SyncTo(target, [items[0], items[1]]);
        events.Clear();

        ViewList.SyncTo(target, [items[2], items[3]]);

        Assert.Equal("CD", Order(target));
        Assert.All(events, e => Assert.True(e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Add));
    }

    [Fact]
    public void Replacing_everything_keeps_pinned_when_it_stays()
    {
        var (items, target, events) = Make("A", "P", "C", "D");
        var pinned = items[1];
        ViewList.SyncTo(target, [items[0], pinned], pinned);
        events.Clear();

        ViewList.SyncTo(target, [items[2], pinned, items[3]], pinned);

        Assert.Equal("CPD", Order(target));
        AssertPinnedUntouched(events, pinned);
    }

    [Fact]
    public void Pinned_missing_from_desired_is_removed()
    {
        var (items, target, events) = Make("A", "P", "C");
        var pinned = items[1];
        ViewList.SyncTo(target, items, pinned);
        events.Clear();

        ViewList.SyncTo(target, [items[0], items[2]], pinned);

        Assert.Equal("AC", Order(target));
        var removed = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Remove, removed.Action);
        Assert.Same(pinned, removed.OldItems![0]);
    }

    [Theory]
    [InlineData("ABCDE", "EDCBA")]
    [InlineData("ABCDE", "CABED")]
    [InlineData("ABCDE", "BDF")]
    [InlineData("ABC", "DEFABC")]
    [InlineData("", "ABC")]
    [InlineData("ABC", "")]
    public void Any_desired_order_is_reached_with_or_without_a_pinned_item(string from, string to)
    {
        var all = Make("A", "B", "C", "D", "E", "F").items;
        foreach (var pinChar in new char?[] { null, 'A', 'B', 'C', 'D', 'E' })
        {
            var target = new ObservableCollection<Item>(Pick(all, from));
            var pinned = pinChar is null ? null : all.Single(i => i.Name == pinChar.ToString());
            var events = new List<NotifyCollectionChangedEventArgs>();
            target.CollectionChanged += (_, e) => events.Add(e);

            ViewList.SyncTo(target, Pick(all, to), pinned);

            Assert.Equal(to, Order(target));
            if (pinned is not null && from.Contains(pinChar!.Value) && to.Contains(pinChar.Value))
                AssertPinnedUntouched(events, pinned);
        }
    }
}
