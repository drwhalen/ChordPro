using Avalonia;
using Avalonia.Controls;
using ChordPro.Core;
using ChordPro.Core.Scrolling;

namespace ChordPro.Viewer;

/// <summary>Turns a song's timing (duration, markers, or bars) into a timeline of scroll offsets for its rendered layout.</summary>
public sealed class ScrollTimelineBuilder(Song song, IReadOnlyDictionary<(Section, int), Control> lines, Visual content,
    double viewportHeight, double maxOffset)
{
    /// <summary>Fraction of the way down the view where the line being played is kept.</summary>
    public const double ReadingLine = 0.3;

    /// <summary>Speed for a timed song with no duration or markers, in pixels per second.</summary>
    public const double DefaultPixelsPerSecond = 20;

    public double ReadingLineY => viewportHeight * ReadingLine;

    /// <summary>
    /// Timed: the view reaches each <c>{x_time}</c> marker's line at its time, and the bottom at
    /// <c>{duration}</c>. With neither, it scrolls at a steady default speed.
    /// </summary>
    public ScrollTimeline Timed()
    {
        var keyframes = new List<ScrollKeyframe> { new(0, 0) };
        foreach (var (section, index, time) in TimeMarkers.Find(song))
        {
            if (OffsetOf(section, index) is { } offset)
                keyframes.Add(new ScrollKeyframe(time.TotalSeconds, offset));
        }

        var last = keyframes[^1];
        var duration = SongTime.Parse(song.Duration)?.TotalSeconds;
        var end = duration > last.Time ? duration.Value : last.Time + (maxOffset - last.Offset) / DefaultPixelsPerSecond;
        keyframes.Add(new ScrollKeyframe(end, maxOffset));
        return new ScrollTimeline(Monotonic(keyframes));
    }

    /// <summary>
    /// Musical: each line reaches the reading line one beat before it starts, and the view moves
    /// across a line over the time it is played, so long instrumental lines hold the view nearly
    /// still and comments in between are passed quickly.
    /// </summary>
    public ScrollTimeline Musical(MusicalTimeline timeline)
    {
        var lead = 60 / timeline.Tempo;
        var keyframes = new List<ScrollKeyframe> { new(0, 0) };
        foreach (var line in timeline.Lines.Where(l => l.Length > 0))
        {
            if (OffsetOf(line.Section, line.LineIndex) is { } offset)
                keyframes.Add(new ScrollKeyframe(Math.Max(0, line.Start * timeline.SecondsPerBar - lead), offset));
        }
        keyframes.Add(new ScrollKeyframe(Math.Max(keyframes[^1].Time, timeline.TotalSeconds), maxOffset));
        return new ScrollTimeline(Monotonic(keyframes));
    }

    private double? OffsetOf(Section section, int index)
    {
        if (!lines.TryGetValue((section, index), out var control) || control.TranslatePoint(default, content) is not { } point)
            return null;
        return Math.Clamp(point.Y - ReadingLineY, 0, maxOffset);
    }

    // Keep times and offsets from going backwards, so the view never jumps up while playing.
    private static IEnumerable<ScrollKeyframe> Monotonic(List<ScrollKeyframe> keyframes)
    {
        var time = 0.0;
        var offset = 0.0;
        foreach (var keyframe in keyframes.OrderBy(k => k.Time))
        {
            time = Math.Max(time, keyframe.Time);
            offset = Math.Max(offset, keyframe.Offset);
            yield return new ScrollKeyframe(time, offset);
        }
    }
}
