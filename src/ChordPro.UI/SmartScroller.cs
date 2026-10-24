using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChordPro.Core.Scrolling;

namespace ChordPro.UI;

public enum ScrollMode
{
    /// <summary>Tap, swipe, keyboard or foot pedal; nothing moves by itself.</summary>
    Manual,
    /// <summary>Paced by the song's duration, or by timestamp markers.</summary>
    Timed,
    /// <summary>Paced by tempo, time signature and bars, line by line.</summary>
    Musical,
    /// <summary>The musical timeline, driven by the tempo heard through the microphone (experimental).</summary>
    FollowAudio,
    /// <summary>The musical timeline, kept in place by the lyrics heard being sung (experimental).</summary>
    FollowVoice,
}

/// <summary>
/// Moves a <see cref="ScrollViewer"/> along a <see cref="ScrollTimeline"/>, and animates page turns.
/// Scrolling by hand at any time is respected: the timeline jumps to wherever the view was moved.
/// </summary>
public sealed class SmartScroller
{
    private const double PageFraction = 0.8;
    private static readonly TimeSpan PageAnimation = TimeSpan.FromMilliseconds(250);

    private readonly ScrollViewer _scroller;
    private readonly DispatcherTimer _timer;
    private ScrollTimeline? _timeline;
    private DateTime _lastTick;
    private double _lastOffset;
    private (double From, double To, DateTime Start)? _page;

    public SmartScroller(ScrollViewer scroller)
    {
        _scroller = scroller;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
    }

    /// <summary>Raised when scrolling starts or stops.</summary>
    public event EventHandler? RunningChanged;

    /// <summary>Raised on every step while running, for progress displays.</summary>
    public event EventHandler? Progressed;

    /// <summary>Builds the timeline for the current song and layout; null if there's nothing to follow.</summary>
    public Func<ScrollTimeline?>? TimelineFactory { get; set; }

    /// <summary>How fast the timeline plays: 1 is as written. Ignored while <see cref="LiveRate"/> is set.</summary>
    public double Rate { get; set; } = 1;

    /// <summary>A rate that changes while playing (following audio); 0 holds the view still.</summary>
    public Func<double>? LiveRate { get; set; }

    /// <summary>Where on the timeline the player is known to be (following lyrics); the view eases there.</summary>
    public Func<double?>? TargetTime { get; set; }

    /// <summary>The furthest the timeline may run ahead (to the next line not yet heard).</summary>
    public Func<double?>? MaxTime { get; set; }

    /// <summary>Raised when the view is scrolled by hand while running, with the new time.</summary>
    public event Action<double>? ScrolledByHand;

    /// <summary>
    /// Where a page turn (+1 down, -1 up) should move the view, given the current offset and view
    /// height; null falls back to most of a screen.
    /// </summary>
    public Func<int, double, double, double?>? PageTarget { get; set; }

    public bool IsRunning { get; private set; }

    /// <summary>Position on the timeline, in seconds.</summary>
    public double Time { get; private set; }

    public double Duration => _timeline?.Duration ?? 0;

    public void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    public void Start()
    {
        if (IsRunning || (_timeline = TimelineFactory?.Invoke()) is null)
            return;

        // Carry on from the current position, or start over if the end was reached.
        Time = _scroller.Offset.Y >= MaxOffset - 1 && MaxOffset > 0 ? 0 : _timeline.TimeAt(_scroller.Offset.Y);
        if (Time == 0)
            SetOffset(_timeline.OffsetAt(0));
        _lastOffset = _scroller.Offset.Y;
        _lastTick = DateTime.UtcNow;
        IsRunning = true;
        _timer.Start();
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        IsRunning = false;
        if (_page is null)
            _timer.Stop();
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuild the timeline at the next step (after the song is re-laid out), keeping the time.</summary>
    public void InvalidateTimeline() => _timeline = null;

    /// <summary>Animate one page down (+1) or up (-1).</summary>
    public void Page(int direction)
    {
        var from = _scroller.Offset.Y;
        var target = PageTarget?.Invoke(direction, from, _scroller.Viewport.Height)
            ?? from + direction * _scroller.Viewport.Height * PageFraction;
        var to = Math.Clamp(target, 0, MaxOffset);
        _page = (from, to, DateTime.UtcNow);
        _timer.Start();
    }

    private double MaxOffset => Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);

    private void SetOffset(double offset)
    {
        _scroller.Offset = new Vector(_scroller.Offset.X, Math.Clamp(offset, 0, MaxOffset));
        _lastOffset = _scroller.Offset.Y;
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        if (_page is { } page)
        {
            var t = Math.Min(1, (now - page.Start) / PageAnimation);
            var eased = 1 - Math.Pow(1 - t, 3);
            SetOffset(page.From + (page.To - page.From) * eased);
            if (t < 1)
                return;
            _page = null;
            if (!IsRunning)
            {
                _timer.Stop();
                return;
            }
            if (_timeline is not null)
                Time = _timeline.TimeAt(_scroller.Offset.Y);
        }

        if (!IsRunning)
            return;

        _timeline ??= TimelineFactory?.Invoke();
        if (_timeline is null)
        {
            Stop();
            return;
        }

        // Scrolled by hand: carry on from there.
        if (Math.Abs(_scroller.Offset.Y - _lastOffset) > 2)
        {
            Time = _timeline.TimeAt(_scroller.Offset.Y);
            ScrolledByHand?.Invoke(Time);
        }

        Time += elapsed * (LiveRate?.Invoke() ?? Rate);
        // Ease towards a known position over about a third of a second, rather than jumping.
        if (TargetTime?.Invoke() is { } target && Math.Abs(target - Time) > 0.2)
            Time += (target - Time) * Math.Min(1, elapsed * 3);
        if (MaxTime?.Invoke() is { } max && Time > max)
            Time = Math.Max(max, Time - elapsed * (LiveRate?.Invoke() ?? Rate));
        SetOffset(_timeline.OffsetAt(Time));
        Progressed?.Invoke(this, EventArgs.Empty);

        if (Time >= _timeline.Duration)
            Stop();
    }
}
