namespace ChordPro.Core.Scrolling;

/// <summary>A point on a scroll timeline: at <see cref="Time"/> seconds the view should be at <see cref="Offset"/>.</summary>
public readonly record struct ScrollKeyframe(double Time, double Offset);

/// <summary>Scroll offset over time, interpolated linearly between keyframes.</summary>
public sealed class ScrollTimeline
{
    private readonly ScrollKeyframe[] _keyframes;

    /// <param name="keyframes">Keyframes in time order; times and offsets must not decrease.</param>
    public ScrollTimeline(IEnumerable<ScrollKeyframe> keyframes)
    {
        _keyframes = [.. keyframes];
        if (_keyframes.Length == 0)
            _keyframes = [new ScrollKeyframe(0, 0)];
    }

    public double Duration => _keyframes[^1].Time;

    public double OffsetAt(double time)
    {
        if (time <= _keyframes[0].Time)
            return _keyframes[0].Offset;
        for (var i = 1; i < _keyframes.Length; i++)
        {
            var (a, b) = (_keyframes[i - 1], _keyframes[i]);
            if (time <= b.Time)
                return b.Time == a.Time ? b.Offset : a.Offset + (b.Offset - a.Offset) * (time - a.Time) / (b.Time - a.Time);
        }
        return _keyframes[^1].Offset;
    }

    /// <summary>The earliest time at which the view reaches <paramref name="offset"/>.</summary>
    public double TimeAt(double offset)
    {
        if (offset <= _keyframes[0].Offset)
            return _keyframes[0].Time;
        for (var i = 1; i < _keyframes.Length; i++)
        {
            var (a, b) = (_keyframes[i - 1], _keyframes[i]);
            if (offset <= b.Offset)
                return b.Offset == a.Offset ? a.Time : a.Time + (b.Time - a.Time) * (offset - a.Offset) / (b.Offset - a.Offset);
        }
        return _keyframes[^1].Time;
    }
}
