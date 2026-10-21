using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ChordPro.Viewer;

/// <summary>
/// Scrolls a <see cref="ScrollViewer"/> smoothly at a steady speed. Scrolling by hand while it runs
/// is respected; it pauses itself at the bottom.
/// </summary>
public sealed class AutoScroller
{
    /// <summary>Speed used when the song has no duration, in pixels per second at 1×.</summary>
    private const double DefaultPixelsPerSecond = 20;

    private readonly ScrollViewer _scroller;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;
    private double _position;

    public AutoScroller(ScrollViewer scroller)
    {
        _scroller = scroller;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
    }

    /// <summary>Raised when scrolling starts or stops.</summary>
    public event EventHandler? RunningChanged;

    public bool IsRunning => _timer.IsEnabled;

    /// <summary>Multiplier applied to the base speed.</summary>
    public double SpeedFactor { get; set; } = 1;

    /// <summary>The song's length, used to pace scrolling so the end arrives as the song finishes.</summary>
    public TimeSpan? Duration { get; set; }

    public void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    public void Start()
    {
        if (IsRunning)
            return;
        if (MaxOffset <= 0 || _scroller.Offset.Y >= MaxOffset - 1)
            _scroller.Offset = _scroller.Offset.WithY(0);
        _position = _scroller.Offset.Y;
        _lastTick = DateTime.UtcNow;
        _timer.Start();
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        _timer.Stop();
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Parses ChordPro durations such as "3:45", "1:02:30" or "225" (seconds).</summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3 || !parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return null;
        var seconds = parts.Aggregate(0, (total, part) => total * 60 + int.Parse(part, CultureInfo.InvariantCulture));
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private double MaxOffset => Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);

    private double PixelsPerSecond =>
        SpeedFactor * (Duration is { } d && MaxOffset > 0 ? MaxOffset / d.TotalSeconds : DefaultPixelsPerSecond);

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // If the user scrolled by hand, carry on from where they left it.
        if (Math.Abs(_scroller.Offset.Y - _position) > 2)
            _position = _scroller.Offset.Y;

        _position = Math.Min(MaxOffset, _position + PixelsPerSecond * elapsed);
        _scroller.Offset = new Vector(_scroller.Offset.X, _position);

        if (_position >= MaxOffset)
            Stop();
    }
}
