using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ChordPro.Core;
using ChordPro.Core.Chords;
using ChordPro.Core.Scrolling;
using ChordPro.Core.Themes;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>Where a song is in the setlist being played: the songs either side, and how to step to them.</summary>
/// <param name="Next">"Set 2 · Bike (A)", or null at the end of the setlist.</param>
/// <param name="HasPrevious">False on the first song.</param>
internal sealed record SetlistPlace(string? Next, bool HasPrevious, Action<int> Step);

/// <summary>
/// A song on stage: paged by taps or a pedal, or scrolled (timed to its length, or musically by its tempo
/// and bars), with text size, transpose and theme; chord diagrams, easy chords, numbers, the song's tuning,
/// capo suggestions and a tuner. Its settings are shared with the Mac through the library.
/// </summary>
internal sealed class StageView : UserControl
{
    private const double TwoColumnMinWidth = 1000;
    // The Mac's usual text size; a size saved there is shown here in proportion to this device's usual size.
    private const double MacFontSize = 16;
    private const double MinScrollSpeed = 0.2, MaxScrollSpeed = 4, ScrollSpeedStep = 0.1;
    private const double MinTempo = 30, MaxTempo = 300, TempoStep = 2;
    private static readonly ScrollMode[] Modes = [ScrollMode.Manual, ScrollMode.Timed, ScrollMode.Musical];

    private readonly Song _song;
    private readonly MusicalTimeline _musical;
    private readonly SongSettings _songSettings;
    private readonly MobileSettings _settings;
    private readonly Action _saved;
    private readonly bool _keyIncludesCapo;
    private readonly SetlistPlace? _place;
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly SmartScroller _smart;
    private readonly Border _content = new() { Padding = new Thickness(16, 12) };
    // Marks the line being played while scrolling musically.
    private readonly Border _readingMarker = new() { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsVisible = false };
    private readonly Border _bar = new() { Padding = new Thickness(8, 6) };
    private readonly Border _footer = new() { Padding = new Thickness(8, 6) };
    private readonly Button _play = new();
    private readonly TextBlock _transposeText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _speedText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 72, TextAlignment = TextAlignment.Center };
    private readonly StackPanel _speedRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly TextBlock _scrollHint = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
    private readonly Button _tapTempo = new() { Content = "Tap tempo" };
    private readonly ToggleButton[] _modeButtons;
    private readonly ToggleButton _diagrams = new() { Content = "Diagrams" };
    private readonly ToggleButton _easyChords = new() { Content = "Easy chords" };
    private readonly ToggleButton _numbers = new() { Content = "Numbers" };
    private readonly ComboBox _instrumentBox = new() { MinWidth = 220 };
    private readonly ComboBox _tuningBox = new() { MinWidth = 220 };
    private readonly ComboBox _pitchBox = new() { MinWidth = 220, ItemsSource = new[] { "Standard pitch", "Half step down", "Whole step down" } };
    private readonly TextBlock _capoText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Center };
    private readonly StackPanel _capoChoices = new() { Spacing = 6 };
    private readonly Flyout _chordsFlyout = new();
    private readonly Flyout _chordFlyout = new();
    private readonly DockPanel _stage = new();
    private List<Instrument> _tuningChoices = [];
    private bool _updatingChoices;
    private readonly double _deviceFontSize =
        UIKit.UIDevice.CurrentDevice.UserInterfaceIdiom == UIKit.UIUserInterfaceIdiom.Phone ? 17 : 22;
    private readonly TapTempo _taps = new();
    private readonly Stopwatch _tapClock = Stopwatch.StartNew();
    private SongRenderer? _renderer;
    private int _columns = 1;

    /// <param name="saved">Called after the song's settings change, to share them.</param>
    /// <param name="place">The song's place in the setlist being played; null for a song opened from the list.</param>
    public StageView(Song song, SongSettings songSettings, MobileSettings settings, bool keyIncludesCapo,
        string backLabel, Action back, Action saved, SetlistPlace? place = null)
    {
        _song = song;
        _musical = MusicalTimeline.Build(song);
        _songSettings = songSettings;
        _settings = settings;
        _keyIncludesCapo = keyIncludesCapo;
        _saved = saved;
        _place = place;
        _smart = new SmartScroller(_scroller)
        {
            TimelineFactory = BuildTimeline,
            PageTarget = (direction, offset, height) => _renderer?.PageTop(direction, offset, height, _content),
        };
        _smart.RunningChanged += (_, _) => UpdateScrollControls();

        _play.Click += (_, _) => _smart.Toggle();
        _tapTempo.Click += (_, _) => TapTempo();
        _modeButtons = [.. Modes.Select(mode =>
        {
            var button = new ToggleButton { Content = mode.ToString() };
            button.Click += (_, _) => SetMode(mode);
            return button;
        })];

        var title = new TextBlock
        {
            Text = song.Title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(_play);
        buttons.Children.Add(new Button { Content = "Scroll ▾", Flyout = new Flyout { Content = ScrollPanel() } });
        buttons.Children.Add(new Button { Content = "Chords ▾", Flyout = _chordsFlyout });
        buttons.Children.Add(new Button { Content = "Aa ▾", Flyout = new Flyout { Content = TextPanel() } });
        _chordsFlyout.Content = ChordsPanel();
        _chordsFlyout.Opening += (_, _) => UpdateChordControls();
        var barContent = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        barContent.Children.Add(MakeButton($"‹ {backLabel}", back));
        Grid.SetColumn(title, 1);
        barContent.Children.Add(title);
        Grid.SetColumn(buttons, 2);
        barContent.Children.Add(buttons);
        _bar.Child = barContent;

        var root = _stage;
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        if (place is not null)
        {
            // What's coming next, so the key and the next chart are no surprise; tapping it goes there.
            var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            if (place.HasPrevious)
                footer.Children.Add(MakeButton("‹ Previous", () => place.Step(-1)));
            if (place.Next is { } next)
            {
                var nextButton = MakeButton($"Next: {next}  ›", () => place.Step(+1));
                Grid.SetColumn(nextButton, 2);
                footer.Children.Add(nextButton);
            }
            _footer.Child = footer;
            DockPanel.SetDock(_footer, Dock.Bottom);
            root.Children.Add(_footer);
        }
        _scroller.Content = _content;
        root.Children.Add(new Grid { Children = { _scroller, _readingMarker } });
        Content = root;

        Focusable = true;
        // A tap on the lower part of the song turns the page forward, on the upper part back, as on the Mac.
        _scroller.AddHandler(TappedEvent, (_, e) => Page(e.GetPosition(_scroller).Y > _scroller.Bounds.Height / 3 ? +1 : -1));
        UpdateScrollControls();
    }

    private ScrollMode Mode => Enum.TryParse<ScrollMode>(_songSettings.ScrollMode, out var mode) && Modes.Contains(mode) ? mode : ScrollMode.Timed;

    private double Speed => _songSettings.ScrollSpeed ?? 1;

    private double Tempo => _songSettings.Tempo ?? _musical.Tempo;

    private double SongFontSize => _songSettings.FontSize is { } saved ? saved / MacFontSize * _deviceFontSize : _deviceFontSize;

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.AddRange(children);
        return row;
    }

    // Scroll ▾: the mode, its speed or tempo, and tap tempo.
    private Control ScrollPanel()
    {
        _speedRow.Children.AddRange([MakeButton("−", () => StepSpeed(-1)), _speedText, MakeButton("+", () => StepSpeed(+1)), _tapTempo]);
        return new StackPanel { Spacing = 10, Children = { Row(_modeButtons), _speedRow, _scrollHint } };
    }

    // The player's instrument, for every song (as on the Mac, where it's View ▾ Diagrams for).
    private Instrument PlayerInstrument => Instrument.Find(_settings.Instrument);

    private SongTuning? CurrentTuning => SongTuning.For(_song.Tuning, _songSettings);

    // What diagrams, fingerings, capo suggestions and the tuner are for: the player's instrument, in the
    // song's tuning when that's for the same kind of instrument.
    private Instrument CurrentInstrument => CurrentTuning?.Apply(PlayerInstrument) ?? PlayerInstrument;

    private int TuningOffset => CurrentTuning?.Offset ?? 0;

    // The capo when none is chosen for the song: the file's, except on a dulcimer, where a guitar capo means nothing.
    private int DefaultCapo => CurrentInstrument.IsDiatonic ? 0 : _song.CapoFret;

    // Chords ▾: what the chords show, the instrument and the song's tuning, the capo, and the tuner.
    private Control ChordsPanel()
    {
        _diagrams.Click += (_, _) => SetChordOption(() => _settings.ShowDiagrams = _diagrams.IsChecked == true);
        _easyChords.Click += (_, _) => SetChordOption(() => _settings.SimplifyChords = _easyChords.IsChecked == true);
        _numbers.Click += (_, _) => SetChordOption(() => _settings.ChordNumbers = _numbers.IsChecked == true);
        _instrumentBox.ItemsSource = Instrument.All.Select(i => i.Name).ToList();
        _instrumentBox.SelectionChanged += (_, _) =>
        {
            if (_updatingChoices || _instrumentBox.SelectedIndex < 0)
                return;
            SetChordOption(() => _settings.Instrument = Instrument.All[_instrumentBox.SelectedIndex].Name);
        };
        _tuningBox.SelectionChanged += (_, _) => SongTuningChanged();
        _pitchBox.SelectionChanged += (_, _) => SongTuningChanged();
        var tuner = MakeButton("Tuner", ShowTuner);
        tuner.HorizontalAlignment = HorizontalAlignment.Stretch;

        static TextBlock Label(string text) => new() { Text = text, FontSize = 13, Opacity = 0.7 };
        return new ScrollViewer
        {
            MaxHeight = 560,
            Content = new StackPanel
            {
                Spacing = 8,
                MaxWidth = 340,
                Children =
                {
                    Row(_diagrams, _easyChords, _numbers),
                    Label("Instrument"),
                    _instrumentBox,
                    Label("This song's tuning"),
                    _tuningBox,
                    _pitchBox,
                    Row(new TextBlock { Text = "Capo", VerticalAlignment = VerticalAlignment.Center },
                        MakeButton("−", () => SetCapo(Capo - 1)), _capoText, MakeButton("+", () => SetCapo(Capo + 1))),
                    _capoChoices,
                    tuner,
                },
            },
        };
    }

    private int Capo => _songSettings.Capo ?? DefaultCapo;

    private void SetChordOption(Action change)
    {
        if (_updatingChoices)
            return;
        change();
        _settings.Save();
        UpdateChordControls();
        Render();
    }

    private void UpdateChordControls()
    {
        _updatingChoices = true;
        _diagrams.IsChecked = _settings.ShowDiagrams;
        _easyChords.IsChecked = _settings.SimplifyChords;
        _numbers.IsChecked = _settings.ChordNumbers;
        _instrumentBox.SelectedIndex = Instrument.All.ToList().IndexOf(PlayerInstrument);

        // The song's usual tuning first (what the file says, or the player's own), then the others for this instrument.
        var player = PlayerInstrument;
        var usual = SongTuning.Parse(_song.Tuning)?.Apply(player) ?? player;
        _tuningChoices = [usual, .. Instrument.All.Where(i => i.Family == player.Family && i != usual)];
        _tuningBox.ItemsSource = _tuningChoices
            .Select((i, index) => index == 0 && usual != player ? $"As the song says: {SongTuning.ShortName(i)}" : i.Name)
            .ToList();
        _tuningBox.SelectedIndex = _songSettings.Tuning is { } name ? Math.Max(0, _tuningChoices.FindIndex(i => i.Name == name)) : 0;
        _tuningBox.IsVisible = _tuningChoices.Count > 1;
        _pitchBox.SelectedIndex = Math.Clamp(-TuningOffset, 0, 2);
        _updatingChoices = false;

        _capoText.Text = Capo.ToString();
        _capoChoices.Children.Clear();
        var choices = CapoSuggester.ForSong(_song, _songSettings.Transpose, _keyIncludesCapo, TuningOffset, _settings.SimplifyChords, CurrentInstrument);
        if (choices.Count > 0)
            _capoChoices.Children.Add(new TextBlock { Text = "Easiest capo positions (the song sounds the same)", FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        foreach (var choice in choices)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = choice.Capo == Capo ? $"{choice.Title}  (current)" : choice.Title, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = choice.Detail, Opacity = 0.75, FontSize = 13, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            var capo = choice.Capo;
            button.Click += (_, _) => SetCapo(capo);
            _capoChoices.Children.Add(button);
        }
    }

    private void SongTuningChanged()
    {
        if (_updatingChoices || _tuningBox.SelectedIndex < 0 || _pitchBox.SelectedIndex < 0)
            return;
        // Remember only a choice that differs from what the song itself says (as the Mac does).
        var file = SongTuning.Parse(_song.Tuning);
        _songSettings.Tuning = _tuningBox.SelectedIndex == 0 ? null : _tuningChoices[_tuningBox.SelectedIndex].Name;
        var offset = -_pitchBox.SelectedIndex;
        _songSettings.TuningOffset = offset == (file?.Offset ?? 0) ? null : offset;
        _saved();
        UpdateChordControls();
        Render();
    }

    private void SetCapo(int capo)
    {
        capo = Math.Clamp(capo, 0, 11);
        _songSettings.Capo = capo == DefaultCapo ? null : capo;
        _saved();
        UpdateChordControls();
        Render();
    }

    // A chord's fingerings: the song's own first. Songs can't be changed here, so there's nothing to save.
    private void ShowChord(ChordTap tap)
    {
        List<(ChordShape, string?)> shapes = [];
        if (tap.DefinedShape is { } defined)
            shapes.Add((defined, "From the song"));
        shapes.AddRange(ChordShapeFinder.Alternatives(tap.Name, CurrentInstrument)
            .Where(s => !s.Equals(tap.DefinedShape))
            .Select(s => (s, (string?)null)));
        _chordFlyout.Hide();
        _chordFlyout.Content = new ChordPopup(tap.Name, shapes, _deviceFontSize, SongPalette.FromTheme(MainView.CurrentTheme(_settings)), CurrentInstrument);
        _chordFlyout.ShowAt(tap.Anchor);
    }

    // The tuner covers the song until Done, tuned to the song's own tuning.
    private void ShowTuner()
    {
        _chordsFlyout.Hide();
        _smart.Stop();
        Content = new TunerView(CurrentInstrument.Retuned(TuningOffset), SongPalette.FromTheme(MainView.CurrentTheme(_settings)), _settings, () =>
        {
            Content = _stage;
            Focus();
        });
    }

    // Aa ▾: text size, transpose and theme.
    private Control TextPanel() => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            Row(MakeButton("A−", () => ChangeFont(-2)), MakeButton("A+", () => ChangeFont(+2))),
            Row(new TextBlock { Text = "Transpose", VerticalAlignment = VerticalAlignment.Center },
                MakeButton("−", () => ChangeTranspose(-1)), _transposeText, MakeButton("+", () => ChangeTranspose(+1))),
            MakeButton("Next theme", NextTheme),
        },
    };

    private void ChangeFont(double step)
    {
        var size = Math.Clamp(SongFontSize + step, 12, 48);
        _songSettings.FontSize = Math.Abs(size - _deviceFontSize) < 0.5 ? null : size / _deviceFontSize * MacFontSize;
        _saved();
        Render();
    }

    private void ChangeTranspose(int step)
    {
        _songSettings.Transpose += step;
        _saved();
        Render();
    }

    private void NextTheme()
    {
        var themes = SongTheme.BuiltIn;
        var index = themes.ToList().FindIndex(t => t.Name == _settings.Theme);
        _settings.Theme = themes[(index + 1) % themes.Count].Name;
        _settings.Save();
        Render();
    }

    // The mode is the song's own, as on the Mac (where no setting means Timed).
    private void SetMode(ScrollMode mode)
    {
        _smart.Stop();
        _songSettings.ScrollMode = mode == ScrollMode.Timed ? null : mode.ToString();
        _saved();
        UpdateScrollControls();
        Render(); // two columns only make sense when nothing follows the lines
    }

    private void StepSpeed(int direction)
    {
        if (Mode == ScrollMode.Timed)
        {
            var speed = Math.Round(Math.Clamp(Speed + direction * ScrollSpeedStep, MinScrollSpeed, MaxScrollSpeed), 1);
            _songSettings.ScrollSpeed = Math.Abs(speed - 1) < 0.01 ? null : speed;
        }
        else
        {
            SetTempo(Math.Clamp(Math.Round(Tempo) + direction * TempoStep, MinTempo, MaxTempo));
        }
        _saved();
        UpdateScrollControls();
    }

    // Each tap after the first sets the tempo from the beat tapped so far (remembered for the song).
    private void TapTempo()
    {
        if (_taps.Tap(_tapClock.Elapsed) is not { } tapped)
        {
            _scrollHint.Text = "Keep tapping on the beat…";
            return;
        }
        SetTempo(Math.Clamp(Math.Round(tapped), MinTempo, MaxTempo));
        _saved();
        UpdateScrollControls();
        _scrollHint.Text = $"Tempo {Tempo:0} BPM from {_taps.Count} taps";
    }

    private void SetTempo(double tempo) =>
        _songSettings.Tempo = Math.Abs(tempo - _musical.Tempo) < 0.5 ? null : tempo;

    private void UpdateScrollControls()
    {
        var mode = Mode;
        for (var i = 0; i < Modes.Length; i++)
            _modeButtons[i].IsChecked = Modes[i] == mode;
        _play.IsVisible = _speedRow.IsVisible = mode != ScrollMode.Manual;
        _play.Content = _smart.IsRunning ? "❚❚" : "▶";
        _speedText.Text = mode == ScrollMode.Timed ? $"{Speed:0.0}×" : $"{Tempo:0} BPM";
        _tapTempo.IsVisible = mode == ScrollMode.Musical;
        _scrollHint.Text = mode switch
        {
            ScrollMode.Manual => "Turn pages by tapping the top or bottom of the song, or with a pedal.",
            ScrollMode.Timed when TimeMarkers.Find(_song).Count > 0 => "Paced by the song's time markers.",
            ScrollMode.Timed when _song.Duration is { } duration => $"Paced to finish in {duration} at 1.0×.",
            ScrollMode.Timed => "The song has no {duration}, so it scrolls at a steady speed.",
            _ when !_musical.TempoFromSong && _songSettings.Tempo is null =>
                $"The song has no {{tempo}}, so {MusicalTimeline.DefaultTempo:0} BPM is assumed; tap along to set one.",
            _ => _songSettings.Tempo is null ? "Line by line at the song's tempo." : $"Changed from the song's {_musical.Tempo:0} BPM.",
        };
        _readingMarker.IsVisible = _smart.IsRunning && mode == ScrollMode.Musical;
        _smart.Rate = mode == ScrollMode.Timed ? Speed : Tempo / _musical.Tempo;
    }

    private ScrollTimeline? BuildTimeline()
    {
        if (_renderer is null)
            return null;
        var maxOffset = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        var builder = new ScrollTimelineBuilder(_song, _renderer.LineControls, _content, _scroller.Viewport.Height, maxOffset);
        _readingMarker.Margin = new Thickness(4, builder.ReadingLineY - SongFontSize * 0.2, 0, 0);
        _readingMarker.Height = SongFontSize * 2.4;
        return Mode switch
        {
            ScrollMode.Timed => builder.Timed(),
            ScrollMode.Musical => builder.Musical(_musical),
            _ => null,
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Render();
        Focus();
#if DEBUG
        // For simulator checks: scroll in a mode as soon as the song is laid out (SIMCTL_CHILD_CHORDPRO_LIVE_SCROLL=Musical).
        if (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_SCROLL") is { } name && Enum.TryParse<ScrollMode>(name, out var mode))
        {
            SetMode(mode);
            Avalonia.Threading.DispatcherTimer.RunOnce(_smart.Start, TimeSpan.FromSeconds(1));
        }
        // CHORDPRO_LIVE_SHOW=chords opens Chords ▾, =tuner the tuner (with CHORDPRO_LIVE_TUNER=test, never the
        // microphone), =chord:G the popup for G at the song's first chord.
        switch (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_SHOW"))
        {
            case "chords":
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _chordsFlyout.ShowAt(_bar), TimeSpan.FromSeconds(1));
                break;
            case "tuner":
                ShowTuner();
                break;
            case ['c', 'h', 'o', 'r', 'd', ':', .. var chord]:
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    if (_content.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Classes.Contains("chord")) is { } anchor)
                        ShowChord(new ChordTap(chord, chord, null, anchor));
                }, TimeSpan.FromSeconds(1));
                break;
        }
#endif
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _smart.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (ColumnsFor(e.NewSize.Width) != _columns)
            Render();
        _smart.InvalidateTimeline();
    }

    // Two columns on a wide screen (an iPad in landscape), but only when paging by hand: scrolling follows
    // the lines top to bottom.
    private int ColumnsFor(double width) => width >= TwoColumnMinWidth && Mode == ScrollMode.Manual ? 2 : 1;

    // Pedals send arrow keys, Page Up/Down or Space. With a two-button pedal, forward and back are all there
    // is, so they also move through the setlist at either end of a song. Space starts and stops scrolling.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space when Mode != ScrollMode.Manual: _smart.Toggle(); break;
            case Key.Down or Key.Right or Key.PageDown or Key.Space: Page(+1); break;
            case Key.Up or Key.Left or Key.PageUp: Page(-1); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void Page(int direction)
    {
        var atEnd = direction > 0
            ? _scroller.Offset.Y >= _scroller.Extent.Height - _scroller.Viewport.Height - 1
            : _scroller.Offset.Y <= 1;
        if (atEnd && _place is not null)
            _place.Step(direction);
        else
            _smart.Page(direction);
    }

    private void Render()
    {
        var theme = MainView.CurrentTheme(_settings);
        WindowTheme.Apply(theme);
        var palette = SongPalette.FromTheme(theme);
        Background = _scroller.Background = palette.Background;
        _bar.Background = _footer.Background = palette.CommentBackground;
        _readingMarker.Background = palette.Chord;
        _scrollHint.Foreground = palette.MutedText;
        var transpose = _songSettings.Transpose;
        _transposeText.Text = transpose == 0 ? "0" : transpose.ToString("+0;−0");
        _columns = ColumnsFor(Bounds.Width);

        _renderer = new SongRenderer(new RenderOptions
        {
            FontSize = SongFontSize,
            Transpose = transpose,
            // A guitar capo in the file means nothing on a dulcimer: show the chords as heard.
            Capo = _songSettings.Capo ?? (CurrentInstrument.IsDiatonic ? 0 : null),
            ShowDiagrams = _settings.ShowDiagrams,
            Instrument = CurrentInstrument,
            SimplifyChords = _settings.SimplifyChords,
            ChordNumbers = _settings.ChordNumbers,
            Columns = _columns,
            Palette = palette,
            KeyIncludesCapo = _keyIncludesCapo,
            ChordTapped = ShowChord,
            // After the tap: transposing rebuilds the song, including the button being tapped.
            TransposeBy = shift => Avalonia.Threading.Dispatcher.UIThread.Post(() => ChangeTranspose(shift)),
            Tuning = SongTuning.Note(CurrentInstrument, PlayerInstrument, TuningOffset),
            TuningOffset = TuningOffset,
        });
        _content.Child = _renderer.Render(_song);
        _smart.InvalidateTimeline();
    }
}
