using System.Runtime.InteropServices;
using PortAudioSharp;

namespace ChordPro.Viewer;

/// <summary>
/// The default microphone, read through PortAudio. <see cref="SamplesReceived"/> is raised on an
/// audio thread with each buffer of mono samples.
/// </summary>
public sealed class Microphone : IDisposable
{
    private readonly PortAudioSharp.Stream _stream;
    private float[] _buffer = [];

    private Microphone()
    {
        PortAudio.Initialize();
        try
        {
            var device = PortAudio.DefaultInputDevice;
            if (device < 0)
                throw new InvalidOperationException("No microphone was found.");
            var info = PortAudio.GetDeviceInfo(device);
            SampleRate = (int)(info.defaultSampleRate > 0 ? info.defaultSampleRate : 44100);
            DeviceName = info.name;

            var input = new StreamParameters
            {
                device = device,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = info.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };
            _stream = new PortAudioSharp.Stream(input, null, SampleRate, 0, StreamFlags.ClipOff, OnAudio, this);
            _stream.Start();
        }
        catch
        {
            PortAudio.Terminate();
            throw;
        }
    }

    public int SampleRate { get; }
    public string DeviceName { get; } = "";

    /// <summary>Raised on the audio thread with each buffer of samples (valid only during the call).</summary>
    public event SamplesHandler? SamplesReceived;

    public delegate void SamplesHandler(ReadOnlySpan<float> samples);

    /// <summary>Opens the default microphone, or returns null with a reason if it can't.</summary>
    public static Microphone? TryOpen(out string? error)
    {
        try
        {
            error = null;
            return new Microphone();
        }
        catch (Exception ex) when (ex is PortAudioException or DllNotFoundException or InvalidOperationException)
        {
            error = ex.Message;
            return null;
        }
    }

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero)
            return StreamCallbackResult.Continue;
        if (_buffer.Length < frameCount)
            _buffer = new float[frameCount];
        Marshal.Copy(input, _buffer, 0, (int)frameCount);
        SamplesReceived?.Invoke(_buffer.AsSpan(0, (int)frameCount));
        return StreamCallbackResult.Continue;
    }

    public void Dispose()
    {
        _stream.Stop();
        _stream.Dispose();
        PortAudio.Terminate();
    }
}
