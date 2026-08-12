using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class PageRequestTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Page_never_falls_below_one(int? requested, int expected) =>
        Assert.Equal(expected, new PageRequest(requested).Page);

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 50)]
    [InlineData(20, 20)]
    [InlineData(10_000, PageRequest.MaxPageSize)]
    public void PageSize_is_clamped_to_the_maximum(int? requested, int expected) =>
        Assert.Equal(expected, new PageRequest(1, requested).PageSize);

    [Fact]
    public void Skip_counts_whole_pages() =>
        Assert.Equal(60, new PageRequest(4, 20).Skip);

    [Fact]
    public void Skip_is_zero_on_the_first_page() =>
        Assert.Equal(0, new PageRequest(1, 20).Skip);

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(41, 20, 3)]
    [InlineData(40, 20, 2)]
    public void TotalPages_rounds_up(int total, int pageSize, int expected) =>
        Assert.Equal(expected, new PagedResult<string>([], total, 1, pageSize).TotalPages);
}
