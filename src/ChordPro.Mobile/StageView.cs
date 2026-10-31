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
using ChordPro.Core.Layout;
using ChordPro.Core.Scrolling;
using ChordPro.Core.Themes;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>Where a song is in the gig being played: the songs either side, how to step to them, and the set clock.</summary>
/// <param name="Next">"Set 2 · Bike (A)", or null at the end of the setlist.</param>
/// <param name="HasPrevious">False on the first song.</param>
/// <param name="Clock">"9:41 · Set 1 · 18 min · 4 min over", asked for every few seconds.</param>
internal sealed record SetlistPlace(string? Next, bool HasPrevious, Action<int> Step, Func<string> Clock);

/// <summary>
/// A song on stage: paged by taps or a pedal, or scrolled (timed to its length, or musically by its tempo
/// and bars), with text size, transpose and theme; chord diagrams, easy chords, numbers, the song's tuning,
/// capo suggestions and a tuner. Pedals can be set up, and the stage locked against stray touches. Its
/// settings are shared with the Mac through the library.
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
    // Shades the start of the next page where it shows below this one, so each screen ends with a whole section.
    private readonly Border _nextPageShade = new() { VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Opacity = 0.85, IsVisible = false };
    private readonly Border _bar = new() { Padding = new Thickness(8, 6) };
    private readonly Border _footer = new() { Padding = new Thickness(8, 6) };
    private readonly Button _play = new();
    private readonly TextBlock _transposeText = new();
    private readonly ToggleSwitch _singThis = new();
    private readonly TextBlock _singNote = new();
    private readonly TextBlock _backToSungText = new() { VerticalAlignment = VerticalAlignment.Center };
    private StackPanel? _backToSungSection;
    private readonly TextBlock _speedLabel = new();
    private readonly TextBlock _speedText = new();
    private readonly TextBlock _scrollHint = new();
    private readonly StackPanel _speedSection = new();
    private Control? _tapTempoRow;
    private readonly ToggleButton[] _modeButtons;
    private readonly ToggleSwitch _diagrams = new();
    private readonly ToggleSwitch _easyChords = new();
    private readonly ToggleSwitch _numbers = new();
    private readonly ComboBox _instrumentBox = new();
    private readonly ComboBox _tuningBox = new();
    private readonly ComboBox _pitchBox = new() { ItemsSource = new[] { "Standard", "Half step down", "Whole step down" } };
    private Control? _tuningRow;
    private readonly TextBlock _capoText = new();
    private readonly StackPanel _capoChoices = new();
    private readonly TextBlock _fontSizeText = new();
    private readonly ComboBox _themeBox = new() { ItemsSource = SongTheme.BuiltIn.Select(t => t.Name).ToList() };
    private readonly Flyout _chordsFlyout = new();
    private readonly Flyout _chordFlyout = new();
    private readonly Flyout _textFlyout = new();
    private readonly Button _scrollButton = new() { Content = "Scroll ▾" };
    private readonly DockPanel _stage = new();
    private List<Instrument> _tuningChoices = [];
    private bool _updatingChoices;
    private readonly double _deviceFontSize =
        UIKit.UIDevice.CurrentDevice.UserInterfaceIdiom == UIKit.UIUserInterfaceIdiom.Phone ? 17 : 22;
    private readonly TapTempo _taps = new();
    private readonly TextBlock _clock = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Avalonia.Threading.DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Avalonia.Threading.DispatcherTimer _holdTimer = new() { Interval = Pedal.HoldTime };
    private readonly Avalonia.Threading.DispatcherTimer _unlockTimer = new() { Interval = TimeSpan.FromSeconds(0.8) };
    private readonly Grid _barContent = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
    private readonly Grid _lockedBar = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    private readonly Button _unlock = new() { Content = "Hold to unlock" };
    private readonly ToggleSwitch _halfPages = new();
    // In the order of PedalLayout and PedalHold.
    private readonly ComboBox _pedalLayout = new() { ItemsSource = new[] { "Pages, then songs", "Songs", "← → songs, ↑ ↓ pages" } };
    private readonly ComboBox _pedalHold = new() { ItemsSource = new[] { "Nothing more", "Change songs", "Start or stop scrolling" } };
    // The pedal button held down, while waiting to see whether it's a press or a hold.
    private PedalButton? _heldButton;
    private bool _holdDone;
    private readonly Stopwatch _tapClock = Stopwatch.StartNew();
    private SongRenderer? _renderer;
    private int _columns = 1;
#if DEBUG
    private static bool _debugHooksUsed;
#endif

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
            // Half-page turns aim at half the screen, so the line being read moves to the top half.
            PageTarget = (direction, offset, height) => _renderer?.PageTop(direction, offset, _settings.HalfPageTurns ? height / 2 : height, _content),
        };
        _smart.RunningChanged += (_, _) =>
        {
            UpdateScrollControls();
            UpdateNextPageShade();
        };
        _scroller.ScrollChanged += (_, _) => UpdateNextPageShade();

        _play.Click += (_, _) => _smart.Toggle();
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
        _scrollButton.Flyout = MenuFlyout(new Flyout(), ScrollPanel());
        buttons.Children.Add(_scrollButton);
        buttons.Children.Add(new Button { Content = "Chords ▾", Flyout = MenuFlyout(_chordsFlyout, ChordsPanel()) });
        buttons.Children.Add(new Button { Content = "Aa ▾", Flyout = MenuFlyout(_textFlyout, TextPanel()) });
        _chordsFlyout.Opening += (_, _) => UpdateChordControls();
        _textFlyout.Opening += (_, _) => UpdateTextControls();
        _barContent.Children.Add(MakeButton($"‹ {backLabel}", back));
        Grid.SetColumn(title, 1);
        _barContent.Children.Add(title);
        Grid.SetColumn(buttons, 2);
        _barContent.Children.Add(buttons);
        BuildLockedBar(song.Title ?? "");

        var root = _stage;
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        if (place is not null)
        {
            // What's coming next, so the key and the next chart are no surprise; tapping it goes there.
            var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            if (place.HasPrevious)
                footer.Children.Add(MakeButton("‹ Previous", () => place.Step(-1)));
            Grid.SetColumn(_clock, 1);
            footer.Children.Add(_clock);
            _clock.Text = place.Clock();
            _clockTimer.Tick += (_, _) => _clock.Text = place.Clock();
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
        root.Children.Add(new Grid { Children = { _scroller, _nextPageShade, _readingMarker } });
        Content = root;

        Focusable = true;
        // A tap on the lower part of the song turns the page forward, on the upper part back, as on the Mac.
        _scroller.AddHandler(TappedEvent, (_, e) => Page(e.GetPosition(_scroller).Y > _scroller.Bounds.Height / 3 ? +1 : -1));
        _holdTimer.Tick += (_, _) => PedalHeld();
        ShowLock();
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

    // A menu under the toolbar, no taller than the space below it.
    private Flyout MenuFlyout(Flyout flyout, Control menu)
    {
        flyout.Content = menu;
        flyout.FlyoutPresenterClasses.Add("menu");
        flyout.Placement = PlacementMode.BottomEdgeAlignedRight;
        flyout.Opening += (_, _) => menu.MaxHeight = Math.Max(240, Bounds.Height - _bar.Bounds.Height - 48);
        return flyout;
    }

    // Scroll ▾: the mode, its speed or tempo, and tap tempo; how pages turn and what pedals do.
    private Control ScrollPanel()
    {
        _tapTempoRow = MenuParts.ActionRow("Tap tempo", TapTempo, "♩");
        _speedSection.Children.Add(MenuParts.Section(null, (string?)null,
            MenuParts.StepperRow(_speedLabel, _speedText, () => StepSpeed(-1), () => StepSpeed(+1)), _tapTempoRow));
        _halfPages.IsChecked = _settings.HalfPageTurns;
        _halfPages.IsCheckedChanged += (_, _) =>
        {
            _settings.HalfPageTurns = _halfPages.IsChecked == true;
            _settings.Save();
            UpdateNextPageShade();
        };
        _pedalLayout.SelectedIndex = (int)_settings.PedalLayout;
        _pedalLayout.SelectionChanged += (_, _) =>
        {
            _settings.PedalLayout = (PedalLayout)Math.Max(0, _pedalLayout.SelectedIndex);
            _settings.Save();
        };
        _pedalHold.SelectedIndex = (int)_settings.PedalHold;
        _pedalHold.SelectionChanged += (_, _) =>
        {
            _settings.PedalHold = (PedalHold)Math.Max(0, _pedalHold.SelectedIndex);
            _settings.Save();
        };
        return MenuParts.Menu("Scrolling",
            MenuParts.Section(null, _scrollHint, MenuParts.Segmented(_modeButtons)),
            _speedSection,
            MenuParts.Section("Pages and pedal", "A pedal turns pages; at the end of a song in a setlist it moves to the next song.",
                MenuParts.SwitchRow("Half-page turns", _halfPages),
                MenuParts.ChoiceRow("Pedal", _pedalLayout),
                MenuParts.ChoiceRow("Hold a pedal", _pedalHold)));
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
        _diagrams.IsCheckedChanged += (_, _) => SetChordOption(() => _settings.ShowDiagrams = _diagrams.IsChecked == true);
        _easyChords.IsCheckedChanged += (_, _) => SetChordOption(() => _settings.SimplifyChords = _easyChords.IsChecked == true);
        _numbers.IsCheckedChanged += (_, _) => SetChordOption(() => _settings.ChordNumbers = _numbers.IsChecked == true);
        _instrumentBox.ItemsSource = Instrument.All.Select(i => i.Name).ToList();
        _instrumentBox.SelectionChanged += (_, _) =>
        {
            if (_updatingChoices || _instrumentBox.SelectedIndex < 0)
                return;
            SetChordOption(() => _settings.Instrument = Instrument.All[_instrumentBox.SelectedIndex].Name);
        };
        _tuningBox.SelectionChanged += (_, _) => SongTuningChanged();
        _pitchBox.SelectionChanged += (_, _) => SongTuningChanged();
        _tuningRow = MenuParts.ChoiceRow("Tuning", _tuningBox);
        return MenuParts.Menu("Chords",
            MenuParts.Section(null, (string?)null,
                MenuParts.SwitchRow("Chord diagrams", _diagrams),
                MenuParts.SwitchRow("Easy chords", _easyChords),
                MenuParts.SwitchRow("Numbers (1 4 5)", _numbers)),
            MenuParts.Section("Instrument", "Tuning, pitch and capo are for this song only.",
                MenuParts.ChoiceRow("Instrument", _instrumentBox),
                _tuningRow,
                MenuParts.ChoiceRow("Pitch", _pitchBox),
                MenuParts.StepperRow("Capo", _capoText, () => SetCapo(Capo - 1), () => SetCapo(Capo + 1))),
            _capoChoices,
            MenuParts.Section(null, (string?)null, MenuParts.ActionRow("Tuner", ShowTuner)));
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
        _tuningRow!.IsVisible = _tuningChoices.Count > 1;
        _pitchBox.SelectedIndex = Math.Clamp(-TuningOffset, 0, 2);
        _updatingChoices = false;

        _capoText.Text = Capo == 0 ? "None" : Capo.ToString();
        _capoChoices.Children.Clear();
        var choices = CapoSuggester.ForSong(_song, _songSettings.Transpose, _keyIncludesCapo, TuningOffset, _settings.SimplifyChords, CurrentInstrument);
        if (choices.Count == 0)
            return;
        // Each a row to tap, the one in use checked.
        var rows = choices.Select(choice =>
        {
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            content.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = choice.Title, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = choice.Detail, Opacity = 0.7, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.WordEllipsis },
                },
            });
            var check = new TextBlock { Text = choice.Capo == Capo ? "✓" : "", FontSize = 18, Width = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 1);
            content.Children.Add(check);
            var capo = choice.Capo;
            return (Control)MenuParts.Tappable(content, () => SetCapo(capo));
        }).ToArray();
        _capoChoices.Children.Add(MenuParts.Section("Easiest capo positions", "The song sounds the same with each.", rows));
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

    // The song's notes cover it until Done.
    private void ShowNotes()
    {
        _textFlyout.Hide();
        _smart.Stop();
        Content = new NotesView(_song.Title ?? "", _songSettings.Notes, SongPalette.FromTheme(MainView.CurrentTheme(_settings)), notes =>
        {
            if (notes != _songSettings.Notes)
            {
                _songSettings.Notes = notes;
                _saved();
            }
            Content = _stage;
            Render();
            Focus();
        });
    }

    // Aa ▾: text size, transpose, I sing this and theme; notes and the lock.
    private Control TextPanel()
    {
        _singThis.IsCheckedChanged += (_, _) =>
        {
            if (_updatingChoices)
                return;
            _songSettings.SingTranspose = _singThis.IsChecked == true ? _songSettings.Transpose : null;
            _saved();
            UpdateTextControls();
        };
        // In its own card, so hiding it leaves no stray hairline.
        _backToSungSection = MenuParts.Section(null, (string?)null, MenuParts.Tappable(_backToSungText, () =>
        {
            if (_songSettings.SingTranspose is { } sung)
                ChangeTranspose(sung - _songSettings.Transpose);
        }));
        _themeBox.SelectionChanged += (_, _) =>
        {
            if (_updatingChoices || _themeBox.SelectedIndex < 0)
                return;
            _settings.Theme = SongTheme.BuiltIn[_themeBox.SelectedIndex].Name;
            _settings.Save();
            Render();
        };
        return MenuParts.Menu("Display",
            MenuParts.Section("This song", _singNote,
                MenuParts.StepperRow("Text size", _fontSizeText, () => ChangeFont(-2), () => ChangeFont(+2)),
                MenuParts.StepperRow("Transpose", _transposeText, () => ChangeTranspose(-1), () => ChangeTranspose(+1)),
                MenuParts.SwitchRow("I sing this", _singThis),
                MenuParts.ActionRow("Notes", ShowNotes)),
            _backToSungSection,
            MenuParts.Section(null, (string?)null, MenuParts.ChoiceRow("Theme", _themeBox)),
            MenuParts.Section(null, "Locked, only page turns and pedals work. Hold to unlock.",
                MenuParts.ActionRow("Lock the stage", () => SetLocked(true), "🔒")));
    }

    private void UpdateTextControls()
    {
        _updatingChoices = true;
        _themeBox.SelectedIndex = SongTheme.BuiltIn.ToList().IndexOf(MainView.CurrentTheme(_settings));
        _updatingChoices = false;
        _fontSizeText.Text = $"{SongFontSize / _deviceFontSize:0%}";
        var transpose = _songSettings.Transpose;
        _transposeText.Text = transpose == 0 ? "0" : transpose.ToString("+0;−0");
        UpdateSingThis();
    }

    // The song's key as heard at a transposition ("A"), or the transposition for a song without a {key}.
    private string SungKeyName(int transpose) =>
        ChordTransposer.Shift(ChordTransposer.SoundingKey(_song.Key, _song.CapoFret, _keyIncludesCapo, TuningOffset), transpose) is { } key
            ? key
            : transpose == 0 ? "the original key" : transpose.ToString("+0;−0");

    private void UpdateSingThis()
    {
        var sung = _songSettings.SingTranspose;
        _updatingChoices = true;
        _singThis.IsChecked = sung is not null;
        _updatingChoices = false;
        // Twelve semitones apart is the same key.
        var elsewhere = sung is { } at && (at - _songSettings.Transpose) % 12 != 0;
        if (_backToSungSection is not null)
            _backToSungSection.IsVisible = elsewhere;
        if (sung is not { } transpose)
        {
            _singNote.Text = "Turn on I sing this for the songs you sing; the key is remembered.";
            return;
        }
        _singNote.Text = elsewhere
            ? $"You sing this in {SungKeyName(transpose)} (now in {SungKeyName(_songSettings.Transpose)})."
            : $"You sing this in {SungKeyName(transpose)}.";
        _backToSungText.Text = $"Back to {SungKeyName(transpose)}";
    }

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
        _play.IsVisible = _speedSection.IsVisible = mode != ScrollMode.Manual;
        _play.Content = _smart.IsRunning ? "❚❚" : "▶";
        _speedLabel.Text = mode == ScrollMode.Timed ? "Speed" : "Tempo";
        _speedText.Text = mode == ScrollMode.Timed ? $"{Speed:0.0}×" : $"{Tempo:0} BPM";
        _tapTempoRow!.IsVisible = mode == ScrollMode.Musical;
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
        if (_place is not null)
            _clockTimer.Start();
#if DEBUG
        // The hooks below are for the first song shown only: the variables last as long as the app, and every
        // song opened afterwards would otherwise open the same menu again.
        if (_debugHooksUsed)
            return;
        _debugHooksUsed = true;
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
            case "notes":
                ShowNotes();
                break;
            case "lock":
                SetLocked(true);
                break;
            case "text":
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _textFlyout.ShowAt(_bar), TimeSpan.FromSeconds(1));
                break;
            case "scroll":
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _scrollButton.Flyout?.ShowAt(_bar), TimeSpan.FromSeconds(1));
                break;
            case ['p', 'a', 'g', 'e', ':', .. var pages] when int.TryParse(pages, out var count):
                // =page:2 turns two pages down, a few seconds apart, so each can be screenshotted.
                for (var i = 1; i <= count; i++)
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => Page(+1), TimeSpan.FromSeconds(4 * i));
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
        _clockTimer.Stop();
        _holdTimer.Stop();
        CancelUnlock();
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

    // ---- Lock ------------------------------------------------------------------------------------

    // Locked, the toolbar is just the title and Hold to unlock, the footer's buttons don't respond and chords
    // don't open; taps on the song still turn pages, and pedals work as ever.
    private void BuildLockedBar(string title)
    {
        _lockedBar.Children.Add(new TextBlock
        {
            Text = $"🔒 {title}",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0),
        });
        Grid.SetColumn(_unlock, 1);
        _lockedBar.Children.Add(_unlock);
        // A hold, so a brush of the hand doesn't unlock it. (Button takes the pointer events itself.)
        _unlock.AddHandler(PointerPressedEvent, (_, _) =>
        {
            _unlock.Content = "Keep holding…";
            _unlockTimer.Start();
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _unlock.AddHandler(PointerReleasedEvent, (_, _) => CancelUnlock(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _unlock.PointerCaptureLost += (_, _) => CancelUnlock();
        _unlockTimer.Tick += (_, _) =>
        {
            CancelUnlock();
            SetLocked(false);
        };
    }

    private void CancelUnlock()
    {
        _unlockTimer.Stop();
        _unlock.Content = "Hold to unlock";
    }

    private void SetLocked(bool locked)
    {
        _textFlyout.Hide();
        _settings.Locked = locked;
        _settings.Save();
        ShowLock();
        Render(); // chords open popups only when unlocked
        Focus();
    }

    private void ShowLock()
    {
        var locked = _settings.Locked;
        _bar.Child = locked ? _lockedBar : _barContent;
        _footer.IsHitTestVisible = !locked;
    }

    // ---- Pedals ----------------------------------------------------------------------------------

    // Pedals send arrow keys or Page Up/Down; what they do is set up under Scroll ▾ (Pedal). Space starts and
    // stops scrolling (or turns the page when scrolling by hand).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (Mode != ScrollMode.Manual)
                _smart.Toggle();
            else
                Page(+1);
            e.Handled = true;
            return;
        }
        if (ButtonFor(e.Key) is not { } button)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        if (_settings.PedalHold == PedalHold.Nothing)
        {
            Do(Pedal.Action(button, held: false, _settings.PedalLayout, _settings.PedalHold));
            return;
        }
        // Wait to see whether it's held (a held key repeats; only the first counts).
        if (_heldButton is not null)
            return;
        _heldButton = button;
        _holdDone = false;
        _holdTimer.Start();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (ButtonFor(e.Key) is not { } button || button != _heldButton)
        {
            base.OnKeyUp(e);
            return;
        }
        e.Handled = true;
        _holdTimer.Stop();
        _heldButton = null;
        if (!_holdDone)
            Do(Pedal.Action(button, held: false, _settings.PedalLayout, _settings.PedalHold));
    }

    private void PedalHeld()
    {
        _holdTimer.Stop();
        if (_heldButton is not { } button)
            return;
        _holdDone = true;
        Do(Pedal.Action(button, held: true, _settings.PedalLayout, _settings.PedalHold));
    }

    private static PedalButton? ButtonFor(Key key) => key switch
    {
        Key.Left => PedalButton.Left,
        Key.Right => PedalButton.Right,
        Key.Up => PedalButton.Up,
        Key.Down => PedalButton.Down,
        Key.PageUp => PedalButton.PageUp,
        Key.PageDown => PedalButton.PageDown,
        _ => null,
    };

    private void Do((PedalMove Move, int Direction) action)
    {
        switch (action.Move)
        {
            case PedalMove.Page: _smart.Page(action.Direction); break;
            case PedalMove.PageThenSong: Page(action.Direction); break;
            case PedalMove.Song: _place?.Step(action.Direction); break;
            case PedalMove.ToggleScrolling when Mode != ScrollMode.Manual: _smart.Toggle(); break;
        }
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

    // Paging by hand, the part of the next page that shows at the bottom is shaded: the page turn will bring it to
    // the top. Not while scrolling (it all moves), with half-page turns, or at the end, where all that's left shows.
    private void UpdateNextPageShade()
    {
        var viewport = _scroller.Viewport.Height;
        var maxOffset = _scroller.Extent.Height - viewport;
        var offset = _scroller.Offset.Y;
        double? next = _smart.IsRunning || _settings.HalfPageTurns || offset >= maxOffset - 1 || _renderer is null
            ? null
            : _renderer.PageOffsets(viewport, _content) is { } pages ? PageLayout.Turn(pages, offset, +1) : null;
        _nextPageShade.IsVisible = next - offset < viewport;
        if (_nextPageShade.IsVisible)
            _nextPageShade.Height = viewport - (next!.Value - offset);
    }

    private void Render()
    {
        var theme = MainView.CurrentTheme(_settings);
        WindowTheme.Apply(theme);
        var palette = SongPalette.FromTheme(theme);
        Background = _scroller.Background = palette.Background;
        _bar.Background = _footer.Background = palette.CommentBackground;
        _readingMarker.Background = palette.Chord;
        _nextPageShade.Background = palette.Background;
        _clock.Foreground = palette.MutedText;
        UpdateTextControls();
        var transpose = _songSettings.Transpose;
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
            ChordTapped = _settings.Locked ? null : ShowChord,
            // After the tap: transposing rebuilds the song, including the button being tapped.
            TransposeBy = shift => Avalonia.Threading.Dispatcher.UIThread.Post(() => ChangeTranspose(shift)),
            Tuning = SongTuning.Note(CurrentInstrument, PlayerInstrument, TuningOffset),
            TuningOffset = TuningOffset,
            StageNotes = _songSettings.Notes,
        });
        _content.Child = _renderer.Render(_song);
        _smart.InvalidateTimeline();
    }
}
