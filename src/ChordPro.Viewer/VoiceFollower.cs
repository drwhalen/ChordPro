using System.Text.RegularExpressions;
using Whisper.net;

namespace ChordPro.Viewer;

/// <summary>
/// Listens for singing and turns it into words with Whisper, on this computer (no network): every
/// <see cref="Interval"/> it transcribes the last <see cref="Window"/> of audio and raises
/// <see cref="Heard"/>. Recognition of sung words is rough, which is fine: the words are only used
/// to find the place in lyrics that are already known (see LyricAligner).
/// </summary>
public sealed partial class VoiceFollower : IDisposable
{
    // Whisper labels sounds rather than words with brackets: "[BLANK_AUDIO]", "(music)", "*laughs*".
    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex SoundLabelRegex();

    public const int SampleRate = 16000; // what Whisper expects
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(4);
    // A window takes about a tenth of a second to transcribe on Apple Silicon, so once a second keeps
    // the lag after a line starts short without using much of the machine.
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    // Below this (about -50 dBFS) there is nothing to transcribe; Whisper only invents words in silence.
    private const float MinLevel = 0.003f;

    private readonly object _lock = new();
    private readonly Microphone? _microphone;
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly int _inputRate;
    private readonly float[] _ring = new float[(int)(Window.TotalSeconds * SampleRate)];
    private int _ringPosition;
    private int _filled;
    private int _sinceLast;
    private double _resampleAt;   // position in input samples of the next output sample
    private float _carry;         // last input sample, for interpolating across buffers
    private bool _busy;
    private bool _disposed;

    private VoiceFollower(string modelPath, Microphone? microphone, int inputRate)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 4))
            .WithNoContext()       // each window stands alone; earlier text would be repeated
            .WithSingleSegment()
            .Build();
        _microphone = microphone;
        _inputRate = inputRate;
        if (microphone is not null)
            microphone.SamplesReceived += Feed;
        // Whisper's GPU code aborts if a model is still loaded when the process exits, which macOS
        // reports as a crash; make sure it's freed even if the app quits while listening.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    /// <summary>Opens the default microphone, or returns null with a reason if it can't.</summary>
    public static VoiceFollower? TryStart(string modelPath, out string? error)
    {
        if (Microphone.TryOpen(out error) is not { } microphone)
            return null;
        try
        {
            return new VoiceFollower(modelPath, microphone, microphone.SampleRate);
        }
        catch (Exception ex)
        {
            microphone.Dispose();
            error = $"the speech model couldn't be loaded ({ex.Message})";
            return null;
        }
    }

    /// <summary>A follower fed with <see cref="Feed"/> instead of a microphone (for testing with recordings).</summary>
    public static VoiceFollower ForRecording(string modelPath, int sampleRate) => new(modelPath, null, sampleRate);

    /// <summary>
    /// Raised on a background thread with the words heard in the latest window, and when the
    /// window ended. Not raised for silence.
    /// </summary>
    public event Action<string, DateTime>? Heard;

    /// <summary>The level of the latest window (RMS, 0–1), for showing that something is heard.</summary>
    public float Level { get; private set; }

    /// <summary>Adds audio at the input sample rate. Called on the audio thread.</summary>
    public void Feed(ReadOnlySpan<float> samples)
    {
        float[]? window = null;
        lock (_lock)
        {
            if (_disposed)
                return;
            // Resample to 16 kHz by linear interpolation, after averaging over each output step so
            // higher frequencies don't fold down (speech sits well below 8 kHz).
            var step = _inputRate / (double)SampleRate;
            var span = Math.Max(1, (int)step);
            while (_resampleAt < samples.Length)
            {
                var i = (int)_resampleAt;
                var sum = 0f;
                var n = 0;
                for (var k = Math.Max(0, i - span + 1); k <= i; k++, n++)
                    sum += samples[k];
                var value = n > 0 ? sum / n : _carry;
                _ring[_ringPosition] = value;
                _ringPosition = (_ringPosition + 1) % _ring.Length;
                _filled = Math.Min(_filled + 1, _ring.Length);
                _sinceLast++;
                _resampleAt += step;
            }
            _resampleAt -= samples.Length;
            if (samples.Length > 0)
                _carry = samples[^1];

            if (_sinceLast >= Interval.TotalSeconds * SampleRate && _filled == _ring.Length && !_busy)
            {
                _sinceLast = 0;
                window = new float[_ring.Length];
                for (var k = 0; k < _ring.Length; k++)
                    window[k] = _ring[(_ringPosition + k) % _ring.Length];
                Level = MathF.Sqrt(window.Average(s => s * s));
                if (Level >= MinLevel)
                    _busy = true;
                else
                    window = null;
            }
        }
        if (window is not null)
            _ = Task.Run(() => TranscribeAsync(window, DateTime.UtcNow));
    }

    private async Task TranscribeAsync(float[] window, DateTime end)
    {
        try
        {
            var text = "";
            await foreach (var segment in _processor.ProcessAsync(window))
                text += segment.Text;
            text = SoundLabelRegex().Replace(text, " ").Trim();
            if (text.Any(char.IsLetter))
                Heard?.Invoke(text, end);
        }
        catch (ObjectDisposedException)
        {
            // stopped while transcribing
        }
        finally
        {
            lock (_lock)
                _busy = false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _microphone?.Dispose();
        // Let a transcription in progress finish before freeing the model.
        for (var i = 0; i < 100 && IsBusy(); i++)
            Thread.Sleep(20);
        _processor.Dispose();
        _factory.Dispose();
    }

    private bool IsBusy()
    {
        lock (_lock)
            return _busy;
    }
}
