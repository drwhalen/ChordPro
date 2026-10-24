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
    private bool _keyIncludesCapo;
    private int _notDownloaded;

    public MainView()
    {
        _access = new LibraryAccess(_settings);
        _library = new LibraryView(_settings, ChooseLibraryAsync, Open);
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
        var songs = await Task.Run(() => Directory.Exists(folder) ? SongLibrary.Scan(folder) : []);
        _keyIncludesCapo = SongLibrary.GuessKeyIncludesCapo(songs) ?? false;
        _songSettings = LibrarySongSettings.Open(folder);
        _library.SetSongs(songs);
        _library.SetStatus(Status(songs.Count));
#if DEBUG
        // For simulator checks: open a song by title (SIMCTL_CHILD_CHORDPRO_LIVE_OPEN=…).
        if (Environment.GetEnvironmentVariable("CHORDPRO_LIVE_OPEN") is { } title && Content == _library &&
            songs.FirstOrDefault(s => s.Title.Contains(title, StringComparison.OrdinalIgnoreCase)) is { } song)
            Open(song);
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

    private void Open(SongInfo info)
    {
        Song song;
        try
        {
            song = ChordProParser.ParseFile(info.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _library.SetStatus($"Could not open {info.Title}");
            return;
        }
        var store = _songSettings ??= LibrarySongSettings.Open(_access.SongsFolder);
        store.Refresh();
        var key = store.KeyFor(info.Path);
        var songSettings = key is null ? new SongSettings() : store.GetOrAdd(key);
        Content = new StageView(song, songSettings, _settings, _keyIncludesCapo, Back, () =>
        {
            store.Save();
            _access.ShareSettings();
        });
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
