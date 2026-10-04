using Miharikun.Core.Git;

namespace Miharikun.Core.Sessions;

/// <summary>セッションのコミット一覧（開始〜最後の動き）を、git で求める。</summary>
public static class SessionCommits
{
    /// <summary>
    /// 開始・最後の head を持つセッション（Cursor）は、それを使う。
    /// head を持たず、ブランチだけを持つセッション（Claude Code）は、ブランチと時刻（開始・最後の動き）から head を求める（要件 10.1）。
    /// 求められないとき（git が使えない・範囲が解決できない・ブランチが無い）は null（不明）。git を呼ぶので、背景スレッドで使う。
    /// </summary>
    public static IReadOnlyList<GitCommit>? Load(SessionSummary s, GitClient git)
    {
        var (from, to) = Range(s, git);
        return git.GetCommits(from, to);
    }

    private static (string? From, string? To) Range(SessionSummary s, GitClient git)
    {
        if (s.StartHead is not null || s.LatestHead is not null || s.Branch is null)
            return (s.StartHead, s.LatestHead);
        return (git.GetHeadAt(s.Branch, s.StartedAt), git.GetHeadAt(s.Branch, s.LastActivityAt));
    }

    /// <summary>
    /// コミット一覧を取り直す必要があるかの目印。同じ値なら、前回と同じ結果になる（git log を実行し直さない）。
    /// head を持たないセッションは、最後の動きが変わると結果も変わりうる。
    /// </summary>
    public static (string?, string?, string?, DateTimeOffset, DateTimeOffset) Key(SessionSummary s) =>
        s.StartHead is not null || s.LatestHead is not null
            ? (s.StartHead, s.LatestHead, null, default, default)
            : (null, null, s.Branch, s.StartedAt, s.LastActivityAt);
}
