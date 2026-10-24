using System.Text.Json;
using ChordPro.Core;
using ChordPro.Core.Scrolling;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

/// <summary>A library-check problem marked "Looks fine", for as long as its file stays as it was then.</summary>
public sealed class AcceptedIssue
{
    public string Path { get; set; } = "";
    public string Problem { get; set; } = "";
    /// <summary>The file's last-write time (UTC ticks) when it was accepted.</summary>
    public long FileTicks { get; set; }
}

/// <summary>Settings saved as JSON in the user's application-data folder.</summary>
public sealed class AppSettings
{
    private const int MaxRecentFiles = 20;

    // CHORDPRO_VIEWER_SETTINGS overrides the location (useful for testing).
    private static readonly string FilePath = Environment.GetEnvironmentVariable("CHORDPRO_VIEWER_SETTINGS") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChordPro Viewer", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? LibraryFolder { get; set; }
    /// <summary>The artist the last new song was by, offered for the next one.</summary>
    public string? LastArtist { get; set; }
    /// <summary>None, Artist or Book.</summary>
    public string? LibraryGroupBy { get; set; }
    /// <summary>Older on/off setting for grouping by artist, used when <see cref="LibraryGroupBy"/> isn't set.</summary>
    public bool GroupLibraryByArtist { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public LibraryGrouping Grouping => Enum.TryParse<LibraryGrouping>(LibraryGroupBy, out var grouping) ? grouping
        : GroupLibraryByArtist ? LibraryGrouping.Artist : LibraryGrouping.None;
    public List<string> RecentFiles { get; set; } = [];
    public string? SetlistPath { get; set; }
    public List<string> SetlistSongs { get; set; } = [];
    public bool ShowChords { get; set; } = true;
    public bool ShowDiagrams { get; set; }
    public bool SimplifyChords { get; set; }
    /// <summary>Show chords as Nashville numbers (1 4 5) in their key.</summary>
    public bool ChordNumbers { get; set; }
    /// <summary>Songs' <c>{key}</c> is the key heard with the capo on, as LivePrompter writes it.</summary>
    public bool KeyIncludesCapo { get; set; }
    /// <summary>The library folder <see cref="KeyIncludesCapo"/> was last chosen for, by guessing or by hand.</summary>
    public string? KeyConventionFolder { get; set; }
    /// <summary>The tuner's reference pitch for A4, in Hz.</summary>
    public double TunerReference { get; set; } = 440;
    public string Instrument { get; set; } = "Guitar";
    public bool SidebarVisible { get; set; } = true;
    public string Theme { get; set; } = SongTheme.SystemThemeName;
    public List<SongTheme> CustomThemes { get; set; } = [];
    public List<AcceptedIssue> AcceptedIssues { get; set; } = [];
    /// <summary>Follow voice settings found by training; null for the standard ones.</summary>
    public VoiceTuning? VoiceTuning { get; set; }

    /// <summary>
    /// Settings of songs outside the library folder, by full path. Songs in the library keep theirs in the
    /// library's own file (<see cref="LibrarySongSettings"/>), shared with other devices.
    /// </summary>
    public Dictionary<string, SongSettings> Songs { get; set; } = [];

    private LibrarySongSettings? _library;

    // The library's shared song settings, opened when first needed; the first time, the settings this
    // app kept for its songs by full path move there.
    private LibrarySongSettings? Library
    {
        get
        {
            if (LibraryFolder is not { } folder)
                return null;
            if (_library?.Folder != Path.GetFullPath(folder))
            {
                if (!Directory.Exists(folder))
                    return null;
                _library = LibrarySongSettings.Open(folder);
                var adopted = _library.Adopt(Songs);
                if (adopted.Count > 0 && _library.Save())
                {
                    foreach (var path in adopted)
                        Songs.Remove(path);
                    SaveFile();
                }
            }
            return _library;
        }
    }

    /// <summary>A song's settings, added when it has none.</summary>
    public SongSettings ForSong(string path)
    {
        if (Library is { } library && library.KeyFor(path) is { } key)
            return library.GetOrAdd(key);
        if (!Songs.TryGetValue(path, out var settings))
            Songs[path] = settings = new SongSettings();
        return settings;
    }

    /// <summary>A song's settings, or null when it has none.</summary>
    public SongSettings? SettingsOf(string path) =>
        Library is { } library && library.KeyFor(path) is { } key ? library.Get(key) : Songs.GetValueOrDefault(path);

    /// <summary>Every song with settings, by full path.</summary>
    public IEnumerable<KeyValuePair<string, SongSettings>> AllSongSettings() =>
        (Library?.Songs.Select(s => KeyValuePair.Create(_library!.PathFor(s.Key), s.Value)) ?? []).Concat(Songs);

    /// <summary>Takes song settings another device saved to the library since they were last read.</summary>
    public void RefreshSongSettings() => Library?.Refresh();

    public void AddRecentFile(string path)
    {
        RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings shouldn't stop the app from starting; fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        _library?.Save();
        SaveFile();
    }

    private void SaveFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save preferences is not worth interrupting the user for.
        }
    }
}
