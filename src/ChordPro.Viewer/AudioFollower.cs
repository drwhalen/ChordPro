using ChordPro.Core.Scrolling;

namespace ChordPro.Viewer;

/// <summary>
/// Listens to the default microphone and tracks the tempo being played (experimental). The
/// properties can be read from any thread.
/// </summary>
public sealed class AudioFollower : IDisposable
{
    private readonly object _lock = new();
    private readonly Microphone _microphone;
    private readonly TempoTracker _tracker;

    private AudioFollower(Microphone microphone, double expectedTempo)
    {
        _microphone = microphone;
        _tracker = new TempoTracker(microphone.SampleRate, expectedTempo);
        _microphone.SamplesReceived += samples =>
        {
            lock (_lock)
                _tracker.Process(samples);
        };
    }

    /// <summary>Opens the default microphone, or returns null with a reason if it can't.</summary>
    public static AudioFollower? TryStart(double expectedTempo, out string? error) =>
        Microphone.TryOpen(out error) is { } microphone ? new AudioFollower(microphone, expectedTempo) : null;

    public double ExpectedTempo
    {
        set { lock (_lock) _tracker.ExpectedTempo = value; }
    }

    /// <summary>The tempo heard, in BPM, or null until it's clear.</summary>
    public double? Tempo
    {
        get { lock (_lock) return _tracker.Tempo; }
    }

    public bool IsPlaying
    {
        get { lock (_lock) return _tracker.IsPlaying; }
    }

    public void Dispose() => _microphone.Dispose();
}
