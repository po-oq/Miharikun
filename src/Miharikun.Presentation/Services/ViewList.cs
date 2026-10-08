using System.Collections.ObjectModel;

namespace Miharikun.Services;

/// <summary>
/// 絞った一覧を、作り直さずに合わせる（計画 7.3）。<c>ICollectionView</c> の代わり。
/// 要素は同じインスタンスを使い回し、<c>Reset</c> を出さない（作り直すと <c>ListBox</c> が選択を外すため）。
/// <paramref name="pinned"/>（選択中の要素）は <c>Move</c> しない：Avalonia の <c>ListBox</c> は <c>Move</c> を
/// 「取り除く＋足す」として扱い、動かした要素が選択中なら選択を外すため。周りの要素を動かして、同じ並びにする。
/// </summary>
public static class ViewList
{
    /// <summary><paramref name="target"/> を <paramref name="desired"/> と同じ並びにする。変化が無ければ通知を出さない。</summary>
    public static void SyncTo<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, T? pinned = null) where T : class
    {
        var wanted = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);

        // 1. 要らなくなった要素を取り除く（pinned が desired に無ければ pinned も。選択が外れるのは、隠れたときの今の動きと同じ）
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
                target.RemoveAt(i);
        }

        // 2. 前から順に、desired と同じ並びにする
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], item))
                continue;

            var at = IndexOf(target, item, i + 1);
            if (at < 0)
            {
                target.Insert(i, item);
            }
            else if (ReferenceEquals(item, pinned))
            {
                // pinned は動かさない。手前にある要素を、pinned の後ろへ 1 つずつ回す（pinned が i に来るまで）
                for (var p = at; p > i; p--)
                    target.Move(p - 1, p);
            }
            else
            {
                target.Move(at, i);
            }
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> target, T item, int from) where T : class
    {
        for (var i = from; i < target.Count; i++)
        {
            if (ReferenceEquals(target[i], item))
                return i;
        }
        return -1;
    }

}
