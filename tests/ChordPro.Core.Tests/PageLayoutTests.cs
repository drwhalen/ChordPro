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
    public void PageTurnsMoveBetweenPagesThatStartAtSections()
    {
        // Verse 0–100, chorus 120–200: in a 160 view the chorus is cut off, so the second page starts at it.
        var starts = PageLayout.PageStarts([Section(0, 5), Section(120, 4)], 160);
        Assert.Equal([0.0, 120.0], starts);
        Assert.Equal(120, PageLayout.Turn(starts, 0, +1));
        Assert.Null(PageLayout.Turn(starts, 120, +1));
    }

    [Fact]
    public void PageUpReturnsToThePageBefore()
    {
        double[] starts = [0, 120, 300];
        Assert.Equal(120, PageLayout.Turn(starts, 300, -1));
        // Between pages (after dragging), up goes to the start of the page showing at the top.
        Assert.Equal(120, PageLayout.Turn(starts, 200, -1));
        Assert.Equal(0, PageLayout.Turn(starts, 120, -1));
        Assert.Equal(0, PageLayout.Turn(starts, 0, -1));
    }

    [Fact]
    public void PageDownFromBetweenPagesGoesToTheNextStart() =>
        Assert.Equal(300, PageLayout.Turn([0, 120, 300], 200, +1));
}
