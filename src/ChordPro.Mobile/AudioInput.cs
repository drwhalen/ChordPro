using System.Runtime.InteropServices;
using AVFoundation;
using Foundation;

namespace ChordPro.Mobile;

/// <summary>
/// The microphone, for the tuner: iOS asks the player once, then <see cref="Samples"/> is called on an audio
/// thread with each buffer of mono samples until it's disposed.
/// </summary>
internal sealed class AudioInput : IDisposable
{
    private const uint BufferFrames = 4096;
    private readonly AVAudioEngine _engine = new();
    private readonly Action<float[], int> _samples;
    private float[] _buffer = [];

    private AudioInput(Action<float[], int> samples) => _samples = samples;

    public int SampleRate { get; private set; }

    /// <summary>Opens the microphone, or returns why it can't (the player said no, or there isn't one).</summary>
    /// <param name="samples">Gets a buffer and how many samples in it are new (valid only during the call).</param>
    public static async Task<(AudioInput? Input, string? Error)> OpenAsync(Action<float[], int> samples)
    {
        if (!await RequestPermissionAsync())
            return (null, "ChordPro Live isn't allowed to use the microphone. Turn it on in Settings › Privacy & Security › Microphone.");
        var input = new AudioInput(samples);
        if (input.Start() is { } error)
        {
            input.Dispose();
            return (null, error);
        }
        return (input, null);
    }

    private static Task<bool> RequestPermissionAsync()
    {
        var result = new TaskCompletionSource<bool>();
        if (OperatingSystem.IsIOSVersionAtLeast(17))
            AVAudioApplication.RequestRecordPermission(granted => result.TrySetResult(granted));
        else
            AVAudioSession.SharedInstance().RequestRecordPermission(granted => result.TrySetResult(granted));
        return result.Task;
    }

    private string? Start()
    {
        var session = AVAudioSession.SharedInstance();
        // Measurement mode turns off the voice processing (gain control, filtering) that would bend the pitch.
        if (session.SetCategory(AVAudioSessionCategory.Record) is { } categoryError)
            return categoryError.LocalizedDescription;
        session.SetMode(AVAudioSessionMode.Measurement, out _);
        if (!session.SetActive(true, out var activeError))
            return activeError?.LocalizedDescription ?? "The microphone couldn't be started.";

        var input = _engine.InputNode;
        var format = input.GetBusOutputFormat(0);
        if (format.SampleRate <= 0 || format.ChannelCount == 0)
            return "No microphone was found.";
        SampleRate = (int)format.SampleRate;
        if (OperatingSystem.IsIOSVersionAtLeast(27))
        {
            if (!input.InstallTapOnBus(0, BufferFrames, format, out var tapError, OnBuffer))
                return tapError?.LocalizedDescription ?? "The microphone couldn't be started.";
        }
        else
        {
            input.InstallTapOnBus(0, BufferFrames, format, OnBuffer);
        }
        _engine.Prepare();
        return _engine.StartAndReturnError(out var startError) ? null : startError?.LocalizedDescription ?? "The microphone couldn't be started.";
    }

    private void OnBuffer(AVAudioPcmBuffer buffer, AVAudioTime when)
    {
        var frames = (int)buffer.FrameLength;
        if (frames <= 0 || buffer.FloatChannelData == IntPtr.Zero)
            return;
        if (_buffer.Length < frames)
            _buffer = new float[frames];
        // The first channel is enough for a single string.
        Marshal.Copy(Marshal.ReadIntPtr(buffer.FloatChannelData), _buffer, 0, frames);
        _samples(_buffer, frames);
    }

    public void Dispose()
    {
        _engine.InputNode.RemoveTapOnBus(0);
        _engine.Stop();
        _engine.Dispose();
        // Let other apps' audio carry on.
        AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out NSError _);
    }
}
