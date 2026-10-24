using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChordPro.Core;
using ChordPro.Core.Themes;
using ChordPro.UI;
using UIKit;

namespace ChordPro.Mobile;

/// <summary>The app's screen: the song list, a setlist or gig, or a song on stage.</summary>
internal sealed class MainView : UserControl
{
    private readonly MobileSettings _settings = MobileSettings.Load();
    private readonly LibraryAccess _access;
    private readonly LibraryView _library;
    private LibrarySongSettings? _songSettings;
    private Dictionary<string, SongInfo> _songsByPath = [];
    private List<SongInfo> _songs = [];
    private List<SetlistSummary> _setlists = [];
    // The gig being played (one at a time), kept in AppFolders.CurrentGig until it ends.
    private Gig? _gig = Gig.Load(AppFolders.CurrentGig);
    private bool _resumed;
    private bool _keyIncludesCapo;
    private int _notDownloaded;

    public MainView()
    {
        _access = new LibraryAccess(_settings);
        _library = new LibraryView(_settings, ChooseLibraryAsync, info => Open(info.Path, "Songs", Back), OpenSetlist, ShowGig, OpenLog);
        Content = _library;
        ApplyTheme();
        // Coming back to the app (from Files, or after a while away): pick up changes made on the Mac.
        UIApplication.Notifications.ObserveWillEnterForeground((_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync()));
    }

    /// <summary>The theme chosen in the app.</summary>
    public static SongTheme CurrentTheme(MobileSettings settings) =>
        SongTheme.BuiltIn.FirstOrDefault(t => t.Name == settings.Theme) ?? SongTheme.Night;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Keep clear of the notch, the Dynamic Island and the home indicator.
        if (TopLevel.GetTopLevel(this)?.InsetsManager is { } insets)
        {
            Padding = insets.SafeAreaPadding;
            insets.SafeAreaChanged += (_, args) => Padding = args.SafeAreaPadding;
        }
        if (!_access.HasLibrary)
            LibraryAccess.ExtractSamples();
        _ = ReloadAsync(); // the copy as it is, at once
        _ = RefreshAsync(); // then any changes from the library
    }

    private async Task ChooseLibraryAsync()
    {
        if (!await _access.ChooseAsync())
            return;
        _library.SetStatus($"{_settings.LibraryName} · copying songs…");
        await RefreshAsync();
    }

    // Brings the copy up to date with the library, then shows it.
    private async Task RefreshAsync()
    {
        if (!_access.HasLibrary)
            return;
        var result = await _access.RefreshAsync();
        if (result is not null)
        {
            _settings.LastSynced = DateTime.UtcNow;
            _settings.Save();
            _notDownloaded = result.NotDownloaded.Count;
        }
        await ReloadAsync();
    }

    // Lists the songs in the copy (or the samples).
    private async Task ReloadAsync()
    {
        var folder = _access.SongsFolder;
        var (songs, setlists) = await Task.Run(() =>
        {
            if (!Directory.Exists(folder))
                return ([], []);
            var songs = SongLibrary.Scan(folder);
            var paths = songs.Select(s => s.Path).ToList();
            var byPath = songs.ToDictionary(s => s.Path);
            List<SetlistSummary> setlists = [];
            foreach (var path in SongLibrary.FindSetlists(folder))
            {
                try
                {
                    var setlist = Setlist.Load(path, paths);
                    var length = SetLength.Of(setlist.Songs.Where(e => !Setlist.IsDivider(e)).Select(e => byPath.GetValueOrDefault(e)?.Length));
                    setlists.Add(new SetlistSummary(path, Path.GetFileNameWithoutExtension(path), setlist, length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A setlist mid-sync is listed next time.
                }
            }
            return (songs, setlists);
        });
        _keyIncludesCapo = SongLibrary.GuessKeyIncludesCapo(songs) ?? false;
        _songSettings = LibrarySongSettings.Open(folder);
        _songsByPath = songs.ToDictionary(s => s.Path);
        _songs = songs;
        _setlists = setlists;
        _library.SetSongs(songs, setlists);
        _library.SetStatus(Status(songs.Count));
        ShowGigs();
        if (!_resumed)
        {
            // Back where the gig was when iOS closed the app, unless it was long ago.
            _resumed = true;
            if (_gig is not null && DateTime.UtcNow - _gig.Started > StaleGig)
                EndGig();
            else if (_gig is not null && Content == _library)
            {
                // How long the song stayed on screen after the app went away isn't known; don't count it.
                _gig.CurrentSince = null;
                ShowGig();
            }
#if DEBUG
            // For simulator checks of a resumed gig: CHORDPRO_LIVE_GIG=request (the picker), edit, or log (ends it, shows the log).
            switch (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_GIG"))
            {
                case "request": ShowRequests(); break;
                case "edit" when Content is GigView view: view.Edit(); break;
                case "log":
                    EndGig();
                    if (Directory.Exists(AppFolders.GigLog))
                        OpenLog(new GigLogEntry(Directory.EnumerateFiles(AppFolders.GigLog).First(), Gig.Load(Directory.EnumerateFiles(AppFolders.GigLog).First())!));
                    break;
            }
#endif
        }
#if DEBUG
        // For simulator checks: open a song by title (SIMCTL_CHILD_CHORDPRO_LIVE_OPEN=…).
        if (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_OPEN") is { } title && Content == _library &&
            songs.FirstOrDefault(s => s.Title.Contains(title, StringComparison.OrdinalIgnoreCase)) is { } song)
            Open(song.Path, "Songs", Back);
        if (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_SETLIST") is { } name && Content == _library &&
            setlists.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            OpenSetlist(found);
            if (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_SETLIST_SONG") is { } index)
                StartGig(found, int.Parse(index, System.Globalization.CultureInfo.InvariantCulture));
        }
#endif
    }

    private string Status(int count)
    {
        if (!_access.HasLibrary)
            return "Sample songs · tap Library… to choose your songs folder";
        var synced = _settings.LastSynced is { } time ? $"updated {Ago(time)}" : "not copied yet";
        var pending = _notDownloaded > 0 ? $" · {_notDownloaded} still downloading" : "";
        return $"{_settings.LibraryName ?? "Library"} · {count} songs · {synced}{pending}";
    }

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : utc.ToLocalTime().ToString("MMM d");
    }

    // Shows a song on stage; false when its file can't be read.
    private bool Open(string path, string backLabel, Action back, SetlistPlace? place = null)
    {
        Song song;
        try
        {
            song = ChordProParser.ParseFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _library.SetStatus($"Could not open {Path.GetFileNameWithoutExtension(path)}");
            return false;
        }
        var store = _songSettings ??= LibrarySongSettings.Open(_access.SongsFolder);
        store.Refresh();
        var key = store.KeyFor(path);
        var songSettings = key is null ? new SongSettings() : store.GetOrAdd(key);
        void Saved()
        {
            store.Save();
            _access.ShareSettings();
        }
        Content = new StageView(song, songSettings, _settings, _keyIncludesCapo, backLabel, back, Saved, place);
        return true;
    }

    private void OpenSetlist(SetlistSummary setlist)
    {
        if (_gig is not null && _gig.Setlist == Relative(setlist.Path))
        {
            ShowGig();
            return;
        }
        var palette = SongPalette.FromTheme(CurrentTheme(_settings));
        Content = new SetlistView(setlist, _songsByPath, palette, Back, index => StartGig(setlist, index));
    }

    // ---- Gigs ------------------------------------------------------------------------------------

    // A gig left this long is over (the app was closed after it, not during it).
    private static readonly TimeSpan StaleGig = TimeSpan.FromHours(12);

    // Paths in a gig are library-relative, so the log still means something after the app's folders move
    // (they do, when iOS updates the app).
    private string Relative(string path) =>
        Setlist.IsDivider(path) ? path : Path.GetRelativePath(_access.SongsFolder, path).Replace('\\', '/');

    private string Full(string song) =>
        Setlist.IsDivider(song) ? song : Path.GetFullPath(Path.Combine(_access.SongsFolder, song));

    private SongInfo? SongFor(string song) => _songsByPath.GetValueOrDefault(Full(song));

    // Plays a setlist from one of its songs, as a new gig (ending one still going).
    private void StartGig(SetlistSummary setlist, int index)
    {
        if (_gig is not null)
            EndGig();
        _gig = Gig.Start(setlist.Name, Relative(setlist.Path), setlist.Setlist.Songs.Select(Relative), DateTime.UtcNow);
        Play(index);
    }

    // Shows a song of the gig on stage: the footer has what's next and the set clock, and steps through the gig.
    private void Play(int index)
    {
        if (_gig is not { } gig)
            return;
        Counted(gig.Show(index, DateTime.UtcNow));
        // Songs that aren't in the library are passed over.
        (int Index, string? Divider)? StepFrom(int from, int direction) => gig.Step(from, direction, song => SongFor(song) is not null);

        string? next = null;
        if (StepFrom(index, +1) is var (nextIndex, divider) && SongFor(gig.Entries[nextIndex].Song) is { } info)
        {
            next = (divider is null ? "" : $"{divider} · ") + info.Title;
            if (HeardKey(info) is { } key)
                next += $" ({key})";
        }
        var place = new SetlistPlace(next, StepFrom(index, -1) is not null, direction =>
        {
            if (StepFrom(index, direction) is var (to, _))
                Play(to);
        }, ClockText);
        if (!Open(Full(gig.Entries[index].Song), "Gig", ShowGig, place))
            ShowGig();
    }

    // The gig's songs: played, skipped, requests, and the set clock.
    private void ShowGig()
    {
        if (_gig is not { } gig)
            return;
        Counted(gig.Leave(DateTime.UtcNow));
        var palette = SongPalette.FromTheme(CurrentTheme(_settings));
        Content = new GigView(gig, SongFor, palette, new GigActions(Back, Play, SaveGig, ShowRequests, () =>
        {
            EndGig();
            Back();
        }, ClockText));
        ApplyTheme();
    }

    private void ShowRequests()
    {
        var palette = SongPalette.FromTheme(CurrentTheme(_settings));
        Content = new RequestView(_songs, palette, ShowGig, (song, place) =>
        {
            if (_gig is not { } gig)
                return;
            var index = gig.Request(Relative(song.Path), atEnd: place == RequestPlace.AtEnd);
            SaveGig();
            if (place == RequestPlace.Now)
                Play(index);
            else
                ShowGig();
        });
    }

    // A song just counted as played in the gig: its play count goes up, so Suggest a set on the Mac can
    // favor familiar songs. The gig is saved either way.
    private void Counted(string? played)
    {
        if (played is not null && _songSettings is { } store && store.KeyFor(Full(played)) is { } key)
        {
            store.GetOrAdd(key).Plays++;
            store.Save();
            _access.ShareSettings();
        }
        SaveGig();
    }

    private void SaveGig()
    {
        try
        {
            _gig?.Save(AppFolders.CurrentGig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting a performance for; the next change tries again.
        }
    }

    // Ends the gig into the log.
    private void EndGig()
    {
        if (_gig is not { } gig)
            return;
        Counted(gig.End(DateTime.UtcNow));
        try
        {
            var name = string.Concat($"{gig.Started.ToLocalTime():yyyy-MM-dd HHmm} {gig.Name}".Split(Path.GetInvalidFileNameChars()));
            gig.Save(Path.Combine(AppFolders.GigLog, name + ".json"));
            File.Delete(AppFolders.CurrentGig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        _gig = null;
        ShowGigs();
    }

    private void ShowGigs()
    {
        List<GigLogEntry> log = [];
        if (Directory.Exists(AppFolders.GigLog))
        {
            foreach (var path in Directory.EnumerateFiles(AppFolders.GigLog, "*.json"))
                if (Gig.Load(path) is { } gig)
                    log.Add(new GigLogEntry(path, gig));
        }
        _library.SetGigs(_gig, [.. log.OrderByDescending(g => g.Gig.Started)]);
    }

    private void OpenLog(GigLogEntry log) =>
        Content = new GigLogView(log, SongFor, SongPalette.FromTheme(CurrentTheme(_settings)), Back);

    // "9:41 · Set 1 · 18 min · 4 min over": the time, and how the set is running against its plan.
    private string ClockText()
    {
        if (_gig is not { } gig)
            return "";
        var now = DateTime.UtcNow;
        if (gig.Update(now) is { } played)
            Counted(played);
        var clock = gig.Clock(now, song => SongFor(song)?.Length);
        List<string> parts = [DateTime.Now.ToString("h:mm"), $"{clock.Set ?? "Set"} · {(int)clock.SetElapsed.TotalMinutes} min"];
        if (clock.Over is { } over)
        {
            var minutes = (int)Math.Round(Math.Abs(over.TotalMinutes));
            parts.Add(minutes == 0 ? "on time" : over > TimeSpan.Zero ? $"{minutes} min over" : $"{minutes} min under");
        }
        return string.Join(" · ", parts);
    }

    // The key a song will be heard in, as its header shows it: after its capo, tuning and saved transpose.
    private string? HeardKey(SongInfo song)
    {
        var saved = _songSettings?.KeyFor(song.Path) is { } key ? _songSettings.Get(key) : null;
        var sounding = ChordTransposer.SoundingKey(song.Key, song.Capo, _keyIncludesCapo, saved?.TuningOffset ?? song.TuningOffset);
        return ChordTransposer.Shift(sounding, saved?.Transpose ?? 0);
    }

    private void Back()
    {
        Content = _library;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var theme = CurrentTheme(_settings);
        WindowTheme.Apply(theme);
        var palette = SongPalette.FromTheme(theme);
        Background = palette.Background;
        _library.ApplyPalette(palette);
    }
}
