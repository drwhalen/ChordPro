using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChordPro.Core;
using ChordPro.Core.Chords;

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

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AutoScroller _autoScroller;
    private readonly DispatcherTimer _reloadTimer;
    private FileSystemWatcher? _watcher;

    private Song? _song;
    private string? _path;
    private string? _loadedText;
    private double _fontSize = DefaultFontSize;
    private int _transpose;
    private int? _capo;
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

        _autoScroller = new AutoScroller(SongScroller);
        _autoScroller.RunningChanged += (_, _) => UpdateScrollControls();
        _reloadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => ReloadChangedFile());

        AddHandler(DragDrop.DropEvent, OnDrop);
        // Tunnel so shortcuts work even when a list or the scroll view has focus.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

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

        _autoScroller.Stop();
        _song = song;
        _loadedText = text;
        _path = Path.GetFullPath(path);

        var songSettings = _settings.ForSong(_path);
        _transpose = songSettings.Transpose;
        _capo = songSettings.Capo;
        _fontSize = songSettings.FontSize ?? DefaultFontSize;
        _autoScroller.SpeedFactor = songSettings.ScrollSpeed ?? 1;
        _autoScroller.Duration = AutoScroller.ParseDuration(song.Duration);

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

        var offset = SongScroller.Offset;
        _autoScroller.Duration = AutoScroller.ParseDuration(_song.Duration);
        RenderSong();
        SongScroller.UpdateLayout();
        SongScroller.Offset = offset;
        StatusText.Text = $"{Path.GetFileName(_path)} — reloaded {DateTime.Now:t}";
    }

    private void RenderSong()
    {
        if (_song is null)
            return;

        _renderedColumns = _stageMode && Bounds.Width >= TwoColumnMinWidth ? 2 : 1;
        SongHost.Content = new SongRenderer(CurrentRenderOptions() with { Columns = _renderedColumns }).Render(_song);
    }

    private RenderOptions CurrentRenderOptions() => new()
    {
        FontSize = _fontSize * (_stageMode ? StageFontScale : 1),
        Transpose = _transpose,
        Capo = _capo,
        ShowChords = _settings.ShowChords,
        ShowDiagrams = _settings.ShowDiagrams,
        Instrument = Instrument.All.FirstOrDefault(i => i.Name == _settings.Instrument) ?? Instrument.Guitar,
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
        if (change.Property == BoundsProperty && _stageMode &&
            (Bounds.Width >= TwoColumnMinWidth ? 2 : 1) != _renderedColumns)
            RenderSong();
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
        songSettings.ScrollSpeed = Math.Abs(_autoScroller.SpeedFactor - 1) < 0.01 ? null : _autoScroller.SpeedFactor;
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

    // ---- Auto-scroll -------------------------------------------------------------------------

    private void OnScrollToggleClick(object? sender, RoutedEventArgs e) => _autoScroller.Toggle();

    private void OnScrollSlowerClick(object? sender, RoutedEventArgs e) => SetScrollSpeed(_autoScroller.SpeedFactor - ScrollSpeedStep);

    private void OnScrollFasterClick(object? sender, RoutedEventArgs e) => SetScrollSpeed(_autoScroller.SpeedFactor + ScrollSpeedStep);

    private void SetScrollSpeed(double factor)
    {
        _autoScroller.SpeedFactor = Math.Round(Math.Clamp(factor, MinScrollSpeed, MaxScrollSpeed), 1);
        SaveSongSettings();
        UpdateScrollControls();
    }

    private void UpdateScrollControls()
    {
        ScrollToggle.IsChecked = _autoScroller.IsRunning;
        ScrollToggle.Content = _autoScroller.IsRunning ? "❚❚ Scroll" : "▶ Scroll";
        SpeedText.Text = $"{_autoScroller.SpeedFactor:0.0}×";
        ToolTip.SetTip(SpeedText, _autoScroller.Duration is { } d
            ? $"Paced to finish in {d:m\\:ss} at 1.0×"
            : "Add {duration: m:ss} to the song to pace scrolling to its length");
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

        if (e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Space: _autoScroller.Toggle(); break;
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
        _autoScroller.Stop();
        base.OnClosed(e);
    }
}
