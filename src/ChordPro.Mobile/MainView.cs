using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChordPro.Core;
using ChordPro.Core.Themes;
using ChordPro.UI;
using UIKit;

namespace ChordPro.Mobile;

/// <summary>The app's screen: the song list, or a song on stage.</summary>
internal sealed class MainView : UserControl
{
    private readonly MobileSettings _settings = MobileSettings.Load();
    private readonly LibraryAccess _access;
    private readonly LibraryView _library;
    private LibrarySongSettings? _songSettings;
    private Dictionary<string, SongInfo> _songsByPath = [];
    private bool _keyIncludesCapo;
    private int _notDownloaded;

    public MainView()
    {
        _access = new LibraryAccess(_settings);
        _library = new LibraryView(_settings, ChooseLibraryAsync, info => Open(info.Path, "Songs", Back), OpenSetlist);
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
        _library.SetSongs(songs, setlists);
        _library.SetStatus(Status(songs.Count));
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
                Play(found, int.Parse(index, System.Globalization.CultureInfo.InvariantCulture));
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
        if (place is not null)
        {
            // Played in a set: counted, so Suggest a set on the Mac can favor familiar songs.
            songSettings.Plays++;
            Saved();
        }
        Content = new StageView(song, songSettings, _settings, _keyIncludesCapo, backLabel, back, Saved, place);
        return true;
    }

    private void OpenSetlist(SetlistSummary setlist)
    {
        var palette = SongPalette.FromTheme(CurrentTheme(_settings));
        Content = new SetlistView(setlist, _songsByPath, palette, Back, index => Play(setlist, index));
    }

    // Plays a setlist from one of its songs: the stage shows what's next, and steps through the set.
    private void Play(SetlistSummary setlist, int index)
    {
        var songs = setlist.Setlist;
        // Songs that aren't in the library are passed over.
        (int Index, string? Divider)? StepFrom(int from, int direction) => songs.Step(from, direction, _songsByPath.ContainsKey);

        string? next = null;
        if (StepFrom(index, +1) is var (nextIndex, divider) && _songsByPath.GetValueOrDefault(songs.Songs[nextIndex]) is { } info)
        {
            next = (divider is null ? "" : $"{divider} · ") + info.Title;
            if (HeardKey(info) is { } key)
                next += $" ({key})";
        }
        var place = new SetlistPlace(next, StepFrom(index, -1) is not null, direction =>
        {
            if (StepFrom(index, direction) is var (to, _))
                Play(setlist, to);
        });
        Open(songs.Songs[index], "Setlist", () => OpenSetlist(setlist), place);
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
