using System.Text.Json;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

/// <summary>Per-song view settings that are remembered between sessions.</summary>
public sealed class SongSettings
{
    public int Transpose { get; set; }
    public int? Capo { get; set; }
    public double? FontSize { get; set; }
    /// <summary>Speed multiplier for timed scrolling.</summary>
    public double? ScrollSpeed { get; set; }
    /// <summary>Manual, Timed, Musical or FollowAudio; null means Timed.</summary>
    public string? ScrollMode { get; set; }
    /// <summary>Tempo override for musical scrolling, in BPM.</summary>
    public double? Tempo { get; set; }
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
    public List<string> RecentFiles { get; set; } = [];
    public string? SetlistPath { get; set; }
    public List<string> SetlistSongs { get; set; } = [];
    public bool ShowChords { get; set; } = true;
    public bool ShowDiagrams { get; set; }
    public bool SimplifyChords { get; set; }
    public string Instrument { get; set; } = "Guitar";
    public bool SidebarVisible { get; set; } = true;
    public string Theme { get; set; } = SongTheme.SystemThemeName;
    public List<SongTheme> CustomThemes { get; set; } = [];
    public Dictionary<string, SongSettings> Songs { get; set; } = [];

    public SongSettings ForSong(string path)
    {
        if (!Songs.TryGetValue(path, out var settings))
            Songs[path] = settings = new SongSettings();
        return settings;
    }

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
