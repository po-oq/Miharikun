using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;

namespace Miharikun.Views;

/// <summary>
/// 行の高さがまちまちな長い一覧（タイムライン）の、仮想化とスクロール。Avalonia 12 の <c>ListBox</c> の仮想化は、全体の高さを
/// 「作った行の高さの平均」から見積もるので、5,000 行ではスクロール位置と行の対応が崩れた（下では全体 15 万 px と見積もったが実際は
/// 40 万 px 前後で、一番下から上へ進むと位置が 0 になった時点でまだ 1,000 行以上が上にあり、最後の 1 歩で先頭へ飛んだ。バーも上下した）。
/// そこで、<b>行ごとの高さを自分で持つ</b>：まだ作っていない行は本文から見積もり、作ったら測った高さに置き換えて覚える（幅が変わるまで）。
/// 全体の高さは、その合計。測り直して高さが変わっても、いま先頭に見えている行の画面上の位置は動かさない（スクロールアンカー）。
/// <see cref="ScrollViewer"/> の中に置く（<see cref="ILogicalScrollable"/>）ので、バー・ホイール・慣性は標準のまま。
/// </summary>
public sealed class TimelinePanel : Panel, ILogicalScrollable
{
    public static readonly StyledProperty<IList?> ItemsSourceProperty =
        AvaloniaProperty.Register<TimelinePanel, IList?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<TimelinePanel, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>表示する行（<see cref="INotifyCollectionChanged"/> なら、追記に追従する）。</summary>
    public IList? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    /// <summary>1 行の見た目（DataContext に行を入れて作る）。</summary>
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    /// <summary>まだ作っていない行の高さの見積もり（行・幅 → 高さ）。近いほど、スクロールのずれが小さい。</summary>
    public Func<object, double, double>? EstimateHeight { get; set; }

    private const double FallbackWidth = 360;
    private const double Buffer = 0.5;   // 見えている範囲の前後に、この倍率ぶんも作っておく

    private IList? _items;
    private List<double> _heights = [];
    private double[] _prefix = [0];
    private bool _prefixDirty = true;
    private bool _rebuild = true;
    private double _width;
    private Vector _offset;
    private Size _extent, _viewport;
    private readonly Dictionary<int, Control> _realized = [];
    private readonly Stack<Control> _pool = new();
    private readonly ConditionalWeakTable<object, StrongBox<double>> _measured = new();
    private bool _measuring;

    public TimelinePanel()
    {
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
            AttachItems(change.NewValue as IList);
        else if (change.Property == ItemTemplateProperty)
        {
            RecycleAll();
            _pool.Clear();
            InvalidateMeasure();
        }
    }

    private void AttachItems(IList? items)
    {
        if (_items is INotifyCollectionChanged old)
            old.CollectionChanged -= OnItemsChanged;
        _items = items;
        if (_items is INotifyCollectionChanged now)
            now.CollectionChanged += OnItemsChanged;
        _rebuild = true;
        _offset = default;
        RecycleAll();
        InvalidateMeasure();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 末尾への追加だけは、いまの高さを保つ。それ以外（差し替え・並べ替え・削除）は作り直す。
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex != _heights.Count)
        {
            _rebuild = true;
            RecycleAll();
        }
        InvalidateMeasure();
    }

    // ---------------------------------------------------------------- 高さの管理

    private int Count => _items?.Count ?? 0;

    private void EnsureHeights()
    {
        var count = Count;
        if (_rebuild)
        {
            _heights = new List<double>(count);
            _rebuild = false;
        }
        while (_heights.Count < count)
            _heights.Add(HeightOf(_items![_heights.Count]!));
        if (_heights.Count > count)
            _heights.RemoveRange(count, _heights.Count - count);
        _prefixDirty = true;
    }

    /// <summary>測ったことのある行はその高さ、なければ見積もり。</summary>
    private double HeightOf(object item) =>
        _measured.TryGetValue(item, out var box) ? box.Value : Math.Max(1, EstimateHeight?.Invoke(item, _width) ?? 60);

    private void EnsurePrefix()
    {
        if (!_prefixDirty && _prefix.Length == _heights.Count + 1)
            return;
        var prefix = new double[_heights.Count + 1];
        for (var i = 0; i < _heights.Count; i++)
            prefix[i + 1] = prefix[i] + _heights[i];
        _prefix = prefix;
        _prefixDirty = false;
    }

    private double Total { get { EnsurePrefix(); return _prefix[^1]; } }

    /// <summary>行 <paramref name="index"/> の上端（一覧の先頭から）。</summary>
    public double OffsetOf(int index)
    {
        EnsurePrefix();
        return _prefix[Math.Clamp(index, 0, _heights.Count)];
    }

    /// <summary>位置 <paramref name="y"/> にある行の番号（範囲外は両端）。</summary>
    public int IndexAt(double y)
    {
        EnsurePrefix();
        if (_heights.Count == 0)
            return 0;
        var lo = 0;
        var hi = _heights.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_prefix[mid] <= y) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // ---------------------------------------------------------------- 仮想化（作る・捨てる・測る・並べる）

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_measuring)
            return availableSize;
        _measuring = true;
        try
        {
            var width = double.IsInfinity(availableSize.Width) ? FallbackWidth : availableSize.Width;
            var height = double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height;
            if (Math.Abs(width - _width) > 0.5)
            {
                // 幅が変わると、折り返しが変わって高さも変わる。測った高さを捨てて、見積もりからやり直す。
                _width = width;
                ClearMeasured();
                _rebuild = true;
                RecycleAll();
            }
            EnsureHeights();

            var maxOffset = Math.Max(0, Total - height);
            _offset = new Vector(0, Math.Clamp(_offset.Y, 0, maxOffset));

            // 先頭に見えている行の画面上の位置を覚える（測り直して高さが変わっても、ここは動かさない）
            var anchor = IndexAt(_offset.Y);
            var anchorScreenY = OffsetOf(anchor) - _offset.Y;

            for (var pass = 0; pass < 4; pass++)
            {
                var top = _offset.Y;
                var first = IndexAt(top - height * Buffer);
                var last = Count == 0 ? -1 : IndexAt(top + height * (1 + Buffer));
                var changed = false;

                foreach (var index in _realized.Keys.Where(i => i < first || i > last).ToList())
                    Recycle(index);

                for (var i = first; i <= last; i++)
                {
                    var element = Realize(i);
                    element.Measure(new Size(width, double.PositiveInfinity));
                    var measured = Math.Max(1, element.DesiredSize.Height);
                    _measured.AddOrUpdate(_items![i]!, new StrongBox<double>(measured));
                    if (Math.Abs(measured - _heights[i]) > 0.25)
                    {
                        _heights[i] = measured;
                        _prefixDirty = true;
                        changed = true;
                    }
                }
                if (!changed)
                    break;

                // 高さが変わったので、先頭の行の画面上の位置を保つように位置を直して、範囲を取り直す
                _offset = new Vector(0, Math.Clamp(OffsetOf(anchor) - anchorScreenY, 0, Math.Max(0, Total - height)));
            }

            var extent = new Size(width, Total);
            var viewport = new Size(width, height);
            var invalidated = extent != _extent || viewport != _viewport;
            _extent = extent;
            _viewport = viewport;
            if (invalidated)
                ScrollInvalidated?.Invoke(this, EventArgs.Empty);
            return new Size(width, height);
        }
        finally
        {
            _measuring = false;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, element) in _realized)
            element.Arrange(new Rect(0, OffsetOf(index) - _offset.Y, finalSize.Width, _heights[index]));
        return finalSize;
    }

    private Control Realize(int index)
    {
        if (_realized.TryGetValue(index, out var existing))
        {
            if (ReferenceEquals(existing.DataContext, _items![index]))
                return existing;
            Recycle(index);   // 同じ番号に別の行が来た
        }

        var item = _items![index]!;
        var element = _pool.Count > 0 ? _pool.Pop() : (ItemTemplate?.Build(item) ?? new TextBlock { Text = item.ToString() });
        element.DataContext = item;
        _realized[index] = element;
        if (element.Parent is null)
            Children.Add(element);
        element.IsVisible = true;
        return element;
    }

    private void Recycle(int index)
    {
        if (!_realized.Remove(index, out var element))
            return;
        element.DataContext = null;
        element.IsVisible = false;   // 捨てずに、プールへ（作り直しの重さを避ける）
        _pool.Push(element);
    }

    private void RecycleAll()
    {
        foreach (var index in _realized.Keys.ToList())
            Recycle(index);
    }

    private void ClearMeasured()
    {
        // ConditionalWeakTable は全消去が無いので、作り直す
        foreach (var item in _items ?? (IList)Array.Empty<object>())
            _measured.Remove(item!);
    }

    /// <summary>作られている行の数（テスト用）。</summary>
    public int RealizedCount => _realized.Count;

    // ---------------------------------------------------------------- スクロール（ILogicalScrollable）

    /// <summary>行 <paramref name="index"/> が見える位置まで、最小限だけスクロールする（見えていれば動かない）。</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= Count)
            return;
        EnsureHeights();
        var top = OffsetOf(index);
        var bottom = OffsetOf(index + 1);
        var y = _offset.Y;
        if (top < y)
            y = top;
        else if (bottom > y + _viewport.Height)
            y = Math.Max(top, bottom - _viewport.Height);
        Offset = new Vector(0, y);
    }

    /// <summary>一番下へ。</summary>
    public void ScrollToEnd() => Offset = new Vector(0, double.MaxValue);

    bool ILogicalScrollable.IsLogicalScrollEnabled => true;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public Size Extent => _extent;
    public Size Viewport => _viewport;
    public Size ScrollSize => new(16, 48);
    public Size PageScrollSize => new(_viewport.Width, Math.Max(48, _viewport.Height * 0.9));

    public Vector Offset
    {
        get => _offset;
        set
        {
            var y = Math.Clamp(value.Y, 0, Math.Max(0, Total - _viewport.Height));
            if (Math.Abs(y - _offset.Y) < 0.01)
                return;
            _offset = new Vector(0, y);
            InvalidateMeasure();
            ScrollInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ScrollInvalidated;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    public bool BringIntoView(Control target, Rect targetRect) => false;

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;
}
