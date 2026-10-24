using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core.Chords;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>The tuner, over the song, listening to the microphone while it's shown.</summary>
internal sealed class TunerView : UserControl
{
    private readonly TunerPanel _panel;
    private AudioInput? _input;
    private bool _closed;

    public TunerView(Instrument instrument, SongPalette palette, MobileSettings settings, Action done)
    {
        Background = palette.Background;
        _panel = new TunerPanel(instrument, palette, settings.TunerReference, reference =>
        {
            settings.TunerReference = reference;
            settings.Save();
        });
        var doneButton = new Button { Content = "Done" };
        doneButton.Click += (_, _) => done();
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(new TextBlock
        {
            Text = $"Tuner · {instrument.Name}",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
            Foreground = palette.Text,
        });
        Grid.SetColumn(doneButton, 1);
        top.Children.Add(doneButton);
        var bar = new Border { Padding = new Thickness(8, 6), Background = palette.CommentBackground, Child = top };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new ScrollViewer
        {
            Content = new Border { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _panel },
        });
        Content = root;
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
#if DEBUG
        // For simulator checks, which must never open the microphone: a steady A2 (the guitar's A string), 8 cents flat.
        if (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_TUNER") == "test")
        {
            const int rate = 48000;
            var tone = new float[rate];
            for (var i = 0; i < tone.Length; i++)
                tone[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 110 * Math.Pow(2, -8 / 1200.0) * i / rate));
            _panel.Start(rate);
            _panel.Feed(tone);
            _panel.Status = "Test tone";
            return;
        }
#endif
        _panel.Status = "Starting the microphone…";
        var (input, error) = await AudioInput.OpenAsync((samples, count) => _panel.Feed(samples.AsSpan(0, count)));
        if (_closed)
        {
            // Done was tapped while iOS was asking.
            input?.Dispose();
            return;
        }
        if (input is null)
        {
            _panel.Status = error ?? "";
            return;
        }
        _input = input;
        _panel.Start(input.SampleRate);
        _panel.Status = "Listening";
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _closed = true;
        _panel.Stop();
        _input?.Dispose();
        _input = null;
        base.OnDetachedFromVisualTree(e);
    }
}
