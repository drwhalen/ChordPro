namespace ChordPro.Core;

/// <summary>
/// An ordered list of song files. Saved as plain text, one song per line; blank lines and lines
/// starting with '#' are ignored. A <c>.setlist</c> file holds paths (relative to the setlist where
/// possible). A <c>.txt</c> file is a LivePrompter setlist, which names each song by its file name
/// without the extension.
/// </summary>
public sealed class Setlist
{
    public const string FileExtension = ".setlist";

    public List<string> Songs { get; } = [];

    /// <summary>True for a LivePrompter setlist: a <c>.txt</c> file that isn't itself a ChordPro song.</summary>
    public static bool IsLivePrompterSetlist(string path) =>
        IsLivePrompterFormat(path) && File.Exists(path) && !SongLibrary.LooksLikeChordPro(path);

    private static bool IsLivePrompterFormat(string path) =>
        Path.GetExtension(path).Equals(SongLibrary.TextExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads a setlist. LivePrompter entries are looked up beside the setlist, in its parent folder
    /// (LivePrompter keeps setlists in a Setlists folder inside the songs folder), then among
    /// <paramref name="library"/> by file name.
    /// </summary>
    public static Setlist Load(string path, IEnumerable<string>? library = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var livePrompter = IsLivePrompterFormat(path);
        var setlist = new Setlist();
        foreach (var line in File.ReadAllLines(path))
        {
            var entry = line.Trim();
            if (entry.Length > 0 && !entry.StartsWith('#'))
                setlist.Songs.Add(livePrompter ? FindSong(entry, directory, library) : Path.GetFullPath(entry, directory));
        }
        return setlist;
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        File.WriteAllLines(path, IsLivePrompterFormat(path)
            ? Songs.Select(Path.GetFileNameWithoutExtension).OfType<string>()
            : Songs.Select(song => Path.GetRelativePath(directory, song)));
    }

    private static string FindSong(string name, string directory, IEnumerable<string>? library)
    {
        string[] extensions = [SongLibrary.TextExtension, .. SongLibrary.FileExtensions];
        string?[] folders = [directory, Path.GetDirectoryName(directory)];
        foreach (var folder in folders.OfType<string>())
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder, name + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        if (library?.FirstOrDefault(song => Path.GetFileNameWithoutExtension(song).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } match)
            return match;

        // Keep the name (song names can contain dots, so add the extension rather than trusting
        // GetFileNameWithoutExtension) so the entry shows as missing and survives a save.
        return Path.Combine(Path.GetDirectoryName(directory) ?? directory, name + SongLibrary.TextExtension);
    }
}
