using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChordPro.Core;

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
    /// <summary>How many times the song was played (scrolled or shown in stage mode), for favoring familiar songs in a set.</summary>
    public int Plays { get; set; }
    /// <summary>The singer marked this song, in its transposed key, as sitting well for their voice.</summary>
    public bool FitsVoice { get; set; }
    /// <summary>The tuning chosen for this song (an instrument name, in the player's family); null to follow the song's <c>{x_tuning}</c>.</summary>
    public string? Tuning { get; set; }
    /// <summary>Semitones this song is tuned down or up (−1 for a half step down); null to follow the song's <c>{x_tuning}</c>.</summary>
    public int? TuningOffset { get; set; }
    /// <summary>
    /// The player's notes for performing the song ("Drop the 2nd verse", "Long intro"), shown above it and
    /// kept out of the song file, so they never change the song itself.
    /// </summary>
    public string? Notes { get; set; }
    /// <summary>When these settings last changed (UTC), so two devices sharing a library keep the newer ones.</summary>
    public DateTime? Modified { get; set; }

    /// <summary>Takes every setting from <paramref name="other"/>, keeping this object (callers may hold on to it).</summary>
    public void CopyFrom(SongSettings other)
    {
        Transpose = other.Transpose;
        Capo = other.Capo;
        FontSize = other.FontSize;
        ScrollSpeed = other.ScrollSpeed;
        ScrollMode = other.ScrollMode;
        Tempo = other.Tempo;
        Plays = other.Plays;
        FitsVoice = other.FitsVoice;
        Tuning = other.Tuning;
        TuningOffset = other.TuningOffset;
        Notes = other.Notes;
        Modified = other.Modified;
    }
}

/// <summary>
/// The per-song settings of one library, kept in a file inside the library folder so that every device
/// the library syncs to (the Mac app, an iPad) shares them. Songs are keyed by their path relative to the
/// library with <c>/</c> separators, which stays the same wherever the folder is mounted.
/// </summary>
/// <remarks>
/// Two devices may save between syncs, so saving merges with what's on disk song by song, the newer
/// <see cref="SongSettings.Modified"/> winning, instead of overwriting the whole file.
/// </remarks>
public sealed class LibrarySongSettings
{
    /// <summary>The settings file's name in the library folder (hidden, so it stays out of the way in Finder and Files).</summary>
    public const string FileName = ".chordpro-viewer.json";

    private readonly Dictionary<string, SongSettings> _songs = new(StringComparer.Ordinal);
    // Each song's settings as last read or written, to tell which ones changed since.
    private readonly Dictionary<string, string> _saved = new(StringComparer.Ordinal);
    private DateTime _fileTime;

    private LibrarySongSettings(string folder) => Folder = Path.GetFullPath(folder);

    /// <summary>The library folder.</summary>
    public string Folder { get; }

    /// <summary>The settings file.</summary>
    public string FilePath => Path.Combine(Folder, FileName);

    /// <summary>Supplies the time a change is stamped with (for tests).</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    /// <summary>The songs with settings, by library-relative path.</summary>
    public IReadOnlyDictionary<string, SongSettings> Songs => _songs;

    /// <summary>Opens a library's settings, reading the file if there is one.</summary>
    public static LibrarySongSettings Open(string folder, Func<DateTime>? clock = null)
    {
        var settings = clock is null ? new LibrarySongSettings(folder) : new LibrarySongSettings(folder) { Clock = clock };
        settings.Refresh();
        return settings;
    }

    /// <summary>The library-relative key for a song, or null when it isn't in this library.</summary>
    public string? KeyFor(string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = Folder.EndsWith(Path.DirectorySeparatorChar) ? Folder : Folder + Path.DirectorySeparatorChar;
        // macOS and Windows file names ignore case, so a path typed differently is still in the library.
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!full.StartsWith(prefix, comparison) || full.Length == prefix.Length)
            return null;
        return full[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>The full path of a song from its library-relative key.</summary>
    public string PathFor(string key) => Path.Combine(Folder, key.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A song's settings, or null when it has none.</summary>
    public SongSettings? Get(string key) => _songs.GetValueOrDefault(key);

    /// <summary>A song's settings, added when it has none.</summary>
    public SongSettings GetOrAdd(string key)
    {
        if (!_songs.TryGetValue(key, out var settings))
            _songs[key] = settings = new SongSettings();
        return settings;
    }

    /// <summary>
    /// Takes settings saved by another device since the file was last read. A song changed here and not
    /// saved yet keeps its own settings.
    /// </summary>
    public void Refresh()
    {
        if (!IsAvailable(Folder, out var exists) || !exists)
            return;
        var time = File.GetLastWriteTimeUtc(FilePath);
        if (time == _fileTime)
            return;
        if (ReadFile(FilePath) is not { } disk)
            return;
        _fileTime = time;
        foreach (var (key, theirs) in disk)
        {
            if (_songs.TryGetValue(key, out var ours) && IsChanged(key, ours))
                continue;
            if (ours is null)
                _songs[key] = theirs;
            else
                ours.CopyFrom(theirs);
            _saved[key] = Snapshot(theirs);
        }
    }

    /// <summary>
    /// Stamps the songs changed since the last save, merges them with the file as it is now (another
    /// device may have written it) and writes the result. Returns false when the file couldn't be read or
    /// written; the changes stay here for the next save.
    /// </summary>
    public bool Save()
    {
        var now = Clock();
        var changed = _songs.Where(s => IsChanged(s.Key, s.Value)).Select(s => s.Key).ToList();
        foreach (var key in changed)
            _songs[key].Modified = now;
        if (changed.Count == 0)
            return true;

        // Never write over a file that's there but can't be read (an iCloud placeholder, a sync in progress):
        // that would throw away the other device's settings.
        if (ReadIfAvailable(Folder) is not { } disk)
            return false;

        foreach (var (key, theirs) in disk)
        {
            if (_songs.TryGetValue(key, out var ours) && (ours.Modified ?? DateTime.MinValue) >= (theirs.Modified ?? DateTime.MinValue))
                continue;
            if (ours is null)
                _songs[key] = theirs;
            else
                ours.CopyFrom(theirs);
        }

        try
        {
            WriteFile(FilePath, Worth(_songs));
            _fileTime = File.GetLastWriteTimeUtc(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var key in changed)
                _saved.Remove(key); // still to be saved
            return false;
        }
        foreach (var (key, settings) in _songs)
            _saved[key] = Snapshot(settings);
        return true;
    }

    /// <summary>
    /// Takes the settings of songs in this library from an older store keyed by full path (the Mac app's
    /// own settings). A song already in this library's file keeps what the file says. Returns the paths that
    /// belong here, for the caller to remove from the old store once this one is saved.
    /// </summary>
    public IReadOnlyList<string> Adopt(IReadOnlyDictionary<string, SongSettings> byFullPath)
    {
        List<string> adopted = [];
        foreach (var (path, settings) in byFullPath)
        {
            if (KeyFor(path) is not { } key)
                continue;
            adopted.Add(path);
            if (!_songs.ContainsKey(key) && !IsDefault(settings))
                _songs[key] = settings;
        }
        return adopted;
    }

    /// <summary>
    /// Merges the settings files of two copies of a library (the library itself and a device's offline copy
    /// of it) song by song, the newer change winning, and writes the result to whichever differs. Returns
    /// false when either file couldn't be read or written, leaving both as they were.
    /// </summary>
    public static bool MergeFiles(string folderA, string folderB)
    {
        if (ReadIfAvailable(folderA) is not { } a || ReadIfAvailable(folderB) is not { } b)
            return false;
        var merged = new Dictionary<string, SongSettings>(a, StringComparer.Ordinal);
        foreach (var (key, theirs) in b)
        {
            if (!merged.TryGetValue(key, out var ours) || (theirs.Modified ?? DateTime.MinValue) > (ours.Modified ?? DateTime.MinValue))
                merged[key] = theirs;
        }
        var text = Serialize(merged);
        try
        {
            foreach (var (folder, before) in new[] { (folderA, a), (folderB, b) })
            {
                if (Serialize(before) != text)
                    WriteFile(Path.Combine(folder, FileName), merged);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return true;
    }

    // The file is readable, or simply not there yet. On a Mac, iCloud may have evicted it to save space,
    // leaving ".name.icloud" in its place.
    private static bool IsAvailable(string folder, out bool exists)
    {
        exists = File.Exists(Path.Combine(folder, FileName));
        return exists || !File.Exists(Path.Combine(folder, "." + FileName + ".icloud"));
    }

    // The folder's settings (none when it has no file yet), or null when there's a file that can't be read.
    private static Dictionary<string, SongSettings>? ReadIfAvailable(string folder)
    {
        if (!IsAvailable(folder, out var exists))
            return null;
        return exists ? ReadFile(Path.Combine(folder, FileName)) : new(StringComparer.Ordinal);
    }

    // Written beside the file and moved over it, so a reader (or a sync) never sees half a file.
    private static void WriteFile(string path, Dictionary<string, SongSettings> songs)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(songs));
        File.Move(temp, path, overwrite: true);
    }

    private static string Serialize(Dictionary<string, SongSettings> songs) =>
        JsonSerializer.Serialize(new SettingsFile { Songs = songs.OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal) },
            SongSettingsJson.Default.SettingsFile);

    private static Dictionary<string, SongSettings>? ReadFile(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(path), SongSettingsJson.Default.SettingsFile);
            return new Dictionary<string, SongSettings>(file?.Songs ?? [], StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // A song that's back to its defaults is still written once it has been saved, so that resetting it
    // reaches the other devices; a song only looked at is not.
    private static Dictionary<string, SongSettings> Worth(Dictionary<string, SongSettings> songs) =>
        songs.Where(s => s.Value.Modified is not null || !IsDefault(s.Value)).ToDictionary(StringComparer.Ordinal);

    private bool IsChanged(string key, SongSettings settings) =>
        Snapshot(settings) != (_saved.GetValueOrDefault(key) ?? DefaultSnapshot);

    private static bool IsDefault(SongSettings settings) => Snapshot(settings) == DefaultSnapshot;

    private static readonly string DefaultSnapshot = Snapshot(new SongSettings());

    // The settings without their time stamp, for comparing.
    private static string Snapshot(SongSettings settings)
    {
        var copy = new SongSettings();
        copy.CopyFrom(settings);
        copy.Modified = null;
        return JsonSerializer.Serialize(copy, SongSettingsJson.Default.SongSettings);
    }

    internal sealed class SettingsFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, SongSettings> Songs { get; set; } = [];
    }
}

// Source-generated, so it needs no reflection (an iOS build is trimmed and compiled ahead of time).
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(LibrarySongSettings.SettingsFile))]
[JsonSerializable(typeof(SongSettings))]
internal sealed partial class SongSettingsJson : JsonSerializerContext;
