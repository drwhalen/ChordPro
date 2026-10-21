using ChordPro.Core.Scrolling;

namespace ChordPro.Core.Tests;

public class ScrollTimelineTests
{
    private static readonly ScrollTimeline Timeline = new(
    [
        new(0, 0),
        new(10, 0),      // hold at the top for the first 10 seconds
        new(20, 500),
        new(30, 600),    // slow section
        new(31, 900),    // quick transition
    ]);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(15, 250)]
    [InlineData(25, 550)]
    [InlineData(30.5, 750)]
    [InlineData(99, 900)]
    public void InterpolatesOffsets(double time, double offset) =>
        Assert.Equal(offset, Timeline.OffsetAt(time), precision: 6);

    [Theory]
    [InlineData(0, 0)]       // the earliest time the top is shown
    [InlineData(250, 15)]
    [InlineData(750, 30.5)]
    [InlineData(5000, 31)]
    public void FindsTheTimeForAnOffset(double offset, double time) =>
        Assert.Equal(time, Timeline.TimeAt(offset), precision: 6);

    [Fact]
    public void DurationIsTheLastKeyframe() => Assert.Equal(31, Timeline.Duration);

    [Fact]
    public void EmptyTimelineStaysAtTheTop()
    {
        var empty = new ScrollTimeline([]);
        Assert.Equal(0, empty.OffsetAt(5));
        Assert.Equal(0, empty.Duration);
    }
}
