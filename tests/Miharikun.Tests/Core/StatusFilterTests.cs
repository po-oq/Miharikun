using Miharikun.Core.Meta;

namespace Miharikun.Tests.Core;

public sealed class StatusFilterTests
{
    [Fact]
    public void All_matches_everything_including_unset()
    {
        foreach (SessionStatus? s in new SessionStatus?[] { null, SessionStatus.Working, SessionStatus.Paused, SessionStatus.Done })
            Assert.True(StatusFilter.Matches(StatusTab.All, s));
    }

    [Theory]
    [InlineData(StatusTab.Unset, null, true)]
    [InlineData(StatusTab.Unset, SessionStatus.Working, false)]
    [InlineData(StatusTab.Working, SessionStatus.Working, true)]
    [InlineData(StatusTab.Working, SessionStatus.Done, false)]
    [InlineData(StatusTab.Working, null, false)]
    [InlineData(StatusTab.Paused, SessionStatus.Paused, true)]
    [InlineData(StatusTab.Paused, SessionStatus.Working, false)]
    [InlineData(StatusTab.Done, SessionStatus.Done, true)]
    [InlineData(StatusTab.Done, null, false)]
    public void Each_tab_matches_only_its_status(StatusTab tab, SessionStatus? status, bool expected) =>
        Assert.Equal(expected, StatusFilter.Matches(tab, status));

    [Fact]
    public void Count_per_tab_and_all_is_the_total()
    {
        SessionStatus?[] statuses =
            [null, null, SessionStatus.Working, SessionStatus.Working, SessionStatus.Working, SessionStatus.Paused, SessionStatus.Done];

        Assert.Equal(7, StatusFilter.Count(StatusTab.All, statuses));
        Assert.Equal(2, StatusFilter.Count(StatusTab.Unset, statuses));
        Assert.Equal(3, StatusFilter.Count(StatusTab.Working, statuses));
        Assert.Equal(1, StatusFilter.Count(StatusTab.Paused, statuses));
        Assert.Equal(1, StatusFilter.Count(StatusTab.Done, statuses));
    }

    [Fact]
    public void Counts_do_not_depend_on_any_other_filter_so_the_empty_list_is_all_zero()
    {
        foreach (var tab in Enum.GetValues<StatusTab>())
            Assert.Equal(0, StatusFilter.Count(tab, []));
    }

    [Theory]
    [InlineData(StatusTab.All, "全て")]
    [InlineData(StatusTab.Unset, "未設定")]
    [InlineData(StatusTab.Working, "作業中")]
    [InlineData(StatusTab.Paused, "中断")]
    [InlineData(StatusTab.Done, "完了")]
    public void Tab_names(StatusTab tab, string expected) => Assert.Equal(expected, StatusFilter.Name(tab));
}
