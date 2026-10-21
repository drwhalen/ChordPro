using ChordPro.Core.Layout;

namespace ChordPro.Core.Tests;

public class PageLayoutTests
{
    // A section of `count` lines, each 20 tall, starting at `top` (no gaps between lines).
    private static PageGroup Section(double top, int count, bool keepWithNext = false) =>
        new([.. Enumerable.Range(0, count).Select(i => new LineExtent(top + i * 20, top + (i + 1) * 20))], keepWithNext);

    [Fact]
    public void EverythingOnOnePageWhenItFits() =>
        Assert.Equal([0.0], PageLayout.PageStarts([Section(0, 3), Section(70, 3)], 200));

    [Fact]
    public void MovesASectionThatWouldBeSplitToTheNextPage()
    {
        // Page 0–200. The chorus (150–230) would be split, so the page breaks before it.
        var starts = PageLayout.PageStarts([Section(0, 7), Section(150, 4)], 200);

        Assert.Equal([0.0, 150], starts);
    }

    [Fact]
    public void BreaksALongSectionBetweenLines()
    {
        // 20 lines (400 tall) on 200-tall pages: ten lines a page, broken at a line boundary.
        Assert.Equal([0.0, 200], PageLayout.PageStarts([Section(0, 20)], 200));
    }

    [Fact]
    public void DoesNotLeaveASingleLineOfASectionOnTheNextPage()
    {
        // 11 lines (0–220) on a 200-tall page would leave one line over; break two lines early.
        var starts = PageLayout.PageStarts([Section(0, 11)], 200);

        Assert.Equal([0.0, 180], starts);
    }

    [Fact]
    public void DoesNotStartALongSectionWithOneLineAtTheBottomOfAPage()
    {
        // A first section fills 0–180; a 15-line section starts at 180, leaving room for one line.
        var starts = PageLayout.PageStarts([Section(0, 9), Section(180, 15)], 200);

        Assert.Equal(180, starts[1]);
    }

    [Fact]
    public void KeepsALeadInCommentWithTheSectionAfterIt()
    {
        // "Solo" comment at 170–190, then a section at 190. The comment moves with it.
        var starts = PageLayout.PageStarts([Section(0, 8), Section(170, 1, keepWithNext: true), Section(190, 3)], 200);

        Assert.Equal([0.0, 170], starts);
    }

    [Fact]
    public void CutsALineTallerThanAPage()
    {
        var starts = PageLayout.PageStarts([new PageGroup([new LineExtent(0, 450)])], 200);

        Assert.Equal([0.0, 200, 400], starts);
    }

    [Fact]
    public void EveryPageStartIsALineTopUnlessALineIsTooTall()
    {
        var groups = new[] { Section(0, 5), Section(110, 12), Section(360, 3, keepWithNext: true), Section(430, 9), Section(620, 2) };
        var tops = groups.SelectMany(g => g.Lines).Select(l => l.Top).ToHashSet();

        foreach (var height in new[] { 100.0, 150, 200, 260 })
            Assert.All(PageLayout.PageStarts(groups, height), s => Assert.Contains(s, tops));
    }

    [Fact]
    public void NextScreenStartsAtTheFirstLineNotFullyVisible()
    {
        // View 0–210 shows lines up to 200; the line at 200 is cut off.
        Assert.Equal(200, PageLayout.NextPageTop([Section(0, 20)], 0, 210));
    }

    [Fact]
    public void NextScreenShowsACutOffChorusWhole()
    {
        // Verse 0–100, chorus 120–200 cut off by a view ending at 160: the next screen starts at the chorus.
        Assert.Equal(120, PageLayout.NextPageTop([Section(0, 5), Section(120, 4)], 0, 160));
    }

    [Fact]
    public void NextScreenDoesNotJumpBackForASectionTallerThanTheView()
    {
        // The section starts at 120 but is 400 tall, so carry on from the cut-off line instead.
        Assert.Equal(160, PageLayout.NextPageTop([Section(0, 5), Section(120, 20)], 0, 170));
    }

    [Fact]
    public void NoNextScreenWhenTheEndIsVisible() =>
        Assert.Null(PageLayout.NextPageTop([Section(0, 5)], 0, 200));

    [Fact]
    public void PreviousScreenEndsWhereTheCurrentOneStarts()
    {
        // At offset 300 with a 210 view: go back to 90, snapped down to the line at 100.
        Assert.Equal(100, PageLayout.PreviousPageTop([Section(0, 30)], 300, 210));
        Assert.Equal(0, PageLayout.PreviousPageTop([Section(0, 30)], 100, 210));
    }
}
