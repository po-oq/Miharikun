using Miharikun.Core.Sessions;

namespace Miharikun.Tests.Core;

public sealed class CommitCheckTests
{
    [Fact]
    public void Git_unavailable_is_unknown()
    {
        var (level, text) = CommitCheck.Evaluate(changedFileCount: 2, uncommitted: null);
        Assert.Equal("na", level);
        Assert.Contains("不明", text);
    }

    [Fact]
    public void No_edits_in_session_is_not_applicable_even_if_nothing_uncommitted()
    {
        var (level, text) = CommitCheck.Evaluate(changedFileCount: 0, uncommitted: []);
        Assert.Equal("na", level);
        Assert.DoesNotContain("コミット済み", text);
        Assert.Contains("編集なし", text);
    }

    [Fact]
    public void Edited_and_all_committed_is_ok()
    {
        var (level, text) = CommitCheck.Evaluate(changedFileCount: 2, uncommitted: []);
        Assert.Equal("ok", level);
        Assert.Contains("コミット済み", text);
        Assert.Contains("このセッションの変更", text);
    }

    [Fact]
    public void Edited_with_uncommitted_is_ng()
    {
        var (level, text) = CommitCheck.Evaluate(changedFileCount: 2, uncommitted: ["a.cs"]);
        Assert.Equal("ng", level);
        Assert.Contains("1ファイル", text);
    }
}
