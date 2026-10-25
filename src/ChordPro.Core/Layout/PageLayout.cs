namespace ChordPro.Core.Layout;

/// <summary>The vertical extent of one unbreakable line (a lyric line with its chords, a comment, …).</summary>
public readonly record struct LineExtent(double Top, double Bottom);

/// <summary>
/// Lines that belong together, such as a section. <see cref="KeepWithNext"/> keeps the group on the
/// same page as the start of the following group (used for titles and comment-only lead-ins).
/// </summary>
public sealed record PageGroup(IReadOnlyList<LineExtent> Lines, bool KeepWithNext = false)
{
    public double Top => Lines[0].Top;
    public double Bottom => Lines[^1].Bottom;
    public double Height => Bottom - Top;
}

/// <summary>
/// Chooses page breaks (for printing) and page turns (on screen) so that sections stay whole when
/// they fit, long sections break between lines with at least <see cref="MinLines"/> lines on each
/// side, and a chord is never separated from its lyric (each line is unbreakable).
/// </summary>
public static class PageLayout
{
    /// <summary>Fewest lines of a section to leave on either side of a page break.</summary>
    public const int MinLines = 2;

    /// <summary>Where each page starts, for pages <paramref name="pageHeight"/> tall.</summary>
    public static List<double> PageStarts(IReadOnlyList<PageGroup> groups, double pageHeight)
    {
        var starts = new List<double> { 0 };
        var pageStart = 0.0;

        void BreakAt(double y)
        {
            if (y <= pageStart)
                return;
            pageStart = y;
            starts.Add(y);
        }

        var nonEmpty = groups.Where(g => g.Lines.Count > 0).ToList();
        for (var g = 0; g < nonEmpty.Count; g++)
        {
            var group = nonEmpty[g];
            var keepBottom = KeepBottom(nonEmpty, g);

            if (keepBottom - pageStart <= pageHeight)
                continue;                       // fits on this page
            if (keepBottom - group.Top <= pageHeight)
            {
                BreakAt(group.Top);             // fits on a fresh page: move it whole
                continue;
            }

            // Taller than a page: break between lines.
            var lines = group.Lines;
            var i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Bottom - pageStart <= pageHeight)
                {
                    i++;
                    continue;
                }

                var firstOnPage = Math.Max(0, IndexOfFirstLineFrom(lines, pageStart));
                var breakIndex = i;
                // Don't strand a few lines on the next page…
                if (lines.Count - breakIndex < MinLines)
                    breakIndex = Math.Max(firstOnPage + MinLines, lines.Count - MinLines);
                // …or at the bottom of this one: start the section on the next page instead.
                if (breakIndex - firstOnPage < MinLines && firstOnPage == 0 && group.Top > pageStart)
                    breakIndex = 0;
                if (breakIndex > i)
                    breakIndex = i; // the rules conflict on a very short page; the line must not overflow

                if (lines[breakIndex].Top > pageStart)
                    BreakAt(lines[breakIndex].Top);
                else
                    BreakAt(pageStart + pageHeight); // a single line taller than a page has to be cut
                i = breakIndex;
            }
        }

        return starts;
    }

    /// <summary>
    /// Where a page turn (+1 down, −1 up) from <paramref name="offset"/> moves the view, over fixed pages that
    /// start at <paramref name="pageStarts"/> (see <see cref="PageStarts"/>): to the next page, or to the start of
    /// the page before the one showing, so paging down and back up shows the same pages and each starts at a
    /// section where one fits. Null when there's no next page.
    /// </summary>
    public static double? Turn(IReadOnlyList<double> pageStarts, double offset, int direction) =>
        direction > 0
            ? pageStarts.Where(top => top > offset + 0.5).Select(top => (double?)top).FirstOrDefault()
            : pageStarts.Where(top => top < offset - 0.5).Select(top => (double?)top).LastOrDefault() ?? 0;

    // The lowest point that must share a page with the group: its end, or the first lines of the
    // next group when it is kept with the next.
    private static double KeepBottom(List<PageGroup> groups, int index)
    {
        var group = groups[index];
        if (!group.KeepWithNext || index + 1 >= groups.Count)
            return group.Bottom;
        var next = groups[index + 1].Lines;
        return next[Math.Min(MinLines, next.Count) - 1].Bottom;
    }

    private static int IndexOfFirstLineFrom(IReadOnlyList<LineExtent> lines, double y)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Top >= y - 0.5)
                return i;
        }
        return lines.Count;
    }
}
