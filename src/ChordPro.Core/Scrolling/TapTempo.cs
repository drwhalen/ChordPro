namespace ChordPro.Core.Scrolling;

/// <summary>
/// Turns taps on a button into a tempo: the average beat over the most recent taps. A pause longer
/// than <see cref="ResetAfter"/> starts a new count, so tapping again later measures afresh.
/// </summary>
public sealed class TapTempo
{
    public static readonly TimeSpan ResetAfter = TimeSpan.FromSeconds(2);

    // Enough taps to even out a shaky hand, few enough to follow a deliberate change.
    private const int MaxTaps = 8;

    private readonly List<TimeSpan> _taps = [];

    /// <summary>Taps in the current count.</summary>
    public int Count => _taps.Count;

    /// <summary>Records a tap at <paramref name="time"/>; returns the tempo in BPM once there are two taps or more.</summary>
    public double? Tap(TimeSpan time)
    {
        if (_taps.Count > 0 && (time - _taps[^1] > ResetAfter || time <= _taps[^1]))
            _taps.Clear();
        _taps.Add(time);
        if (_taps.Count > MaxTaps)
            _taps.RemoveAt(0);
        if (_taps.Count < 2)
            return null;
        var beat = (_taps[^1] - _taps[0]).TotalMinutes / (_taps.Count - 1);
        return 1 / beat;
    }
}
