using System.Text.RegularExpressions;

namespace ChordPro.Core;

/// <summary>
/// An ordered list of song files. Saved as plain text, one song per line; blank lines and lines
/// starting with '#' are ignored. A <c>.setlist</c> file holds paths (relative to the setlist where
/// possible). A <c>.txt</c> file is a LivePrompter setlist, which names each song by its file name
/// without the extension. Either may split sets with divider lines like <c>--- Set 2 ---</c>.
/// </summary>
public sealed partial class Setlist
{
    public const string FileExtension = ".setlist";

    [GeneratedRegex(@"^\s*-{2,}.*?-{2,}\s*$")]
    private static partial Regex DividerRegex();

    /// <summary>True for a divider between sets, such as <c>--- Set 2 ---</c>; it's kept in <see cref="Songs"/> as written.</summary>
    public static bool IsDivider(string entry) => DividerRegex().IsMatch(entry);

    /// <summary>A divider's name without its dashes ("Set 2").</summary>
    public static string DividerName(string entry) => entry.Trim().Trim('-').Trim();

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
            if (entry.Length == 0 || entry.StartsWith('#'))
                continue;
            if (IsDivider(entry))
                setlist.Songs.Add(entry);
            else
                setlist.Songs.Add(livePrompter ? FindSong(entry, directory, library) : Path.GetFullPath(entry, directory));
        }
        return setlist;
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var livePrompter = IsLivePrompterFormat(path);
        File.WriteAllLines(path, Songs.Select(song =>
            IsDivider(song) ? song : livePrompter ? Path.GetFileNameWithoutExtension(song) : Path.GetRelativePath(directory, song)));
    }

    private static string FindSong(string name, string directory, IEnumerable<string>? library)
    {
        string[] extensions = [SongLibrary.TextExtension, .. SongLibrary.FileExtensions];
        string?[] folders = [directory, Path.GetDirectoryName(directory)];
        // An entry may also be written with its extension ("Song - Artist (G).txt").
        string[] names = extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))
            ? [name, .. extensions.Select(e => name + e)]
            : [.. extensions.Select(e => name + e)];
        foreach (var folder in folders.OfType<string>())
        {
            foreach (var file in names)
            {
                var candidate = Path.Combine(folder, file);
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
