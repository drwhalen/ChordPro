namespace ChordPro.Core.Tests;

public class SetLengthTests
{
    [Fact]
    public void AddsUpTheSongsAndCountsThoseWithoutALength()
    {
        Assert.Equal("2 songs · 10 min", SetLength.Of([TimeSpan.FromSeconds(258), TimeSpan.FromSeconds(356)]).ToString());
        Assert.Equal("2 songs · 5 min (1 without a length)", SetLength.Of([null, TimeSpan.FromMinutes(5)]).ToString());
        Assert.Equal("1 song", SetLength.Of([null]).ToString());
    }

    [Fact]
    public void GivesLongSetsInHours()
    {
        Assert.Equal("1 h 05 min", SetLength.Minutes(TimeSpan.FromMinutes(65)));
        Assert.Equal("3 songs", SetLength.Of([null, null, null]).ToString());
    }
}
