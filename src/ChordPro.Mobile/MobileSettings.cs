using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation;

namespace ChordPro.Mobile;

/// <summary>Where the app keeps its files, in Application Support (kept, unlike caches, and not shown in Files).</summary>
internal static class AppFolders
{
    public static string Support { get; } = Path.Combine(
        NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!,
        "ChordPro Live");

    /// <summary>The device's own copy of the library, read on stage (see <see cref="Core.LibraryMirror"/>).</summary>
    public static string LibraryCopy => Path.Combine(Support, "Library");

    /// <summary>The bundled sample songs, shown until a library is chosen.</summary>
    public static string Samples => Path.Combine(Support, "Samples");
}

/// <summary>The app's own preferences. Per-song settings live with the library (<see cref="Core.LibrarySongSettings"/>).</summary>
internal sealed class MobileSettings
{
    private static string FilePath => Path.Combine(AppFolders.Support, "settings.json");

    /// <summary>iOS's bookmark for the chosen library folder (base64), which keeps access to it across launches.</summary>
    public string? LibraryBookmark { get; set; }
    /// <summary>The library folder's name, shown while it can't be reached.</summary>
    public string? LibraryName { get; set; }
    /// <summary>When the copy last matched the library.</summary>
    public DateTime? LastSynced { get; set; }
    public string Theme { get; set; } = Core.Themes.SongTheme.Night.Name;
    /// <summary>A <see cref="Core.LibraryGrouping"/> name.</summary>
    public string? Grouping { get; set; }
    /// <summary>The player's instrument (an <see cref="Core.Chords.Instrument"/> name), for diagrams, fingerings and the tuner.</summary>
    public string Instrument { get; set; } = Core.Chords.Instrument.Guitar.Name;
    public bool ShowDiagrams { get; set; }
    /// <summary>Easy chords (<see cref="Core.Chords.ChordSimplifier"/>).</summary>
    public bool SimplifyChords { get; set; }
    /// <summary>Nashville numbers under the chords.</summary>
    public bool ChordNumbers { get; set; }
    /// <summary>The tuner's A4, in Hz.</summary>
    public double TunerReference { get; set; } = Core.Tuning.Note.StandardReference;

    public static MobileSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), MobileJson.Default.MobileSettings) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable preferences fall back to the defaults.
        }
        return new MobileSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppFolders.Support);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, MobileJson.Default.MobileSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting a performance for.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MobileSettings))]
internal sealed partial class MobileJson : JsonSerializerContext;
