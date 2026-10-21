using System.Runtime.InteropServices;
using ChordPro.Core.Scrolling;
using PortAudioSharp;

namespace ChordPro.Viewer;

/// <summary>
/// Listens to the default microphone and tracks the tempo being played (experimental). The
/// microphone callback runs on an audio thread; the properties can be read from any thread.
/// </summary>
public sealed class AudioFollower : IDisposable
{
    private readonly object _lock = new();
    private readonly TempoTracker _tracker;
    private readonly PortAudioSharp.Stream _stream;
    private float[] _buffer = [];

    private AudioFollower(double expectedTempo)
    {
        PortAudio.Initialize();
        try
        {
            var device = PortAudio.DefaultInputDevice;
            if (device < 0)
                throw new InvalidOperationException("No microphone was found.");
            var info = PortAudio.GetDeviceInfo(device);
            var sampleRate = info.defaultSampleRate > 0 ? info.defaultSampleRate : 44100;
            _tracker = new TempoTracker((int)sampleRate, expectedTempo);

            var input = new StreamParameters
            {
                device = device,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = info.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };
            _stream = new PortAudioSharp.Stream(input, null, sampleRate, 0, StreamFlags.ClipOff, OnAudio, this);
            _stream.Start();
            DeviceName = info.name;
        }
        catch
        {
            PortAudio.Terminate();
            throw;
        }
    }

    public string DeviceName { get; } = "";

    /// <summary>Opens the default microphone, or returns null with a reason if it can't.</summary>
    public static AudioFollower? TryStart(double expectedTempo, out string? error)
    {
        try
        {
            error = null;
            return new AudioFollower(expectedTempo);
        }
        catch (Exception ex) when (ex is PortAudioException or DllNotFoundException or InvalidOperationException)
        {
            error = ex.Message;
            return null;
        }
    }

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

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero)
            return StreamCallbackResult.Continue;
        if (_buffer.Length < frameCount)
            _buffer = new float[frameCount];
        Marshal.Copy(input, _buffer, 0, (int)frameCount);
        lock (_lock)
            _tracker.Process(_buffer.AsSpan(0, (int)frameCount));
        return StreamCallbackResult.Continue;
    }

    public void Dispose()
    {
        _stream.Stop();
        _stream.Dispose();
        PortAudio.Terminate();
    }
}
