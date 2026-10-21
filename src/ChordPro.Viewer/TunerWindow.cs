using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChordPro.Core.Chords;
using ChordPro.Core.Tuning;

namespace ChordPro.Viewer;

/// <summary>
/// A chromatic tuner: shows the note being played, how many cents sharp or flat it is, and which
/// of the instrument's strings it matches. Listens to the microphone while open.
/// </summary>
public sealed class TunerWindow : Window
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(1.2); // keep showing the last note this long
    private const int SmoothingFrames = 5;
    private const double MinReference = 415, MaxReference = 466;
    private static readonly IBrush InTuneBrush = new SolidColorBrush(Color.Parse("#2E9E4F"));

    private readonly Instrument _instrument;
    private readonly SongPalette _palette;
    private readonly Action<double> _saveReference;
    private readonly Microphone? _microphone;
    private readonly PitchDetector? _detector;
    private readonly object _lock = new();
    private readonly float[] _ring = [];
    private int _ringPosition;
    private readonly float[] _analysis = [];
    private readonly Queue<double> _recent = new();
    private DateTime _lastHeard;
    private double _reference;

    private readonly TextBlock _noteName = new() { FontSize = 84, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _octave = new() { FontSize = 28, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 0, 16) };
    private readonly TextBlock _detail = new() { FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7 };
    private readonly TextBlock _referenceText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96, TextAlignment = TextAlignment.Center };
    private readonly TunerMeter _meter = new() { Margin = new Thickness(8, 4) };
    private readonly WrapPanel _strings = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _timer;

    public TunerWindow() : this(Instrument.Guitar, SongPalette.FromTheme(Core.Themes.SongTheme.Day), Note.StandardReference, _ => { }) { }

    /// <param name="listen">Open the microphone; when false, audio is supplied with <see cref="Feed"/> (for testing).</param>
    public TunerWindow(Instrument instrument, SongPalette palette, double reference, Action<double> saveReference, bool listen = true)
    {
        _instrument = instrument;
        _palette = palette;
        _reference = Math.Clamp(reference, MinReference, MaxReference);
        _saveReference = saveReference;

        Title = $"Tuner · {instrument.Name}";
        Width = 440;
        Height = 470;
        CanResize = false;
        Background = palette.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _noteName.Foreground = _octave.Foreground = palette.Text;
        _detail.Foreground = _status.Foreground = _referenceText.Foreground = palette.Text;
        _meter.Foreground = palette.MutedText;
        _meter.Needle = palette.Chord;
        _meter.InTune = InTuneBrush;

        var flat = new Button { Content = "−" };
        var sharp = new Button { Content = "+" };
        ToolTip.SetTip(flat, "Lower reference pitch");
        ToolTip.SetTip(sharp, "Raise reference pitch");
        flat.Click += (_, _) => SetReference(_reference - 1);
        sharp.Click += (_, _) => SetReference(_reference + 1);
        var referenceRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { flat, _referenceText, sharp },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20, 16),
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Height = 112,
                    Children = { _noteName, _octave },
                },
                _meter,
                _detail,
                _strings,
                referenceRow,
                _status,
            },
        };

        foreach (var midi in instrument.Tuning)
        {
            _strings.Children.Add(new Border
            {
                Margin = new Thickness(4),
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = palette.ChorusBar,
                Tag = midi,
                Child = new TextBlock { Text = Note.NameOf(midi), Foreground = palette.Text, FontWeight = FontWeight.SemiBold },
            });
        }

        SetReference(_reference);
        ShowNote(null);

        string? error = null;
        _microphone = listen ? Microphone.TryOpen(out error) : null;
        if (listen && _microphone is null)
        {
            _status.Text = $"Can't use the microphone: {error}";
        }
        else
        {
            _detector = new PitchDetector(_microphone?.SampleRate ?? TestSampleRate);
            _ring = new float[_detector.RequiredSamples * 2];
            _analysis = new float[_detector.RequiredSamples];
            if (_microphone is not null)
            {
                _microphone.SamplesReceived += OnSamples;
                _status.Text = $"Listening on {_microphone.DeviceName}";
            }
        }

        _timer = new DispatcherTimer(UpdateInterval, DispatcherPriority.Background, (_, _) => Analyse());
        _timer.Start();
    }

    /// <summary>Sample rate assumed for audio supplied with <see cref="Feed"/>.</summary>
    public const int TestSampleRate = 48000;

    /// <summary>Supplies audio directly instead of from the microphone.</summary>
    public void Feed(ReadOnlySpan<float> samples) => OnSamples(samples);

    private void OnSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var sample in samples)
            {
                _ring[_ringPosition] = sample;
                _ringPosition = (_ringPosition + 1) % _ring.Length;
            }
        }
    }

    private void Analyse()
    {
        if (_detector is null)
            return;

        lock (_lock)
        {
            // The most recent samples, oldest first.
            var start = (_ringPosition - _analysis.Length + _ring.Length) % _ring.Length;
            for (var i = 0; i < _analysis.Length; i++)
                _analysis[i] = _ring[(start + i) % _ring.Length];
        }

        if (_detector.Detect(_analysis) is { Clarity: > 0.8 } pitch)
        {
            // A short median smooths the jitter of a ringing string without lagging much.
            _recent.Enqueue(pitch.Frequency);
            while (_recent.Count > SmoothingFrames)
                _recent.Dequeue();
            _lastHeard = DateTime.UtcNow;
            var sorted = _recent.Order().ToList();
            ShowNote(Note.FromFrequency(sorted[sorted.Count / 2], _reference));
        }
        else if (DateTime.UtcNow - _lastHeard > HoldTime)
        {
            _recent.Clear();
            ShowNote(null);
        }
    }

    private void ShowNote(Note? heard)
    {
        if (heard is not { } note)
        {
            _noteName.Text = "–";
            _octave.Text = "";
            _detail.Text = "Play a single string";
            _meter.Cents = null;
            _noteName.Foreground = _palette.MutedText;
            HighlightString(null);
            return;
        }

        var inTune = Math.Abs(note.Cents) <= TunerMeter.InTuneCents;
        _noteName.Text = note.Name;
        _octave.Text = note.Octave.ToString();
        _noteName.Foreground = inTune ? InTuneBrush : _palette.Text;
        _meter.Cents = note.Cents;
        var cents = $"{(note.Cents >= 0 ? "+" : "−")}{Math.Abs(note.Cents):0} cents";
        var advice = inTune ? "In tune" : note.Cents < 0 ? "Flat · tune up" : "Sharp · tune down";
        _detail.Text = $"{advice}   ·   {cents}   ·   {note.Frequency:0.0} Hz";
        HighlightString(note.Midi);
    }

    private void HighlightString(int? midi)
    {
        foreach (var child in _strings.Children.OfType<Border>())
        {
            var match = child.Tag is int stringMidi && stringMidi == midi;
            child.BorderBrush = match ? _palette.Chord : _palette.ChorusBar;
            child.Background = match ? _palette.CommentBackground : null;
        }
    }

    private void SetReference(double reference)
    {
        _reference = Math.Clamp(Math.Round(reference), MinReference, MaxReference);
        _referenceText.Text = $"A4 = {_reference:0} Hz";
        _saveReference(_reference);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (_microphone is not null)
        {
            _microphone.SamplesReceived -= OnSamples;
            _microphone.Dispose();
        }
        base.OnClosed(e);
    }
}
