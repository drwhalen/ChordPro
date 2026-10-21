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
using ChordPro.Core;
using ChordPro.Core.Chords;
using ChordPro.Core.Scrolling;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ChordProFiles = new("ChordPro songs")
    {
        Patterns = [.. SongLibrary.FileExtensions.Select(e => "*" + e), "*.txt"],
    };

    private static readonly FilePickerFileType SetlistFiles = new("Setlists") { Patterns = ["*" + Setlist.FileExtension] };

    private const double DefaultFontSize = 16, MinFontSize = 10, MaxFontSize = 40;
    private const double StageFontScale = 1.4;
    private const double TwoColumnMinWidth = 1000;
    private const double MinScrollSpeed = 0.2, MaxScrollSpeed = 4, ScrollSpeedStep = 0.1;
    private const double MinTempo = 30, MaxTempo = 300, TempoStep = 2;
    private const double TapZone = 0.4;            // top/bottom fraction of the song that turns pages
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(200);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SmartScroller _scroller;
    private readonly DispatcherTimer _reloadTimer;
    private FileSystemWatcher? _watcher;

    private Song? _song;
    private SongRenderer? _renderer;
    private MusicalTimeline? _musical;
    private string? _path;
    private string? _loadedText;
    private double _fontSize = DefaultFontSize;
    private int _transpose;
    private int? _capo;
    private ScrollMode _scrollMode = ScrollMode.Timed;
    private double _speed = 1;
    private double? _tempo;
    private AudioFollower? _audio;
    private DateTime _lastStatusUpdate;
    private Point? _pressPosition;
    private bool _updatingScrollMode;
    private bool _stageMode;
    private int _renderedColumns = 1;
    private WindowState _windowStateBeforeStage;

    private List<SongInfo> _library = [];
    private readonly Setlist _setlist = new();
    private string? _setlistPath;
    private bool _updatingLists;
    private bool _updatingViewOptions;

    public MainWindow() : this(null) { }

    public MainWindow(string? initialPath)
    {
        InitializeComponent();

        _scroller = new SmartScroller(SongScroller) { TimelineFactory = BuildTimeline };
        _scroller.RunningChanged += (_, _) => OnScrollRunningChanged();
        _scroller.Progressed += (_, _) => UpdateScrollStatus();
        // Tap the top or bottom of the song to turn pages in manual mode.
        SongScroller.AddHandler(PointerPressedEvent, OnSongPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SongScroller.AddHandler(PointerReleasedEvent, OnSongPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _reloadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => ReloadChangedFile());

        AddHandler(DragDrop.DropEvent, OnDrop);
        // Tunnel so shortcuts work even when a list or the scroll view has focus.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        // With the System theme, follow the OS switching between light and dark.
        ActualThemeVariantChanged += (_, _) => RefreshThemeColors();

        ApplySavedSettings();

        var startPath = initialPath ?? _settings.RecentFiles.FirstOrDefault(File.Exists);
        if (startPath is not null)
            LoadFile(startPath);
        else
            ShowMessage("Open a ChordPro file, choose a library folder, or drop a song onto this window.");
    }

    // ---- Loading ---------------------------------------------------------------------------

    private void LoadFile(string path)
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
        SyncListSelections();
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
        catch (IOException)
        {
            _reloadTimer.Start(); // still being written; try again shortly
            return;
        }
        // File events can arrive without a real change (e.g. when watching starts on macOS).
        if (text == _loadedText)
            return;
        _loadedText = text;
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

    private void UpdateSongControls()
    {
        TransposeResetButton.Content = _transpose > 0 ? $"+{_transpose}" : _transpose < 0 ? $"−{-_transpose}" : "0";
        TransposeResetButton.IsEnabled = _transpose != 0;
        CapoResetButton.Content = (_capo ?? FileCapo).ToString();
        CapoResetButton.IsEnabled = _capo is not null;
        UpdateScrollControls();
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
        _scroller.Start();
    }

    private void OnScrollRunningChanged()
    {
        if (!_scroller.IsRunning)
        {
            _audio?.Dispose();
            _audio = null;
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
            ScrollMode.Musical or ScrollMode.FollowAudio => builder.Musical(_musical),
            _ => null,
        };
    }

    private double CurrentTempo => _tempo ?? _musical?.Tempo ?? MusicalTimeline.DefaultTempo;

    private void ApplyScrollRate()
    {
        var writtenTempo = _musical?.Tempo ?? MusicalTimeline.DefaultTempo;
        _scroller.Rate = _scrollMode == ScrollMode.Timed ? _speed : CurrentTempo / writtenTempo;
        _scroller.LiveRate = _scrollMode == ScrollMode.FollowAudio ? FollowRate : null;
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

    private void OnPageUpClick(object? sender, RoutedEventArgs e) => _scroller.Page(-1);

    private void OnPageDownClick(object? sender, RoutedEventArgs e) => _scroller.Page(+1);

    private void OnSongPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressPosition = e.GetCurrentPoint(SongScroller).Properties.IsLeftButtonPressed ? e.GetPosition(SongScroller) : null;

    private void OnSongPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_scrollMode != ScrollMode.Manual || _pressPosition is not { } pressed)
            return;
        _pressPosition = null;
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
        ScrollToggle.IsChecked = _scroller.IsRunning;
        ScrollToggle.Content = _scroller.IsRunning ? "❚❚ Scroll" : _scrollMode == ScrollMode.FollowAudio ? "▶ Listen" : "▶ Scroll";

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
        GuitarRadio.IsChecked = _settings.Instrument != Instrument.Ukulele.Name;
        UkuleleRadio.IsChecked = _settings.Instrument == Instrument.Ukulele.Name;
        ShowDiagramsCheck.IsEnabled = _settings.ShowChords;
        _updatingViewOptions = false;

        SetSidebarVisible(_settings.SidebarVisible);
        SetTheme(_settings.Theme);
        LibraryFolderText.Text = _settings.LibraryFolder ?? "No folder chosen";
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
        _settings.Instrument = UkuleleRadio.IsChecked == true ? Instrument.Ukulele.Name : Instrument.Guitar.Name;
        ShowDiagramsCheck.IsEnabled = _settings.ShowChords;
        _settings.Save();
        RenderSong();
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

    private void SetStageMode(bool on)
    {
        if (on == _stageMode)
            return;
        _stageMode = on;
        StageToggle.IsChecked = on;
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
        if (e.Source is TextBox)
            return; // let the search box have its keys

        // Cmd on macOS, Ctrl elsewhere.
        var command = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(command))
        {
            switch (e.Key)
            {
                case Key.O: OnOpenClick(this, e); break;
                case Key.P: OnExportPdfClick(this, e); break;
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
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is not { } path)
            return;
        if (Path.GetExtension(path).Equals(Setlist.FileExtension, StringComparison.OrdinalIgnoreCase))
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
            LoadFile(path);
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

    private async Task ScanLibraryAsync()
    {
        if (_settings.LibraryFolder is not { } folder || !Directory.Exists(folder))
        {
            _library = [];
            ApplyLibraryFilter();
            return;
        }
        LibraryFolderText.Text = $"{folder} (scanning…)";
        _library = await Task.Run(() => SongLibrary.Scan(folder));
        LibraryFolderText.Text = $"{folder} · {_library.Count} songs";
        ToolTip.SetTip(LibraryFolderText, folder);
        ApplyLibraryFilter();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyLibraryFilter();

    private void ApplyLibraryFilter()
    {
        var search = SearchBox.Text?.Trim() ?? "";
        _updatingLists = true;
        LibraryList.ItemsSource = search.Length == 0 ? _library : _library.Where(s => s.Matches(search)).ToList();
        _updatingLists = false;
        SyncListSelections();
    }

    private void RefreshRecentList()
    {
        _updatingLists = true;
        RecentList.ItemsSource = _settings.RecentFiles.Where(File.Exists).Select(SongLibrary.ReadInfo).OfType<SongInfo>().ToList();
        _updatingLists = false;
    }

    private void OnSongListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingLists || sender is not ListBox { SelectedItem: SongInfo info } || info.Path == _path)
            return;
        LoadFile(info.Path);
    }

    // Highlight the open song in whichever lists contain it.
    private void SyncListSelections()
    {
        _updatingLists = true;
        foreach (var list in new[] { LibraryList, RecentList, SetlistList })
        {
            var items = list.ItemsSource as IList<SongInfo>;
            var index = items?.ToList().FindIndex(s => s.Path == _path) ?? -1;
            list.SelectedIndex = index;
            if (index >= 0)
                list.ScrollIntoView(index);
        }
        _updatingLists = false;
    }

    // ---- Setlists ----------------------------------------------------------------------------

    private void RefreshSetlist()
    {
        SetlistNameText.Text = _setlistPath is null
            ? $"Unsaved setlist · {_setlist.Songs.Count} songs"
            : $"{Path.GetFileNameWithoutExtension(_setlistPath)} · {_setlist.Songs.Count} songs";

        _updatingLists = true;
        SetlistList.ItemsSource = _setlist.Songs
            .Select(p => SongLibrary.ReadInfo(p) ?? new SongInfo(p, $"{Path.GetFileName(p)} (missing)", null))
            .ToList();
        _updatingLists = false;
        SyncListSelections();
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
            var loaded = Setlist.Load(path);
            _setlist.Songs.Clear();
            _setlist.Songs.AddRange(loaded.Songs);
            _setlistPath = path;
            SetlistChanged(saveFile: false);
            if (_setlist.Songs.FirstOrDefault(File.Exists) is { } first)
                LoadFile(first);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not open setlist: {ex.Message}";
        }
    }

    private async void OnSaveSetlistClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save setlist",
            SuggestedFileName = _setlistPath is null ? "Setlist" + Setlist.FileExtension : Path.GetFileName(_setlistPath),
            DefaultExtension = Setlist.FileExtension.TrimStart('.'),
            FileTypeChoices = [SetlistFiles],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            _setlistPath = path;
            SetlistChanged();
        }
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
        if (next >= 0 && next < _setlist.Songs.Count)
            LoadFile(_setlist.Songs[next]);
    }

    protected override void OnClosed(EventArgs e)
    {
        _watcher?.Dispose();
        _scroller.Stop();
        _audio?.Dispose();
        base.OnClosed(e);
    }
}
