using ChordPro.Core.Scrolling;

namespace ChordPro.Viewer;

/// <summary>
/// Listens for singing and turns it into words with Whisper, on this computer (no network): every
/// interval it transcribes the last few seconds of audio and raises <see cref="Heard"/>. How long
/// the window is, how often, and how loud singing must be come from a <see cref="VoiceTuning"/>,
/// which training can fit to the singer. Recognition of sung words is rough, which is fine: the
/// words are only used to find the place in lyrics that are already known (see LyricAligner).
/// </summary>
public sealed class VoiceFollower : IDisposable
{
    public const int SampleRate = Transcriber.SampleRate;

    private readonly object _lock = new();
    private readonly Microphone? _microphone;
    private readonly Transcriber _transcriber;
    private readonly VoiceTuning _tuning;
    private readonly int _inputRate;
    private readonly float[] _ring;
    private int _ringPosition;
    private int _filled;
    private int _sinceLast;
    private double _resampleAt;   // position in input samples of the next output sample
    private float _carry;         // last input sample, for interpolating across buffers
    private bool _busy;
    private bool _disposed;

    private VoiceFollower(string modelPath, Microphone? microphone, int inputRate, VoiceTuning tuning)
    {
        _transcriber = new Transcriber(modelPath);
        _tuning = tuning;
        _ring = new float[(int)(tuning.WindowSeconds * SampleRate)];
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
    public static VoiceFollower? TryStart(string modelPath, VoiceTuning tuning, out string? error)
    {
        if (Microphone.TryOpen(out error) is not { } microphone)
            return null;
        try
        {
            return new VoiceFollower(modelPath, microphone, microphone.SampleRate, tuning);
        }
        catch (Exception ex)
        {
            microphone.Dispose();
            error = $"the speech model couldn't be loaded ({ex.Message})";
            return null;
        }
    }

    /// <summary>A follower fed with <see cref="Feed"/> instead of a microphone (for testing with recordings).</summary>
    public static VoiceFollower ForRecording(string modelPath, int sampleRate, VoiceTuning? tuning = null) =>
        new(modelPath, null, sampleRate, tuning ?? VoiceTuning.Default);

    /// <summary>
    /// Raised on a background thread with the words heard in the latest window, and when the
    /// window ended. Not raised for silence.
    /// </summary>
    public event Action<string, DateTime>? Heard;

    /// <summary>The level of the latest window (RMS, 0–1), for showing that something is heard.</summary>
    public float Level { get; private set; }

    /// <summary>
    /// The lyrics sung just before the current place, given to recognition as a hint when the tuning
    /// asks for it (see LyricAligner.PromptText). Set from any thread.
    /// </summary>
    public string? Prompt { get; set; }

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

            if (_sinceLast >= _tuning.IntervalSeconds * SampleRate && _filled == _ring.Length && !_busy)
            {
                _sinceLast = 0;
                window = new float[_ring.Length];
                for (var k = 0; k < _ring.Length; k++)
                    window[k] = _ring[(_ringPosition + k) % _ring.Length];
                Level = Transcriber.Level(window);
                // Below the gate there is nothing to transcribe; Whisper only invents words in silence.
                if (Level >= _tuning.MinLevel)
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
            var text = await _transcriber.TranscribeAsync(window, _tuning.UsePrompt ? Prompt : null);
            if (text.Length > 0)
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
        _transcriber.Dispose();
    }

    private bool IsBusy()
    {
        lock (_lock)
            return _busy;
    }
}
