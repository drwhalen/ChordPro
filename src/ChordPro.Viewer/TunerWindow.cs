using Avalonia.Controls;
using ChordPro.Core.Chords;
using ChordPro.Core.Tuning;

namespace ChordPro.Viewer;

/// <summary>The <see cref="TunerPanel"/> in its own window, listening to the microphone while open.</summary>
public sealed class TunerWindow : Window
{
    private readonly TunerPanel _panel;
    private readonly Microphone? _microphone;

    public TunerWindow() : this(Instrument.Guitar, SongPalette.FromTheme(Core.Themes.SongTheme.Day), Note.StandardReference, _ => { }) { }

    /// <param name="listen">Open the microphone; when false, audio is supplied with <see cref="Feed"/> (for testing).</param>
    public TunerWindow(Instrument instrument, SongPalette palette, double reference, Action<double> saveReference, bool listen = true)
    {
        Title = $"Tuner · {instrument.Name}";
        Width = 440;
        Height = 470;
        CanResize = false;
        Background = palette.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _panel = new TunerPanel(instrument, palette, reference, saveReference);

        string? error = null;
        _microphone = listen ? Microphone.TryOpen(out error) : null;
        if (listen && _microphone is null)
        {
            _panel.Status = $"Can't use the microphone: {error}";
            return;
        }
        _panel.Start(_microphone?.SampleRate ?? TestSampleRate);
        if (_microphone is not null)
        {
            _microphone.SamplesReceived += _panel.Feed;
            _panel.Status = $"Listening on {_microphone.DeviceName}";
        }
    }

    /// <summary>Sample rate assumed for audio supplied with <see cref="Feed"/>.</summary>
    public const int TestSampleRate = 48000;

    /// <summary>Supplies audio directly instead of from the microphone.</summary>
    public void Feed(ReadOnlySpan<float> samples) => _panel.Feed(samples);

    protected override void OnClosed(EventArgs e)
    {
        _panel.Stop();
        if (_microphone is not null)
        {
            _microphone.SamplesReceived -= _panel.Feed;
            _microphone.Dispose();
        }
        base.OnClosed(e);
    }
}
