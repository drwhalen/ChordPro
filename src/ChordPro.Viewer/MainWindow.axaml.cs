using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChordPro.Core;
using ChordPro.Core.Chords;
using ChordPro.Core.Layout;
using ChordPro.Core.Scrolling;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ChordProFiles = new("ChordPro songs")
    {
        Patterns = [.. SongLibrary.FileExtensions.Select(e => "*" + e), "*" + SongLibrary.TextExtension],
    };

    private static readonly FilePickerFileType SetlistFiles = new("Setlists") { Patterns = ["*" + Setlist.FileExtension, "*" + SongLibrary.TextExtension] };

    private static readonly FilePickerFileType ViewerSetlistFiles = new("ChordPro Viewer setlist") { Patterns = ["*" + Setlist.FileExtension] };

    private static readonly FilePickerFileType LivePrompterSetlistFiles = new("LivePrompter setlist") { Patterns = ["*" + SongLibrary.TextExtension] };

    private const double DefaultFontSize = 16, MinFontSize = 10, MaxFontSize = 40;
    private const double StageFontScale = 1.4;
    private const double TwoColumnMinWidth = 1000;
    private const double MinScrollSpeed = 0.2, MaxScrollSpeed = 4, ScrollSpeedStep = 0.1;
    private const double MinTempo = 30, MaxTempo = 300, TempoStep = 2;
    private const double TapZone = 0.4;            // top/bottom fraction of the song that turns pages
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(200);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SmartScroller _scroller;
    private TunerWindow? _tuner;
    private readonly Flyout _chordFlyout = new() { Placement = PlacementMode.Bottom, ShowMode = FlyoutShowMode.Standard };
    private readonly DispatcherTimer _reloadTimer;
    private FileSystemWatcher? _watcher;

    private Song? _song;
    private SongRenderer? _renderer;
    private MusicalTimeline? _musical;
    private string? _path;
    private string? _loadedText;

    // Editing: the text as last saved (with "\n" line endings), how the file is written, and whether
    // it changed on disk while there were unsaved edits.
    private bool _editing;
    private string _savedText = "";
    private TextFileFormat _fileFormat = TextFileFormat.Default;
    private bool _changedOnDisk;
    private bool _settingEditorText;
    private bool _closeConfirmed;
    private IReadOnlyList<string> _chordButtonChords = [];
    private readonly DispatcherTimer _previewTimer;
    private double _fontSize = DefaultFontSize;
    private int _transpose;
    private int? _capo;
    private ScrollMode _scrollMode = ScrollMode.Timed;
    private double _speed = 1;
    private double? _tempo;
    private TapTempo _tapTempo = new();
    private readonly Stopwatch _tapClock = Stopwatch.StartNew();
    private AudioFollower? _audio;
    // Following the voice: the listener, where the lyrics place the player, and when that was heard.
    private VoiceFollower? _voice;
    private LyricAligner? _aligner;
    private (double Time, DateTime At)? _voiceTarget;
    private string? _lastHeard;
    private int _unmatched;
    private bool _downloadingModel;
    private bool _countedPlay;   // this song's play is counted once per opening

    /// <summary>Makes the voice listener instead of opening the microphone (for testing with recordings).</summary>
    public Func<VoiceFollower?>? CreateVoiceFollower { get; set; }
    private DateTime _lastStatusUpdate;
    private Point? _pressPosition;
    private bool _updatingScrollMode;
    private bool _stageMode;
    private int _renderedColumns = 1;
    private WindowState _windowStateBeforeStage;

    private List<SongInfo> _library = [];
    // Groups the user expanded while browsing, and ones they collapsed in the current search or filter.
    private readonly HashSet<string> _expandedGroups = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _collapsedFilteredGroups = new(StringComparer.CurrentCultureIgnoreCase);
    private bool _updatingLibraryFilters;
    private List<string> _librarySetlists = [];
    private FileSystemWatcher? _libraryWatcher;
    private readonly DispatcherTimer _libraryRescanTimer;
    private LibraryCheckWindow? _checkWindow;
    private readonly Setlist _setlist = new();
    private string? _setlistPath;
    private bool _updatingLists;
    private bool _updatingViewOptions;

    public MainWindow() : this(null) { }

    public MainWindow(string? initialPath)
    {
        InitializeComponent();

        _scroller = new SmartScroller(SongScroller) { TimelineFactory = BuildTimeline, PageTarget = PageTarget };
        _scroller.RunningChanged += (_, _) => OnScrollRunningChanged();
        _scroller.Progressed += (_, _) => UpdateScrollStatus();
        _scroller.ScrolledByHand += OnScrolledByHand;
        ErrorLog.Recovered += message =>
            StatusText.Text = $"Something went wrong ({message}). Details are in {ErrorLog.FilePath}";
        // Tap the top or bottom of the song to turn pages in manual mode.
        SongScroller.AddHandler(PointerPressedEvent, OnSongPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SongScroller.AddHandler(PointerReleasedEvent, OnSongPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _reloadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => ReloadChangedFile());
        // Re-render the preview once typing pauses, not on every key.
        _previewTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => PreviewEdits());
        // A converter or sync can write many files at once; rescan once they've settled.
        _libraryRescanTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) =>
        {
            _libraryRescanTimer!.Stop();
            _ = ScanLibraryAsync(quiet: true);
        });

        AddHandler(DragDrop.DropEvent, OnDrop);
        // Tunnel so shortcuts work even when a list or the scroll view has focus.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        // With the System theme, follow the OS switching between light and dark.
        ActualThemeVariantChanged += (_, _) => RefreshThemeColors();

        ApplySavedSettings();

        if (initialPath is not null)
            Open(initialPath);
        else if (_settings.RecentFiles.FirstOrDefault(File.Exists) is { } recent)
            LoadFile(recent);
        else
            ShowMessage("Open a ChordPro file, choose a library folder, or drop a song onto this window.");
    }

    // ---- Loading ---------------------------------------------------------------------------

    // Every way of opening a song comes through here, so unsaved edits are never dropped by
    // opening another one. Without unsaved edits it loads straight away.
    private async void LoadFile(string path)
    {
        if (!await ConfirmKeepOrDiscardEditsAsync())
        {
            SyncListSelections(); // put the list highlight back on the song being edited
            return;
        }
        LoadSong(path);
    }

    private void LoadSong(string path)
    {
        Song song;
        string text;
        try
        {
            text = File.ReadAllText(path);
            song = ChordProParser.Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Could not open {Path.GetFileName(path)}:\n{ex.Message}");
            return;
        }

        _scroller.Stop();
        _song = song;
        _musical = MusicalTimeline.Build(song);
        _loadedText = text;
        _path = Path.GetFullPath(path);

        var songSettings = _settings.ForSong(_path);
        _transpose = songSettings.Transpose;
        _capo = songSettings.Capo;
        _fontSize = songSettings.FontSize ?? DefaultFontSize;
        _speed = songSettings.ScrollSpeed ?? 1;
        _tempo = songSettings.Tempo;
        _tapTempo = new TapTempo();
        _countedPlay = false;
        _scrollMode = Enum.TryParse<ScrollMode>(songSettings.ScrollMode, out var mode) ? mode : ScrollMode.Timed;
        ApplyScrollRate();

        _settings.AddRecentFile(_path);
        _settings.Save();

        Title = song.Title is { } title ? $"{title} — ChordPro Viewer" : "ChordPro Viewer";
        StatusText.Text = Path.GetFileName(_path);
        WatchFile(_path);
        UpdateSongControls();
        RenderSong();
        SongScroller.Offset = Vector.Zero;
        RefreshRecentList();
        RevealInLibrary(_path);
        SyncListSelections();
        UpdateNextSong();
        if (_editing)
            LoadEditor();
    }

    // Auto-reload: re-read the file when another program saves it, keeping the scroll position.
    private void WatchFile(string path)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
        _watcher.EnableRaisingEvents = true;
    }

    private void ReloadChangedFile()
    {
        _reloadTimer.Stop();
        if (_path is null || !File.Exists(_path))
            return;
        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _reloadTimer.Start(); // still being written, or iCloud is busy with it; try again shortly
            return;
        }
        // File events can arrive without a real change (e.g. when watching starts on macOS).
        if (text == _loadedText)
            return;
        _loadedText = text;
        if (_editing)
        {
            // Unsaved edits are never replaced without asking; otherwise the editor follows the file.
            if (IsDirty)
            {
                _changedOnDisk = true;
                StatusText.Text = $"{Path.GetFileName(_path)} changed on disk. Save keeps your version; Revert loads the new one.";
                return;
            }
            LoadEditor();
            StatusText.Text = $"{Path.GetFileName(_path)} — reloaded {DateTime.Now:t}";
            return;
        }
        _song = ChordProParser.Parse(text);
        _musical = MusicalTimeline.Build(_song);
        ApplyScrollRate();

        var offset = SongScroller.Offset;
        RenderSong();
        SongScroller.UpdateLayout();
        SongScroller.Offset = offset;
        StatusText.Text = $"{Path.GetFileName(_path)} — reloaded {DateTime.Now:t}";
    }

    private void RenderSong()
    {
        if (_song is null)
            return;

        _renderedColumns = _stageMode && Bounds.Width >= TwoColumnMinWidth && AllowsColumns ? 2 : 1;
        _renderer = new SongRenderer(CurrentRenderOptions() with { Columns = _renderedColumns });
        SongHost.Content = _renderer.Render(_song);
        _scroller.InvalidateTimeline();
    }

    // Following the song line by line needs the lines in one column, top to bottom.
    private bool AllowsColumns =>
        _scrollMode == ScrollMode.Manual ||
        (_scrollMode == ScrollMode.Timed && _song is not null && TimeMarkers.Find(_song).Count == 0);

    private RenderOptions CurrentRenderOptions() => new()
    {
        FontSize = _fontSize * (_stageMode ? StageFontScale : 1),
        Transpose = _transpose,
        Capo = _capo,
        ShowChords = _settings.ShowChords,
        ShowDiagrams = _settings.ShowDiagrams,
        Instrument = Instrument.All.FirstOrDefault(i => i.Name == _settings.Instrument) ?? Instrument.Guitar,
        Palette = SongPalette.FromTheme(CurrentTheme),
        SimplifyChords = _settings.SimplifyChords,
        KeyIncludesCapo = _settings.KeyIncludesCapo,
        ChordTapped = ShowChordPopup,
    };

    private void ShowMessage(string message)
    {
        _song = null;
        SongHost.Content = new TextBlock
        {
            Text = message,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 80, 0, 0),
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // In stage mode the number of columns depends on the window width.
        if (change.Property != BoundsProperty)
            return;
        if (_stageMode && (Bounds.Width >= TwoColumnMinWidth && AllowsColumns ? 2 : 1) != _renderedColumns)
            RenderSong();
        else
            _scroller.InvalidateTimeline(); // line positions move when the window is resized
    }

    // ---- Per-song settings: text size, transpose, capo, scroll speed -------------------------

    private void SaveSongSettings()
    {
        if (_path is null)
            return;
        var songSettings = _settings.ForSong(_path);
        songSettings.Transpose = _transpose;
        songSettings.Capo = _capo;
        songSettings.FontSize = _fontSize == DefaultFontSize ? null : _fontSize;
        songSettings.ScrollSpeed = Math.Abs(_speed - 1) < 0.01 ? null : _speed;
        songSettings.ScrollMode = _scrollMode == ScrollMode.Timed ? null : _scrollMode.ToString();
        songSettings.Tempo = _tempo;
        _settings.Save();
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => SetFontSize(_fontSize + 2);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => SetFontSize(_fontSize - 2);

    private void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
        SaveSongSettings();
        RenderSong();
    }

    private void OnTransposeDownClick(object? sender, RoutedEventArgs e) => SetTranspose(_transpose - 1);

    private void OnTransposeUpClick(object? sender, RoutedEventArgs e) => SetTranspose(_transpose + 1);

    private void OnTransposeResetClick(object? sender, RoutedEventArgs e) => SetTranspose(0);

    private void SetTranspose(int semitones)
    {
        // Twelve semitones is the same key again, so wrap around within an octave.
        _transpose = semitones % 12;
        SaveSongSettings();
        UpdateSongControls();
        RenderSong();
    }

    private int FileCapo => _song is null ? 0 : SongRenderer.FileCapo(_song);

    private void OnCapoDownClick(object? sender, RoutedEventArgs e) => SetCapo((_capo ?? FileCapo) - 1);

    private void OnCapoUpClick(object? sender, RoutedEventArgs e) => SetCapo((_capo ?? FileCapo) + 1);

    private void OnCapoResetClick(object? sender, RoutedEventArgs e) => SetCapo(null);

    private void SetCapo(int? capo)
    {
        capo = capo is { } c ? Math.Clamp(c, 0, 11) : null;
        _capo = capo == FileCapo ? null : capo;
        SaveSongSettings();
        UpdateSongControls();
        RenderSong();
    }

    // ---- Chord popups and capo suggestions -----------------------------------------------------

    private Instrument CurrentInstrument => Instrument.All.FirstOrDefault(i => i.Name == _settings.Instrument) ?? Instrument.Guitar;

    private void OnTunerClick(object? sender, RoutedEventArgs e)
    {
        if (_tuner is not null)
        {
            _tuner.Activate();
            return;
        }
        _tuner = new TunerWindow(CurrentInstrument, SongPalette.FromTheme(CurrentTheme), _settings.TunerReference, reference =>
        {
            _settings.TunerReference = reference;
            _settings.Save();
        });
        _tuner.Closed += (_, _) => _tuner = null;
        _tuner.Show(this);
    }

    private void ShowChordPopup(ChordTap tap)
    {
        var shapes = new List<(ChordShape, string?)>();
        if (tap.DefinedShape is { } defined)
            shapes.Add((defined, "From the song"));
        shapes.AddRange(ChordShapeFinder.Alternatives(tap.Name, CurrentInstrument)
            .Where(s => !s.Equals(tap.DefinedShape))
            .Select(s => (s, (string?)null)));

        // One popup at a time: tapping another chord replaces it.
        _chordFlyout.Hide();
        _chordFlyout.Content = new ChordPopup(tap.Name, shapes, _fontSize, SongPalette.FromTheme(CurrentTheme));
        _chordFlyout.ShowAt(tap.Anchor);
    }

    private void OnCapoSuggestOpening(object? sender, EventArgs e)
    {
        CapoSuggestions.Children.Clear();
        if (_song is null)
            return;

        // Work from the chords as heard, so every suggestion sounds the same as now.
        var sounding = _transpose + FileCapo;
        var writtenKey = ChordTransposer.WrittenKey(_song.Key, FileCapo, _settings.KeyIncludesCapo);
        var preferFlats = ChordTransposer.PrefersFlats(writtenKey, sounding);
        var chords = _song.ChordUsages()
            .Select(c => ChordTransposer.Transpose(_settings.SimplifyChords ? ChordSimplifier.Simplify(c) : c, sounding, preferFlats))
            .GroupBy(c => c)
            .Select(g => (g.Key, g.Count()))
            .ToList();
        if (chords.Count == 0)
        {
            CapoSuggestions.Children.Add(new TextBlock { Text = "This song has no chords." });
            return;
        }

        var soundingKey = writtenKey is { } key ? ChordTransposer.Transpose(key, sounding, preferFlats) : null;
        var current = _capo ?? FileCapo;
        CapoSuggestions.Children.Add(new TextBlock { Text = "Easiest capo positions (the song sounds the same)", Opacity = 0.6, Margin = new Thickness(0, 0, 0, 4) });

        foreach (var suggestion in CapoSuggester.Suggest(chords, CurrentInstrument, soundingKey).Take(4))
        {
            var shapesKey = soundingKey is null ? null
                : ChordTransposer.Transpose(soundingKey, -suggestion.Capo, ChordTransposer.PrefersFlats(soundingKey, -suggestion.Capo));
            var title = suggestion.Capo == 0 ? "No capo" : $"Capo {suggestion.Capo}";
            if (shapesKey is not null)
                title += $" · {shapesKey} shapes";
            if (suggestion.Capo == current)
                title += "  (current)";
            var barre = suggestion.BarreChords switch { 0 => "no barre chords", 1 => "1 barre chord", var n => $"{n} barre chords" };

            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = $"{string.Join("  ", suggestion.Chords)}  ·  {barre}", Opacity = 0.75, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 },
                    },
                },
            };
            var capo = suggestion.Capo;
            button.Click += (_, _) =>
            {
                CapoSuggestButton.Flyout?.Hide();
                SetCapo(capo);
            };
            CapoSuggestions.Children.Add(button);
        }
    }

    private void UpdateSongControls()
    {
        TransposeResetButton.Content = _transpose > 0 ? $"+{_transpose}" : _transpose < 0 ? $"−{-_transpose}" : "0";
        TransposeResetButton.IsEnabled = _transpose != 0;
        CapoResetButton.Content = (_capo ?? FileCapo).ToString();
        CapoResetButton.IsEnabled = _capo is not null;
        FitsVoiceToggle.IsChecked = _path is not null && _settings.Songs.GetValueOrDefault(_path)?.FitsVoice == true;
        UpdateScrollControls();
    }

    private void OnFitsVoiceClick(object? sender, RoutedEventArgs e)
    {
        if (_path is null || _song is null)
        {
            FitsVoiceToggle.IsChecked = false;
            return;
        }
        var fits = FitsVoiceToggle.IsChecked == true;
        _settings.ForSong(_path).FitsVoice = fits;
        _settings.Save();
        var key = ChordTransposer.Shift(ChordTransposer.SoundingKey(_song.Key, FileCapo, _settings.KeyIncludesCapo), _transpose);
        StatusText.Text = fits
            ? $"Noted: {_song.Title ?? "this song"}{(key is null ? "" : $" in {key}")} sits well for your voice"
            : $"No longer marked as fitting your voice";
    }

    // The keys the singer is comfortable in: songs they marked, and songs they transposed.
    private VoiceRange LearnedVoice()
    {
        var byPath = _library.ToDictionary(s => s.Path);
        var keys = _settings.Songs
            .Where(s => s.Value.FitsVoice || s.Value.Transpose != 0)
            .Select(s => byPath.GetValueOrDefault(s.Key))
            .OfType<SongInfo>()
            .Select(s => PlannedFor(s).Key)
            .OfType<string>();
        return new VoiceRange([.. keys]);
    }

    // ---- Scrolling: manual, timed, musical, follow audio ------------------------------------

    private void OnScrollModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingScrollMode || ScrollModeBox.SelectedIndex < 0)
            return;
        _scroller.Stop();
        _scrollMode = (ScrollMode)ScrollModeBox.SelectedIndex;
        ApplyScrollRate();
        SaveSongSettings();
        UpdateScrollControls();
        RenderSong(); // stage mode may switch between one and two columns
    }

    private void OnScrollToggleClick(object? sender, RoutedEventArgs e) => ToggleScrolling();

    private void ToggleScrolling()
    {
        if (_scroller.IsRunning)
        {
            _scroller.Stop();
            return;
        }
        if (_scrollMode == ScrollMode.Manual || _song is null)
        {
            UpdateScrollControls();
            return;
        }
        if (_scrollMode == ScrollMode.FollowAudio && _audio is null)
        {
            _audio = AudioFollower.TryStart(CurrentTempo, out var error);
            if (_audio is null)
            {
                StatusText.Text = $"Can't use the microphone: {error}";
                UpdateScrollControls();
                return;
            }
        }
        if (_scrollMode == ScrollMode.FollowVoice && _voice is null && !StartVoice())
            return;
        _scroller.Start();
        CountPlay();
        if (_scrollMode == ScrollMode.FollowVoice)
            OnScrolledByHand(_scroller.Time); // pick up the lyrics from where the view is
    }

    // Opens the microphone and speech recognition; the first time, downloads the model instead.
    private bool StartVoice()
    {
        if (!SpeechModel.IsDownloaded)
        {
            _ = DownloadSpeechModelAsync();
            UpdateScrollControls();
            return false;
        }
        StatusText.Text = "Loading speech recognition…";
        string? error = "no listener";
        _voice = CreateVoiceFollower is { } create ? create() : VoiceFollower.TryStart(SpeechModel.FilePath, out error);
        if (_voice is null)
        {
            StatusText.Text = $"Can't follow your voice: {error}";
            UpdateScrollControls();
            return false;
        }
        _aligner = new LyricAligner(_song!);
        (_voiceTarget, _lastHeard, _unmatched) = (null, null, 0);
        var voice = _voice;
        voice.Heard += (text, at) => Dispatcher.UIThread.Post(() =>
        {
            if (_voice == voice)
                OnVoiceHeard(text, at);
        });
        return true;
    }

    private async Task DownloadSpeechModelAsync()
    {
        if (_downloadingModel)
            return;
        _downloadingModel = true;
        UpdateScrollControls();
        try
        {
            var progress = new Progress<double>(p =>
                StatusText.Text = $"Downloading the speech model, {p:P0} of {SpeechModel.ApproximateBytes / 1e6:0} MB (one time only)…");
            await SpeechModel.DownloadAsync(progress);
            StatusText.Text = "Speech model ready. Press Listen and start singing.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            StatusText.Text = $"Couldn't download the speech model: {ex.Message}";
        }
        finally
        {
            _downloadingModel = false;
            UpdateScrollControls();
        }
    }

    // Place the player by the words heard; after several stretches that match nothing (a long
    // instrumental, or a jump to another part), look for them in the whole song again.
    private void OnVoiceHeard(string text, DateTime at)
    {
        if (_aligner is null || _musical is null || !_scroller.IsRunning)
            return;
        _lastHeard = text;
        // The time lets the aligner work out the singer's pace, which is what places them in repeated lines.
        if (_aligner.Hear(text, (at - DateTime.UnixEpoch).TotalSeconds) is { } line &&
            TimelineTime(line.Section, line.LineIndex, _aligner.CurrentFraction) is { } time)
        {
            _voiceTarget = (time, at);
            _unmatched = 0;
        }
        else if (++_unmatched >= 6)
        {
            _aligner.Reset();
            _unmatched = 0;
        }
        UpdateScrollStatus(force: true);
    }

    // Scrolled by hand (or just started): follow the lyrics from the line now in view.
    private void OnScrolledByHand(double time)
    {
        if (_aligner is null || _musical is null)
            return;
        _voiceTarget = null;
        if (time <= 0)
            _aligner.Reset();
        else if (_musical.LineAt((time + 60 / _musical.Tempo) / _musical.SecondsPerBar) is { } line)
            _aligner.MoveTo(line.Section, line.LineIndex);
    }

    // When a lyric line, or a point through it, reaches the reading position on the musical timeline
    // (as ScrollTimelineBuilder.Musical places lines: a beat early).
    private double? TimelineTime(Section section, int lineIndex, double fraction)
    {
        if (_musical?.Lines.FirstOrDefault(l => l.Section == section && l.LineIndex == lineIndex) is not { } line)
            return null;
        return Math.Max(0, (line.Start + fraction * line.Length) * _musical.SecondsPerBar - 60 / _musical.Tempo);
    }

    // Where the singing has got to, moved on by the time since it was heard.
    private double? VoiceTargetTime() =>
        _voiceTarget is { } heard ? heard.Time + (DateTime.UtcNow - heard.At).TotalSeconds * _scroller.Rate : null;

    // Never past the next lyric line not yet heard: wait with it at the reading position (half a
    // beat in, so it's fully there) through a pause or an instrumental.
    private double? VoiceMaxTime()
    {
        if (_aligner is null || _aligner.Lines.Count == 0)
            return null;
        var next = _aligner.CurrentLine is { } current ? _aligner.Lines.ToList().IndexOf(current) + 1 : 0;
        return next < _aligner.Lines.Count && TimelineTime(_aligner.Lines[next].Section, _aligner.Lines[next].LineIndex, 0) is { } start
            ? start + 30 / _musical!.Tempo
            : null;
    }

    private void OnScrollRunningChanged()
    {
        if (!_scroller.IsRunning)
        {
            _audio?.Dispose();
            _audio = null;
            // Freeing the model waits for a transcription in progress, so do it off the UI thread.
            if (_voice is { } voice)
                _ = Task.Run(voice.Dispose);
            (_voice, _aligner, _voiceTarget, _lastHeard) = (null, null, null, null);
        }
        UpdateScrollControls();
        UpdateScrollStatus(force: true);
    }

    private ScrollTimeline? BuildTimeline()
    {
        if (_song is null || _renderer is null || _musical is null)
            return null;

        var builder = new ScrollTimelineBuilder(_song, _renderer.LineControls, SongHost, SongScroller.Viewport.Height,
            Math.Max(0, SongScroller.Extent.Height - SongScroller.Viewport.Height));
        ReadingMarker.Margin = new Thickness(12, builder.ReadingLineY - _fontSize * 0.2, 0, 0);
        ReadingMarker.Height = _fontSize * 2.4 * (_stageMode ? StageFontScale : 1);
        return _scrollMode switch
        {
            ScrollMode.Timed => builder.Timed(),
            ScrollMode.Musical or ScrollMode.FollowAudio or ScrollMode.FollowVoice => builder.Musical(_musical),
            _ => null,
        };
    }

    private double CurrentTempo => _tempo ?? _musical?.Tempo ?? MusicalTimeline.DefaultTempo;

    private void ApplyScrollRate()
    {
        var writtenTempo = _musical?.Tempo ?? MusicalTimeline.DefaultTempo;
        _scroller.Rate = _scrollMode == ScrollMode.Timed ? _speed : CurrentTempo / writtenTempo;
        _scroller.LiveRate = _scrollMode == ScrollMode.FollowAudio ? FollowRate : null;
        _scroller.TargetTime = _scrollMode == ScrollMode.FollowVoice ? VoiceTargetTime : null;
        _scroller.MaxTime = _scrollMode == ScrollMode.FollowVoice ? VoiceMaxTime : null;
        if (_audio is not null)
            _audio.ExpectedTempo = CurrentTempo;
    }

    // Following audio: play the musical timeline at the tempo heard, and hold still in silence.
    private double FollowRate()
    {
        if (_audio is null || !_audio.IsPlaying)
            return 0;
        var heard = _audio.Tempo ?? CurrentTempo;
        return Math.Clamp(heard / (_musical?.Tempo ?? MusicalTimeline.DefaultTempo), 0.5, 2);
    }

    private void OnScrollSlowerClick(object? sender, RoutedEventArgs e) => StepScrollSpeed(-1);

    private void OnScrollFasterClick(object? sender, RoutedEventArgs e) => StepScrollSpeed(+1);

    // Each tap after the first sets the tempo from the beat tapped so far.
    private void OnTapTempoClick(object? sender, RoutedEventArgs e)
    {
        if (_tapTempo.Tap(_tapClock.Elapsed) is not { } tapped)
        {
            StatusText.Text = "Keep tapping on the beat…";
            return;
        }
        var tempo = Math.Clamp(Math.Round(tapped), MinTempo, MaxTempo);
        _tempo = Math.Abs(tempo - (_musical?.Tempo ?? 0)) < 0.5 ? null : tempo;
        ApplyScrollRate();
        SaveSongSettings();
        UpdateScrollControls();
        StatusText.Text = $"Tempo {tempo:0} BPM from {_tapTempo.Count} taps";
    }

    private void StepScrollSpeed(int direction)
    {
        if (_scrollMode == ScrollMode.Timed)
            _speed = Math.Round(Math.Clamp(_speed + direction * ScrollSpeedStep, MinScrollSpeed, MaxScrollSpeed), 1);
        else
        {
            var tempo = Math.Clamp(Math.Round(CurrentTempo) + direction * TempoStep, MinTempo, MaxTempo);
            _tempo = Math.Abs(tempo - (_musical?.Tempo ?? 0)) < 0.5 ? null : tempo;
        }
        ApplyScrollRate();
        SaveSongSettings();
        UpdateScrollControls();
    }

    // Page turns land on line boundaries and keep a chorus together where it fits on screen.
    private double? PageTarget(int direction, double offset, double viewportHeight)
    {
        if (_renderer is null || _renderedColumns > 1)
            return null; // with two columns there's no single top-to-bottom order of lines
        var groups = PdfExporter.PageGroups(_renderer, SongHost);
        var target = direction > 0
            ? PageLayout.NextPageTop(groups, offset, viewportHeight) ?? double.MaxValue
            : PageLayout.PreviousPageTop(groups, offset, viewportHeight);
        // Leave a little room above the new top line rather than pressing it against the edge.
        return target is > 0 and < double.MaxValue ? target - _fontSize * 0.5 : target;
    }

    private void OnPageUpClick(object? sender, RoutedEventArgs e) => _scroller.Page(-1);

    private void OnPageDownClick(object? sender, RoutedEventArgs e) => _scroller.Page(+1);

    private void OnSongPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressPosition = e.GetCurrentPoint(SongScroller).Properties.IsLeftButtonPressed ? e.GetPosition(SongScroller) : null;

    private void OnSongPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrollMode != ScrollMode.Manual || _pressPosition is not { } pressed)
            return;
        _pressPosition = null;
        if (e.Source is StyledElement source && source.Classes.Contains("chord"))
            return; // tapping a chord shows its fingerings instead
        var released = e.GetPosition(SongScroller);
        var size = SongScroller.Bounds.Size;
        // A tap, not a drag, and not on the scroll bar.
        if (Point.Distance(pressed, released) > 6 || released.X > size.Width - 24)
            return;
        if (released.Y > size.Height * (1 - TapZone))
            _scroller.Page(+1);
        else if (released.Y < size.Height * TapZone)
            _scroller.Page(-1);
    }

    private void UpdateScrollControls()
    {
        _updatingScrollMode = true;
        ScrollModeBox.SelectedIndex = (int)_scrollMode;
        _updatingScrollMode = false;

        var manual = _scrollMode == ScrollMode.Manual;
        var timed = _scrollMode == ScrollMode.Timed;
        ScrollToggle.IsVisible = SlowerButton.IsVisible = SpeedText.IsVisible = FasterButton.IsVisible = !manual;
        TapTempoButton.IsVisible = !manual && !timed;
        ScrollToggle.IsChecked = _scroller.IsRunning;
        ScrollToggle.Content = _scroller.IsRunning ? "❚❚ Scroll"
            : _scrollMode == ScrollMode.FollowVoice && !SpeechModel.IsDownloaded ? (_downloadingModel ? "Downloading…" : "⬇ Get voice model")
            : _scrollMode is ScrollMode.FollowAudio or ScrollMode.FollowVoice ? "▶ Listen" : "▶ Scroll";
        ScrollToggle.IsEnabled = !_downloadingModel;
        ToolTip.SetTip(ScrollToggle, _scrollMode == ScrollMode.FollowVoice && !SpeechModel.IsDownloaded
            ? $"Following your voice needs a one-time download of the speech model ({SpeechModel.ApproximateBytes / 1e6:0} MB). After that it works offline."
            : "Start or stop (Space)");

        SpeedText.Text = timed ? $"{_speed:0.0}×" : $"{CurrentTempo:0} BPM";
        ToolTip.SetTip(SlowerButton, timed ? "Scroll slower" : "Lower tempo");
        ToolTip.SetTip(FasterButton, timed ? "Scroll faster" : "Raise tempo");
        ToolTip.SetTip(SpeedText, (timed, _song) switch
        {
            (true, { } song) when TimeMarkers.Find(song).Count > 0 => "Paced by the {x_time} markers in the song",
            (true, { Duration: { } duration }) => $"Paced to finish in {duration} at 1.0×",
            (true, _) => "Add {duration: m:ss} or {x_time: m:ss} markers to pace scrolling to the song",
            (false, _) when _musical is { TempoFromSong: false } => $"No {{tempo}} in the song; assuming {MusicalTimeline.DefaultTempo:0} BPM",
            _ => _tempo is null ? "The song's tempo" : $"Changed from the song's {_musical?.Tempo:0} BPM",
        });

        var following = _scroller.IsRunning && !manual;
        ReadingMarker.IsVisible = following && !timed;
        ReadingMarker.Background = SongPalette.FromTheme(CurrentTheme).Chord;
    }

    private void UpdateScrollStatus(bool force = false)
    {
        if (_song is null || _path is null)
            return;
        var now = DateTime.UtcNow;
        if (!force && now - _lastStatusUpdate < StatusInterval)
            return;
        _lastStatusUpdate = now;

        if (!_scroller.IsRunning)
        {
            StatusText.Text = Path.GetFileName(_path);
            return;
        }

        static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss");
        StatusText.Text = _scrollMode switch
        {
            ScrollMode.Timed => $"{Clock(_scroller.Time)} / {Clock(_scroller.Duration)}",
            ScrollMode.Musical => BarStatus(),
            ScrollMode.FollowAudio when _audio is { IsPlaying: false } => $"Waiting for you to play…  ·  {BarStatus()}",
            ScrollMode.FollowAudio => $"Hearing {(_audio?.Tempo is { } heard ? $"{heard:0} BPM" : "you")}  ·  {BarStatus()}",
            ScrollMode.FollowVoice when _lastHeard is null => $"Listening for you to sing…  ·  {BarStatus()}",
            ScrollMode.FollowVoice => $"Heard “{(_lastHeard.Length > 40 ? "…" + _lastHeard[^40..].TrimStart() : _lastHeard)}”  ·  {BarStatus()}",
            _ => Path.GetFileName(_path),
        };
    }

    private string BarStatus()
    {
        if (_musical is null)
            return "";
        var bar = _scroller.Time / _musical.SecondsPerBar;
        var section = _musical.LineAt(bar)?.Section;
        var name = section?.Label ?? (section?.Kind is { } kind and not SectionKind.None ? kind.ToString() : null);
        var position = $"Bar {Math.Min(Math.Floor(bar) + 1, Math.Ceiling(_musical.TotalBars))} of {Math.Ceiling(_musical.TotalBars)}";
        return name is null ? position : $"{position}  ·  {name}";
    }

    // ---- View options: chords, diagrams, instrument, stage mode ------------------------------

    private void ApplySavedSettings()
    {
        _updatingViewOptions = true;
        ShowChordsCheck.IsChecked = _settings.ShowChords;
        ShowDiagramsCheck.IsChecked = _settings.ShowDiagrams;
        SimplifyChordsCheck.IsChecked = _settings.SimplifyChords;
        KeyIncludesCapoCheck.IsChecked = _settings.KeyIncludesCapo;
        GuitarRadio.IsChecked = _settings.Instrument != Instrument.Ukulele.Name;
        UkuleleRadio.IsChecked = _settings.Instrument == Instrument.Ukulele.Name;
        ShowDiagramsCheck.IsEnabled = _settings.ShowChords;
        _updatingViewOptions = false;

        SetSidebarVisible(_settings.SidebarVisible);
        SetTheme(_settings.Theme);
        LibraryFolderText.Text = _settings.LibraryFolder ?? "No folder chosen";
        _updatingLibraryFilters = true;
        GroupByCombo.SelectedIndex = (int)_settings.Grouping;
        _updatingLibraryFilters = false;
        LibraryList.Classes.Set("byArtist", Grouping == LibraryGrouping.Artist);
        RefreshLibraryFilterChoices();
        if (_settings.LibraryFolder is not null)
            _ = ScanLibraryAsync();

        _setlistPath = _settings.SetlistPath;
        _setlist.Songs.AddRange(_settings.SetlistSongs);
        RefreshSetlist();
        UpdateSongControls();
    }

    private void OnViewOptionChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingViewOptions)
            return;
        _settings.ShowChords = ShowChordsCheck.IsChecked == true;
        _settings.ShowDiagrams = ShowDiagramsCheck.IsChecked == true;
        _settings.SimplifyChords = SimplifyChordsCheck.IsChecked == true;
        _settings.Instrument = UkuleleRadio.IsChecked == true ? Instrument.Ukulele.Name : Instrument.Guitar.Name;
        var keyConventionChanged = _settings.KeyIncludesCapo != (KeyIncludesCapoCheck.IsChecked == true);
        _settings.KeyIncludesCapo = KeyIncludesCapoCheck.IsChecked == true;
        if (keyConventionChanged)
            _settings.KeyConventionFolder = _settings.LibraryFolder; // a choice made by hand isn't guessed again
        ShowDiagramsCheck.IsEnabled = _settings.ShowChords;
        _settings.Save();
        RenderSong();
        if (keyConventionChanged)
        {
            // The key filter lists the keys you hear, which this changes.
            RefreshLibraryFilterChoices();
            ApplyLibraryFilter();
            UpdateNextSong();
        }
    }

    // ---- Themes ------------------------------------------------------------------------------

    private IEnumerable<SongTheme> AllThemes => SongTheme.BuiltIn.Concat(_settings.CustomThemes);

    private bool UsingSystemTheme =>
        _settings.Theme == SongTheme.SystemThemeName || AllThemes.All(t => t.Name != _settings.Theme);

    private SongTheme CurrentTheme => UsingSystemTheme
        ? (ActualThemeVariant == ThemeVariant.Dark ? SongTheme.Night : SongTheme.Day)
        : AllThemes.First(t => t.Name == _settings.Theme);

    private void SetTheme(string name)
    {
        _settings.Theme = name;
        _settings.Save();
        var theme = UsingSystemTheme ? null : CurrentTheme;
        WindowTheme.Apply(theme);
        Classes.Set("recolored", WindowTheme.RecolorsControls(theme));
        RefreshThemeColors();
    }

    private void RefreshThemeColors()
    {
        SongScroller.Background = SongPalette.FromTheme(CurrentTheme).Background;
        RenderSong();
        UpdateNextSong();
    }

    private void OnThemeFlyoutOpening(object? sender, EventArgs e)
    {
        ThemeMenu.Children.Clear();
        AddThemeChoice(SongTheme.SystemThemeName, "System (follow light/dark setting)");
        foreach (var theme in SongTheme.BuiltIn)
            AddThemeChoice(theme.Name, theme.Name);

        if (_settings.CustomThemes.Count > 0)
        {
            ThemeMenu.Children.Add(new TextBlock { Text = "Your themes", Opacity = 0.6, Margin = new Thickness(0, 8, 0, 0) });
            foreach (var theme in _settings.CustomThemes)
                AddThemeChoice(theme.Name, theme.Name);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        var newButton = new Button { Content = "New theme…" };
        ToolTip.SetTip(newButton, "Start a new theme from the current one");
        newButton.Click += async (_, _) =>
        {
            ThemeButton.Flyout?.Hide();
            await EditThemeAsync(CurrentTheme.Clone(UniqueThemeName($"{CurrentTheme.Name} (custom)")), isNew: true);
        };
        var editButton = new Button { Content = "Edit…", IsEnabled = !UsingSystemTheme && !CurrentTheme.IsBuiltIn };
        ToolTip.SetTip(editButton, "Edit the current theme (built-in themes can be copied with New theme…)");
        editButton.Click += async (_, _) =>
        {
            ThemeButton.Flyout?.Hide();
            await EditThemeAsync(CurrentTheme, isNew: false);
        };
        buttons.Children.Add(newButton);
        buttons.Children.Add(editButton);
        ThemeMenu.Children.Add(buttons);

        void AddThemeChoice(string name, string label)
        {
            var radio = new RadioButton
            {
                Content = label,
                GroupName = "Theme",
                IsChecked = name == SongTheme.SystemThemeName ? UsingSystemTheme : !UsingSystemTheme && name == _settings.Theme,
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && name != _settings.Theme)
                    SetTheme(name);
            };
            ThemeMenu.Children.Add(radio);
        }
    }

    private string UniqueThemeName(string baseName)
    {
        var taken = AllThemes.Select(t => t.Name).ToHashSet();
        var name = baseName;
        for (var i = 2; taken.Contains(name); i++)
            name = $"{baseName} {i}";
        return name;
    }

    private async Task EditThemeAsync(SongTheme theme, bool isNew)
    {
        var editor = new ThemeEditorWindow(theme, isNew, AllThemes.Select(t => t.Name).ToHashSet());
        await editor.ShowDialog(this);

        var index = _settings.CustomThemes.FindIndex(t => t.Name == theme.Name);
        if (editor.Deleted && index >= 0)
        {
            _settings.CustomThemes.RemoveAt(index);
            SetTheme(SongTheme.SystemThemeName);
        }
        else if (editor.SavedTheme is { } saved)
        {
            if (isNew || index < 0)
                _settings.CustomThemes.Add(saved);
            else
                _settings.CustomThemes[index] = saved;
            SetTheme(saved.Name);
        }
    }

    private void OnStageToggleClick(object? sender, RoutedEventArgs e) => SetStageMode(!_stageMode);

    // Opening a song isn't playing it; scrolling it or putting it on stage is.
    private void CountPlay()
    {
        if (_countedPlay || _path is null)
            return;
        _countedPlay = true;
        _settings.ForSong(_path).Plays++;
        _settings.Save();
    }

    private void SetStageMode(bool on)
    {
        if (on == _stageMode)
            return;
        _stageMode = on;
        StageToggle.IsChecked = on;
        if (on)
            CountPlay();
        EditorPane.IsVisible = _editing && !on;
        if (on)
        {
            _windowStateBeforeStage = WindowState;
            WindowState = WindowState.FullScreen;
            Sidebar.IsVisible = false;
        }
        else
        {
            WindowState = _windowStateBeforeStage;
            Sidebar.IsVisible = _settings.SidebarVisible;
        }
        RenderSong();
        UpdateNextSong();
    }

    private void OnSidebarToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_stageMode)
            SetStageMode(false);
        SetSidebarVisible(SidebarToggle.IsChecked == true);
        _settings.Save();
    }

    private void SetSidebarVisible(bool visible)
    {
        _settings.SidebarVisible = visible;
        Sidebar.IsVisible = visible;
        SidebarToggle.IsChecked = visible;
    }

    // ---- Keyboard and drag & drop ------------------------------------------------------------

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _stageMode)
        {
            SetStageMode(false);
            e.Handled = true;
            return;
        }
        // Cmd on macOS, Ctrl elsewhere.
        var command = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        // Save and Edit work while typing in the editor.
        if (e.KeyModifiers.HasFlag(command) && e.Key is Key.S or Key.E)
        {
            if (e.Key == Key.E)
                OnEditToggleClick(this, e);
            else if (_editing)
                _ = SaveEditsAsync();
            e.Handled = true;
            return;
        }
        if (e.Source is TextBox)
            return; // let the search box and the editor have their keys
        if (e.KeyModifiers.HasFlag(command))
        {
            switch (e.Key)
            {
                case Key.O: OnOpenClick(this, e); break;
                case Key.P: OnExportPdfClick(this, e); break;
                case Key.T: OnTunerClick(this, e); break;
                case Key.OemPlus or Key.Add: SetFontSize(_fontSize + 2); break;
                case Key.OemMinus or Key.Subtract: SetFontSize(_fontSize - 2); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        // Page turns: Page Up/Down (most foot pedals) in any mode; Space in manual mode.
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Shift && _scrollMode == ScrollMode.Manual)
        {
            _scroller.Page(-1);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Space when _scrollMode == ScrollMode.Manual: _scroller.Page(+1); break;
            case Key.Space: ToggleScrolling(); break;
            case Key.PageDown: _scroller.Page(+1); break;
            case Key.PageUp: _scroller.Page(-1); break;
            case Key.Right: StepSetlist(+1); break;
            case Key.Left: StepSetlist(-1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path)
            OpenPath(path);
    }

    /// <summary>Opens a song, a setlist, or a folder as the library (from Finder, drag and drop, …).</summary>
    public void OpenPath(string path)
    {
        Activate();
        Open(path);
    }

    private void Open(string path)
    {
        if (Path.GetExtension(path).Equals(Setlist.FileExtension, StringComparison.OrdinalIgnoreCase) || Setlist.IsLivePrompterSetlist(path))
            OpenSetlist(path);
        else if (Directory.Exists(path))
            SetLibraryFolder(path);
        else
            LoadFile(path);
    }

    // ---- Open / export -----------------------------------------------------------------------

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ChordPro song",
            AllowMultiple = false,
            FileTypeFilter = [ChordProFiles, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            Open(path); // a LivePrompter setlist is a .txt file too
    }

    private async void OnExportPdfClick(object? sender, RoutedEventArgs e)
    {
        if (_song is null || _path is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save song as PDF",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_path) + ".pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } pdfPath)
            return;

        try
        {
            await PdfExporter.ExportAsync(_song, CurrentRenderOptions(), pdfPath);
            StatusText.Text = $"Saved {Path.GetFileName(pdfPath)}";
            // Open it in the default viewer, where it can be printed.
            Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save PDF: {ex.Message}";
        }
    }

    private async void OnSetlistPdfClick(object? sender, RoutedEventArgs e)
    {
        var name = _setlistPath is null ? "Setlist" : Path.GetFileNameWithoutExtension(_setlistPath);
        var (entries, missing) = BookletEntries();
        if (entries.All(entry => entry.IsDivider))
        {
            StatusText.Text = "The setlist has no songs to print";
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save setlist as PDF",
            SuggestedFileName = name + ".pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } pdfPath)
            return;
        try
        {
            await PdfExporter.ExportSetlistAsync(name, entries, pdfPath);
            StatusText.Text = $"Saved {Path.GetFileName(pdfPath)}" + (missing > 0 ? $" ({missing} missing {(missing == 1 ? "song" : "songs")} left out)" : "");
            Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save PDF: {ex.Message}";
        }
    }

    // The setlist as the player plays it: each song with their transpose and capo, keys as heard.
    private (List<BookletEntry> Entries, int Missing) BookletEntries()
    {
        var entries = new List<BookletEntry>();
        var missing = 0;
        foreach (var path in _setlist.Songs)
        {
            if (Setlist.IsDivider(path))
            {
                entries.Add(new BookletEntry(Setlist.DividerName(path), null, null, 0, null, null, null));
                continue;
            }
            Song song;
            try
            {
                song = ChordProParser.ParseFile(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                missing++;
                continue;
            }
            var info = SongLibrary.ReadInfo(path) ?? new SongInfo(path, song.Title ?? Path.GetFileNameWithoutExtension(path), song.Artist);
            var played = PlannedFor(info);
            var settings = _settings.Songs.GetValueOrDefault(path);
            var options = CurrentRenderOptions() with { Transpose = settings?.Transpose ?? 0, Capo = settings?.Capo };
            entries.Add(new BookletEntry(info.Title, info.Artist, played.Key, played.Capo, played.Tempo, song, options));
        }
        return (entries, missing);
    }

    // ---- Library and recent files ------------------------------------------------------------

    private async void OnChooseFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your songs folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            SetLibraryFolder(path);
    }

    private void SetLibraryFolder(string path)
    {
        _settings.LibraryFolder = path;
        _settings.Save();
        LibraryFolderText.Text = path;
        _ = ScanLibraryAsync();
    }

    private void OnRefreshLibraryClick(object? sender, RoutedEventArgs e) => _ = ScanLibraryAsync();

    /// <param name="quiet">An automatic rescan: don't show "scanning…", and leave the list alone if nothing changed.</param>
    private async Task ScanLibraryAsync(bool quiet = false)
    {
        if (_settings.LibraryFolder is not { } folder || !Directory.Exists(folder))
        {
            WatchLibrary(null);
            _library = [];
            _librarySetlists = [];
            RefreshLibraryFilterChoices();
            RefreshLibrarySetlists();
            ApplyLibraryFilter();
            return;
        }
        WatchLibrary(folder);
        if (!quiet)
            LibraryFolderText.Text = $"{folder} (scanning…)";
        var (library, setlists) = await Task.Run(() => (SongLibrary.Scan(folder), SongLibrary.FindSetlists(folder)));
        if (folder != _settings.LibraryFolder)
            return; // another folder was chosen while this one was scanning

        // File events also arrive for changes that don't matter here (or none at all, on macOS).
        if (quiet && library.SequenceEqual(_library, SongInfoComparer.Instance) && setlists.SequenceEqual(_librarySetlists))
            return;
        _library = library;
        _librarySetlists = setlists;
        await GuessKeyConventionAsync(folder);
        LibraryFolderText.Text = $"{folder} · {_library.Count} songs";
        ToolTip.SetTip(LibraryFolderText, folder);
        RefreshLibraryFilterChoices();
        RefreshLibrarySetlists();
        ApplyLibraryFilter();
        if (_path is not null)
            RevealInLibrary(_path); // the song opened at startup may have loaded before the scan finished
    }

    // The first time a library is scanned, work out from its capo songs whether its keys include
    // the capo (LivePrompter) or not (ChordPro), so keys show right without visiting the View menu.
    private async Task GuessKeyConventionAsync(string folder)
    {
        if (_settings.KeyConventionFolder == folder)
            return;
        var songs = _library;
        var guess = await Task.Run(() => SongLibrary.GuessKeyIncludesCapo(songs));
        if (guess is not { } includesCapo)
            return; // nothing to go on yet; try again on the next scan
        _settings.KeyConventionFolder = folder;
        _settings.Save();
        if (includesCapo == _settings.KeyIncludesCapo)
            return;
        _settings.KeyIncludesCapo = includesCapo;
        _settings.Save();
        _updatingViewOptions = true;
        KeyIncludesCapoCheck.IsChecked = includesCapo;
        _updatingViewOptions = false;
        RenderSong();
        UpdateNextSong();
        StatusText.Text = includesCapo
            ? "This library's keys include the capo, as LivePrompter writes them; set in View"
            : "This library's keys are the chords' keys, as ChordPro writes them; set in View";
    }

    // Rescan when songs are added, removed, renamed or edited, e.g. by a converter writing into the folder.
    private void WatchLibrary(string? folder)
    {
        if (folder is not null && _libraryWatcher?.Path == folder)
            return;
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        if (folder is null)
            return;
        try
        {
            _libraryWatcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _libraryRescanTimer.Stop();
                _libraryRescanTimer.Start();
            });
            _libraryWatcher.Changed += changed;
            _libraryWatcher.Created += changed;
            _libraryWatcher.Deleted += changed;
            _libraryWatcher.Renamed += (s, e) => changed(s, e);
            _libraryWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            _libraryWatcher = null; // Refresh still works
        }
    }

    private async void OnCheckLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (_library.Count == 0 && _librarySetlists.Count == 0)
        {
            StatusText.Text = "Choose a library folder to check";
            return;
        }
        var (songs, setlists) = (_library, _librarySetlists);
        StatusText.Text = "Checking the library…";
        // A library whose keys include the capo is a LivePrompter library: check its line widths too.
        var livePrompter = _settings.KeyIncludesCapo;
        var issues = await Task.Run(() => LibraryCheck.Run(songs, setlists, livePrompter));
        StatusText.Text = _path is null ? "" : Path.GetFileName(_path);

        // "Looks fine" lasts until the file changes: forget acceptances for files changed since.
        static long Ticks(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        _settings.AcceptedIssues.RemoveAll(a => a.FileTicks != Ticks(a.Path));
        _settings.Save();
        bool IsAccepted(LibraryIssue issue) => _settings.AcceptedIssues.Any(a => a.Path == issue.Path && a.Problem == issue.Problem);
        void Accept(LibraryIssue issue)
        {
            _settings.AcceptedIssues.Add(new AcceptedIssue { Path = issue.Path, Problem = issue.Problem, FileTicks = Ticks(issue.Path) });
            _settings.Save();
        }

        _checkWindow?.Close();
        _checkWindow = new LibraryCheckWindow(issues, SongPalette.FromTheme(CurrentTheme), OpenPath, IsAccepted, Accept);
        _checkWindow.Closed += (_, _) => _checkWindow = null;
        _checkWindow.Show(this);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => OnLibraryFilterChanged();

    private void OnLibraryFilterChanged(object? sender, SelectionChangedEventArgs e) => OnLibraryFilterChanged();

    private void OnLibraryFilterChanged()
    {
        if (_updatingLibraryFilters)
            return;
        _collapsedFilteredGroups.Clear();
        ApplyLibraryFilter();
    }

    private void OnGroupByChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingLibraryFilters)
            return;
        _settings.LibraryGroupBy = Grouping.ToString();
        _settings.Save();
        // Group names mean something else under the new grouping.
        _expandedGroups.Clear();
        _collapsedFilteredGroups.Clear();
        LibraryList.Classes.Set("byArtist", Grouping == LibraryGrouping.Artist);
        ApplyLibraryFilter();
        if (_path is not null)
            RevealInLibrary(_path);
    }

    private LibraryGrouping Grouping => (LibraryGrouping)Math.Max(0, GroupByCombo.SelectedIndex);

    // The key and capo choices; index 0 of each combo box is "any".
    private List<KeyChoice> _filterKeys = [];
    private List<int> _filterCapos = [];

    private string? KeyFilter => KeyFilterCombo.SelectedIndex > 0 ? _filterKeys[KeyFilterCombo.SelectedIndex - 1].Key : null;

    private int? CapoFilter => CapoFilterCombo.SelectedIndex > 0 ? _filterCapos[CapoFilterCombo.SelectedIndex - 1] : null;

    // Fill the key and capo choices from the library, keeping the current choice if it's still there.
    private void RefreshLibraryFilterChoices()
    {
        var (key, capo) = (KeyFilter, CapoFilter);
        _filterKeys = SongLibrary.Keys(_library, _settings.KeyIncludesCapo);
        _filterCapos = SongLibrary.Capos(_library);

        _updatingLibraryFilters = true;
        KeyFilterCombo.ItemsSource = new[] { "Any key" }.Concat(_filterKeys.Select(k => $"Key: {k.Label}")).ToList();
        KeyFilterCombo.SelectedIndex = key is null ? 0 : _filterKeys.FindIndex(k => SongLibrary.SameKey(k.Key, key)) + 1;
        CapoFilterCombo.ItemsSource = new[] { "Any capo" }.Concat(_filterCapos.Select(c => c == 0 ? "No capo" : $"Capo {c}")).ToList();
        CapoFilterCombo.SelectedIndex = capo is null ? 0 : _filterCapos.IndexOf(capo.Value) + 1;
        _updatingLibraryFilters = false;
    }

    // Searching or filtering by key or capo narrows the list, so every group starts expanded.
    private bool IsLibraryNarrowed => !string.IsNullOrWhiteSpace(SearchBox.Text) || KeyFilter is not null || CapoFilter is not null;

    /// <param name="scrollToSong">Scroll the library to the open song (not while a group is being opened).</param>
    private void ApplyLibraryFilter(bool scrollToSong = true)
    {
        var songs = SongLibrary.Filter(_library, SearchBox.Text, KeyFilter, CapoFilter, _settings.KeyIncludesCapo);
        _updatingLists = true;
        LibraryList.ItemsSource = Grouping == LibraryGrouping.None ? songs : GroupedLibraryItems(songs);
        _updatingLists = false;
        SyncListSelections(scrollToSong);
    }

    // Groups start collapsed so a large library reads as a list of artists or books.
    private List<object> GroupedLibraryItems(List<SongInfo> songs)
    {
        var items = new List<object>();
        var groups = SongLibrary.Group(songs, Grouping);
        // Recently added is for finding what just came in: open the newest group.
        if (Grouping == LibraryGrouping.Recent && groups.Count > 0 && !IsLibraryNarrowed && _expandedGroups.Count == 0)
            _expandedGroups.Add(groups[0].Name);
        foreach (var group in groups)
        {
            var expanded = IsGroupExpanded(group.Name);
            items.Add(new GroupHeader(group.Name, group.Songs.Count, expanded));
            if (expanded)
                items.AddRange(group.Songs);
        }
        return items;
    }

    private bool IsGroupExpanded(string name) =>
        IsLibraryNarrowed ? !_collapsedFilteredGroups.Contains(name) : _expandedGroups.Contains(name);

    private void ToggleGroup(string name)
    {
        var set = IsLibraryNarrowed ? _collapsedFilteredGroups : _expandedGroups;
        if (!set.Remove(name))
            set.Add(name);
        ApplyLibraryFilter(scrollToSong: false);
        ScrollHeadingToTop(name);
    }

    // Put a group's heading at the top of the list so its songs show beneath it, instead of
    // opening below the visible part of a long list.
    private void ScrollHeadingToTop(string name)
    {
        if (LibraryList.ItemsSource is not IEnumerable<object> items)
            return;
        var index = items.ToList().FindIndex(item => item is GroupHeader header && header.Name == name);
        if (index < 0)
            return;
        LibraryList.ScrollIntoView(index); // the list only builds rows near the view
        // Measure where the row landed once the list has laid it out.
        Dispatcher.UIThread.Post(() =>
        {
            if (LibraryList.ContainerFromIndex(index) is not { } row ||
                LibraryList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller ||
                row.TranslatePoint(default, scroller) is not { } position)
                return;
            scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, scroller.Offset.Y + position.Y));
        }, DispatcherPriority.Background);
    }

    // A song opened from Recent, a setlist or Finder expands its group so it shows highlighted
    // (the first of its books, when grouped by book).
    private void RevealInLibrary(string path)
    {
        if (Grouping == LibraryGrouping.None || IsLibraryNarrowed || _library.Find(s => s.Path == path) is not { } info)
            return;
        var names = SongLibrary.GroupNames(info, Grouping).ToList();
        if (!names.Any(_expandedGroups.Contains) && _expandedGroups.Add(names[0]))
            ApplyLibraryFilter();
    }

    private void RefreshRecentList()
    {
        _updatingLists = true;
        RecentList.ItemsSource = _settings.RecentFiles.Where(File.Exists).Select(SongLibrary.ReadInfo).OfType<SongInfo>().ToList();
        _updatingLists = false;
    }

    private void OnSongListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingLists && sender is ListBox { SelectedItem: GroupHeader header })
        {
            // Rebuilding the list inside its own selection event confuses the ListBox, so defer it.
            Dispatcher.UIThread.Post(() => ToggleGroup(header.Name));
            return;
        }
        if (_updatingLists || sender is not ListBox { SelectedItem: SongInfo info } || info.Path == _path || Setlist.IsDivider(info.Path))
            return;
        LoadFile(info.Path);
    }

    // Highlight the open song in whichever lists contain it.
    private void SyncListSelections(bool scrollLibrary = true)
    {
        _updatingLists = true;
        foreach (var list in new[] { LibraryList, RecentList, SetlistList })
        {
            var items = list.ItemsSource as IEnumerable<object>;
            var index = items?.ToList().FindIndex(item => item is SongInfo s && s.Path == _path) ?? -1;
            list.SelectedIndex = index;
            if (index >= 0 && (scrollLibrary || list != LibraryList))
                list.ScrollIntoView(index);
        }
        _updatingLists = false;
    }

    // ---- Editing ------------------------------------------------------------------------------

    private bool IsDirty => _editing && (EditorBox.Text ?? "") != _savedText;

    private void OnEditToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_editing)
            _ = StopEditingAsync();
        else
            StartEditing();
    }

    private void StartEditing()
    {
        if (_path is null || _song is null)
        {
            EditToggle.IsChecked = false;
            StatusText.Text = "Open a song to edit it";
            return;
        }
        if (_stageMode)
            SetStageMode(false);
        _scroller.Stop();
        _editing = true;
        EditToggle.IsChecked = true;
        EditorPane.IsVisible = true;
        LoadEditor();
        EditorBox.Focus();
    }

    private async Task StopEditingAsync()
    {
        if (!await ConfirmKeepOrDiscardEditsAsync())
        {
            EditToggle.IsChecked = true;
            return;
        }
        _editing = false;
        _previewTimer.Stop();
        EditToggle.IsChecked = false;
        EditorPane.IsVisible = false;
        // Show the song as saved (changes not kept are dropped from the preview too).
        if (_loadedText is not null)
            ShowSongText(_loadedText);
        UpdateEditorState();
    }

    // Puts the file's current text in the editor.
    private void LoadEditor()
    {
        if (_path is null)
            return;
        try
        {
            (_savedText, _fileFormat) = SongFile.Read(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not read {Path.GetFileName(_path)}: {ex.Message}";
            return;
        }
        _changedOnDisk = false;
        SetEditorText(_savedText, 0);
        ShowSongText(_savedText);
        UpdateEditorState();
    }

    // Replaces the editor's text with the cursor at caret. The selection is cleared first: a
    // selection left over from longer text points past the end of the new text, which macOS's
    // text input (asking about the selection between key presses) can trip over.
    private void SetEditorText(string text, int caret, bool quiet = true)
    {
        _settingEditorText = quiet;
        EditorBox.ClearSelection();
        EditorBox.CaretIndex = 0;
        EditorBox.Text = text;
        EditorBox.CaretIndex = Math.Clamp(caret, 0, text.Length);
        _settingEditorText = false;
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_settingEditorText)
            return;
        _previewTimer.Stop();
        _previewTimer.Start();
        UpdateEditorState();
    }

    private void PreviewEdits()
    {
        _previewTimer.Stop();
        if (_editing)
            ShowSongText(EditorBox.Text ?? "");
    }

    // Renders text as the song, keeping the scroll position, and refreshes the chord buttons.
    private void ShowSongText(string text)
    {
        _song = ChordProParser.Parse(text);
        _musical = MusicalTimeline.Build(_song);
        ApplyScrollRate();
        var offset = SongScroller.Offset;
        RenderSong();
        SongScroller.UpdateLayout();
        SongScroller.Offset = offset;
        RefreshChordButtons();
        UpdateEditorState();
    }

    private void UpdateEditorState()
    {
        var dirty = IsDirty;
        var name = _song?.Title ?? (_path is null ? null : Path.GetFileNameWithoutExtension(_path));
        Title = (dirty ? "• " : "") + (name is null ? "ChordPro Viewer" : $"{name} — ChordPro Viewer");
        EditorTitle.Text = _path is null ? "" : Path.GetFileName(_path) + (dirty ? "  (edited)" : "");
        SaveButton.IsEnabled = dirty || _changedOnDisk;
        RevertButton.IsEnabled = dirty || _changedOnDisk;
    }

    private void OnSaveEditsClick(object? sender, RoutedEventArgs e) => _ = SaveEditsAsync();

    /// <summary>Writes the editor's text to the file; false if it wasn't saved.</summary>
    private async Task<bool> SaveEditsAsync()
    {
        if (!_editing || _path is null)
            return false;
        if (_changedOnDisk)
        {
            var answer = await ChoiceDialog.AskAsync(this, $"{Path.GetFileName(_path)} changed on disk while you were editing it.",
                "Saving replaces those changes with yours. Revert loads the version on disk instead.", "Save Anyway", "Cancel");
            if (answer != "Save Anyway")
                return false;
        }
        var text = EditorBox.Text ?? "";
        try
        {
            SongFile.Write(_path, text, _fileFormat);
            _loadedText = File.ReadAllText(_path); // so the file watcher doesn't treat our save as someone else's
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save {Path.GetFileName(_path)}: {ex.Message}";
            return false;
        }
        _savedText = text;
        _changedOnDisk = false;
        PreviewEdits();
        UpdateEditorState();
        RefreshRecentList();
        StatusText.Text = $"Saved {Path.GetFileName(_path)} at {DateTime.Now:t}";
        return true;
    }

    private void OnRevertEditsClick(object? sender, RoutedEventArgs e)
    {
        LoadEditor();
        StatusText.Text = $"Reverted to the saved {Path.GetFileName(_path)}";
    }

    // Before unsaved edits would be lost: Save, Don't Save, or Cancel (false: stay as you are).
    private async Task<bool> ConfirmKeepOrDiscardEditsAsync()
    {
        if (!IsDirty)
            return true;
        var name = _song?.Title ?? Path.GetFileNameWithoutExtension(_path);
        var answer = await ChoiceDialog.AskAsync(this, $"Save changes to “{name}”?",
            "Your changes will be lost if you don't save them.", "Save", "Don't Save", "Cancel");
        switch (answer)
        {
            case "Save":
                return await SaveEditsAsync();
            case "Don't Save":
                SetEditorText(_savedText, 0);
                UpdateEditorState();
                return true;
            default:
                return false;
        }
    }

    // One button per chord already in the song, to insert it at the cursor.
    private void RefreshChordButtons()
    {
        var chords = _song?.ChordUsages().Distinct().Take(30).ToList() ?? [];
        if (chords.SequenceEqual(_chordButtonChords))
            return;
        _chordButtonChords = chords;
        ChordButtons.Children.Clear();
        foreach (var chord in chords)
        {
            var button = new Button { Content = chord, Padding = new Thickness(8, 2), Focusable = false };
            button.Click += (_, _) => InsertChord(chord);
            ChordButtons.Children.Add(button);
        }
    }

    private void InsertChord(string chord)
    {
        var text = EditorBox.Text ?? "";
        var at = Math.Clamp(EditorBox.CaretIndex, 0, text.Length);
        var insert = $"[{chord}]";
        SetEditorText(text.Insert(at, insert), at + insert.Length, quiet: false);
        EditorBox.Focus();
    }

    private void OnInsertChordClick(object? sender, RoutedEventArgs e)
    {
        var chord = (ChordEntry.Text ?? "").Trim().Trim('[', ']').Trim();
        if (chord.Length == 0)
            return;
        InsertChord(chord);
        ChordEntry.Text = "";
    }

    private void OnChordEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnInsertChordClick(sender, e);
            e.Handled = true;
        }
    }

    // ---- Setlists ----------------------------------------------------------------------------

    private void RefreshSetlist()
    {
        SetlistNameText.Text = _setlistPath is null
            ? $"Unsaved setlist · {_setlist.Songs.Count} songs"
            : $"{Path.GetFileNameWithoutExtension(_setlistPath)} · {_setlist.Songs.Count} songs";

        _updatingLists = true;
        SetlistList.ItemsSource = _setlist.Songs
            .Select(p => Setlist.IsDivider(p) ? new SongInfo(p, $"— {Setlist.DividerName(p)} —", null)
                : SongLibrary.ReadInfo(p) ?? new SongInfo(p, $"{Path.GetFileName(p)} (missing)", null))
            .ToList();
        _updatingLists = false;
        SyncListSelections();
        RefreshLibrarySetlists();
        UpdateNextSong();
    }

    // The setlists found in the library folder, with the open one selected.
    private void RefreshLibrarySetlists()
    {
        _updatingLists = true;
        LibrarySetlistsCombo.ItemsSource = _librarySetlists.Select(Path.GetFileNameWithoutExtension).ToList();
        LibrarySetlistsCombo.SelectedIndex = _setlistPath is null ? -1 : _librarySetlists.IndexOf(_setlistPath);
        LibrarySetlistsCombo.IsVisible = _librarySetlists.Count > 0;
        _updatingLists = false;
    }

    private void OnLibrarySetlistChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingLists && LibrarySetlistsCombo.SelectedIndex is var index and >= 0 && _librarySetlists[index] != _setlistPath)
            OpenSetlist(_librarySetlists[index]);
    }

    // The song after the open one in the setlist, skipping set dividers but saying when one is crossed.
    private (string Path, string? Divider)? NextInSetlist()
    {
        var current = _path is null ? -1 : _setlist.Songs.IndexOf(_path);
        if (current < 0)
            return null;
        string? divider = null;
        for (var i = current + 1; i < _setlist.Songs.Count; i++)
        {
            if (Setlist.IsDivider(_setlist.Songs[i]))
                divider = Setlist.DividerName(_setlist.Songs[i]);
            else
                return (_setlist.Songs[i], divider);
        }
        return null;
    }

    // In stage mode, show what's coming next so the key and the next chart are no surprise.
    private void UpdateNextSong()
    {
        if (!_stageMode || NextInSetlist() is not var (next, divider))
        {
            NextSongBadge.IsVisible = false;
            return;
        }
        var text = "Next: ";
        if (divider is not null)
            text += $"{divider} · ";
        if (SongLibrary.ReadInfo(next) is { } info)
        {
            text += info.Title;
            // The key as the song's header will show it, with this song's saved transposition.
            var sounding = ChordTransposer.SoundingKey(info.Key, info.Capo, _settings.KeyIncludesCapo);
            if (ChordTransposer.Shift(sounding, _settings.ForSong(next).Transpose) is { } key)
                text += $" ({key})";
        }
        else
        {
            text += $"{Path.GetFileNameWithoutExtension(next)} (missing)";
        }

        var palette = SongPalette.FromTheme(CurrentTheme);
        NextSongText.Text = text;
        NextSongText.Foreground = palette.Text;
        NextSongBadge.Background = palette.CommentBackground;
        NextSongBadge.BorderBrush = palette.ChorusBar;
        NextSongBadge.IsVisible = true;
    }

    // Remember the setlist between sessions, and keep its file up to date once it has one.
    private void SetlistChanged(bool saveFile = true)
    {
        _settings.SetlistPath = _setlistPath;
        _settings.SetlistSongs = [.. _setlist.Songs];
        _settings.Save();
        if (saveFile && _setlistPath is not null)
        {
            try { _setlist.Save(_setlistPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = $"Could not save setlist: {ex.Message}"; }
        }
        RefreshSetlist();
    }

    private void OnNewSetlistClick(object? sender, RoutedEventArgs e)
    {
        _setlist.Songs.Clear();
        _setlistPath = null;
        SetlistChanged();
    }

    private async void OnOpenSetlistClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open setlist",
            AllowMultiple = false,
            FileTypeFilter = [SetlistFiles, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            OpenSetlist(path);
    }

    private void OpenSetlist(string path)
    {
        try
        {
            var loaded = Setlist.Load(path, _library.Select(s => s.Path));
            _setlist.Songs.Clear();
            _setlist.Songs.AddRange(loaded.Songs);
            _setlistPath = path;
            SetlistChanged(saveFile: false);
            if (_setlist.Songs.FirstOrDefault(song => !Setlist.IsDivider(song) && File.Exists(song)) is { } first)
                LoadFile(first);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not open setlist: {ex.Message}";
        }
    }

    private async void OnSaveSetlistClick(object? sender, RoutedEventArgs e) => await SaveSetlistAsAsync();

    private async Task<bool> SaveSetlistAsAsync(string? suggestedName = null)
    {
        // A LivePrompter library keeps its setlists as .txt in its Setlists folder; start there.
        var livePrompter = _settings.KeyIncludesCapo;
        var extension = livePrompter ? SongLibrary.TextExtension : Setlist.FileExtension;
        var setlistsFolder = _settings.LibraryFolder is { } library && Directory.Exists(Path.Combine(library, "Setlists"))
            ? await StorageProvider.TryGetFolderFromPathAsync(Path.Combine(library, "Setlists"))
            : null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save setlist",
            SuggestedFileName = suggestedName is not null ? suggestedName + extension
                : _setlistPath is null ? "Setlist" + extension : Path.GetFileName(_setlistPath),
            SuggestedStartLocation = setlistsFolder,
            DefaultExtension = extension.TrimStart('.'),
            // Saving as .txt writes song names the way LivePrompter does.
            FileTypeChoices = livePrompter ? [LivePrompterSetlistFiles, ViewerSetlistFiles] : [ViewerSetlistFiles, LivePrompterSetlistFiles],
        });
        if (file?.TryGetLocalPath() is not { } path)
            return false;
        _setlistPath = path;
        SetlistChanged();
        return true;
    }

    private void OnSuggestSetlistClick(object? sender, RoutedEventArgs e)
    {
        if (_library.Count == 0)
        {
            StatusText.Text = "Choose a library folder first: sets are built from its songs";
            return;
        }
        var byPath = _library.ToDictionary(s => s.Path);
        var setlist = _setlist.Songs.Where(p => !Setlist.IsDivider(p)).Select(p => byPath.GetValueOrDefault(p) ?? SongLibrary.ReadInfo(p)).OfType<SongInfo>().ToList();
        new SetlistSuggestWindow(_library, setlist, PlannedFor, LearnedVoice(), SongPalette.FromTheme(CurrentTheme),
            use: paths => ReplaceSetlist(paths),
            saveAs: async paths =>
            {
                ReplaceSetlist(paths);
                await SaveSetlistAsAsync($"Suggested set {DateTime.Now:yyyy-MM-dd}");
            }).Show(this);
    }

    // How the player plays a song: the key heard after their transpose, their tempo and capo, and plays.
    private PlannedSong PlannedFor(SongInfo song)
    {
        var settings = _settings.Songs.GetValueOrDefault(song.Path);
        var capo = settings?.Capo ?? song.Capo;
        var heard = ChordTransposer.Shift(ChordTransposer.SoundingKey(song.Key, song.Capo, _settings.KeyIncludesCapo), settings?.Transpose ?? 0);
        return new PlannedSong(song, heard, settings?.Tempo ?? song.Tempo, capo, settings?.Plays ?? 0);
    }

    // A suggestion becomes the setlist (unsaved until Save as).
    private void ReplaceSetlist(IReadOnlyList<string> paths)
    {
        _setlist.Songs.Clear();
        _setlist.Songs.AddRange(paths);
        _setlistPath = null;
        SetlistChanged();
        StatusText.Text = $"Setlist replaced with the suggested set of {paths.Count} songs";
    }

    private void OnAddToSetlistClick(object? sender, RoutedEventArgs e)
    {
        if (_path is null)
            return;
        _setlist.Songs.Add(_path);
        SetlistChanged();
    }

    private void OnRemoveFromSetlistClick(object? sender, RoutedEventArgs e)
    {
        if (SetlistList.SelectedIndex is var index and >= 0)
        {
            _setlist.Songs.RemoveAt(index);
            SetlistChanged();
        }
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e) => MoveSelectedSong(-1);

    private void OnMoveDownClick(object? sender, RoutedEventArgs e) => MoveSelectedSong(+1);

    private void MoveSelectedSong(int direction)
    {
        var index = SetlistList.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _setlist.Songs.Count)
            return;
        (_setlist.Songs[index], _setlist.Songs[target]) = (_setlist.Songs[target], _setlist.Songs[index]);
        SetlistChanged();
        _updatingLists = true;
        SetlistList.SelectedIndex = target;
        _updatingLists = false;
    }

    private void OnPreviousSongClick(object? sender, RoutedEventArgs e) => StepSetlist(-1);

    private void OnNextSongClick(object? sender, RoutedEventArgs e) => StepSetlist(+1);

    private void StepSetlist(int direction)
    {
        if (_setlist.Songs.Count == 0)
            return;
        var current = _path is null ? -1 : _setlist.Songs.IndexOf(_path);
        var next = current < 0 ? (direction > 0 ? 0 : _setlist.Songs.Count - 1) : current + direction;
        while (next >= 0 && next < _setlist.Songs.Count && Setlist.IsDivider(_setlist.Songs[next]))
            next += direction;
        if (next >= 0 && next < _setlist.Songs.Count)
            LoadFile(_setlist.Songs[next]);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && IsDirty)
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
        }
        base.OnClosing(e);
    }

    private async Task ConfirmCloseAsync()
    {
        if (!await ConfirmKeepOrDiscardEditsAsync())
            return;
        _closeConfirmed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _watcher?.Dispose();
        _libraryWatcher?.Dispose();
        _checkWindow?.Close();
        _scroller.Stop();
        _audio?.Dispose();
        _voice?.Dispose();
        _tuner?.Close();
        base.OnClosed(e);
    }
}

/// <summary>Compares what the library list shows of a song, to tell whether a rescan changed anything.</summary>
file sealed class SongInfoComparer : IEqualityComparer<SongInfo>
{
    public static SongInfoComparer Instance { get; } = new();

    public bool Equals(SongInfo? x, SongInfo? y) =>
        x is not null && y is not null && x.Path == y.Path && x.Title == y.Title && x.Artist == y.Artist &&
        x.Key == y.Key && x.Capo == y.Capo && x.Tempo == y.Tempo && x.Books.SequenceEqual(y.Books);

    public int GetHashCode(SongInfo obj) => obj.Path.GetHashCode();
}
