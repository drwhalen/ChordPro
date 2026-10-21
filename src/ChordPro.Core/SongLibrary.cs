using System.Text.RegularExpressions;
using ChordPro.Core.Chords;

namespace ChordPro.Core;

/// <summary>
/// What the library list needs to know about a song file (title, artist, key, capo and books), so a
/// library can be listed, grouped and filtered without keeping whole songs in memory.
/// </summary>
public sealed record SongInfo(string Path, string Title, string? Artist, string? Key = null, int Capo = 0,
    IReadOnlyList<string>? Books = null)
{
    /// <summary>The collections the song belongs to, from <c>{book}</c> directives.</summary>
    public IReadOnlyList<string> Books { get; } = Books ?? [];

    /// <summary>True when every word of <paramref name="search"/> appears in the title, artist or file name.</summary>
    public bool Matches(string search) =>
        search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
            Title.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            (Artist?.Contains(word, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            System.IO.Path.GetFileName(Path).Contains(word, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>A key to filter by: <see cref="Key"/> is one spelling, <see cref="Label"/> names every spelling in the library ("C#/Db").</summary>
public sealed record KeyChoice(string Key, string Label);

/// <summary>How the library list is grouped.</summary>
public enum LibraryGrouping { None, Artist, Book }

/// <summary>A heading in the grouped library (an artist or a book) and the songs under it.</summary>
public sealed record SongGroup(string Name, IReadOnlyList<SongInfo> Songs)
{
    /// <summary>Heading for songs without an <c>{artist}</c> directive.</summary>
    public const string UnknownArtist = "Unknown artist";

    /// <summary>Heading for songs without a <c>{book}</c> directive.</summary>
    public const string NoBook = "Not in a book";
}

public static partial class SongLibrary
{
    public static readonly string[] FileExtensions = [".cho", ".chopro", ".chordpro", ".crd", ".pro"];

    /// <summary>Plain-text extension that songs are often saved with, alongside notes and other text files.</summary>
    public const string TextExtension = ".txt";

    public static bool IsSongFile(string path) =>
        FileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True for ChordPro files, and for <c>.txt</c> files whose text looks like ChordPro, so a folder's
    /// README or notes don't show up in the library.
    /// </summary>
    public static bool IsLibrarySong(string path) =>
        IsSongFile(path) ||
        (Path.GetExtension(path).Equals(TextExtension, StringComparison.OrdinalIgnoreCase) && LooksLikeChordPro(path));

    /// <summary>True when the file has a <c>{directive}</c> line or a <c>[chord]</c> near its start.</summary>
    public static bool LooksLikeChordPro(string path)
    {
        try
        {
            // Directives and chords appear early in a song; reading a bounded prefix keeps scanning
            // a folder of large text files fast.
            using var reader = new StreamReader(path);
            var buffer = new char[16 * 1024];
            var text = new string(buffer, 0, reader.ReadBlock(buffer));
            return ChordProMarkup().IsMatch(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^\s*\{\s*[A-Za-z_]+(-[A-Za-z]+)?\s*[:}]|\[[A-G][#b]?[^\]\s]{0,10}\]", RegexOptions.Multiline)]
    private static partial Regex ChordProMarkup();

    /// <summary>Finds every ChordPro file under <paramref name="folder"/>, sorted by title.</summary>
    public static List<SongInfo> Scan(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(IsLibrarySong)
            .Select(ReadInfo)
            .OfType<SongInfo>()
            .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Groups songs by artist or by book, ignoring case. Artists sort without a leading "The" so
    /// "The Beatles" files under B. A song in several books appears under each. Songs keep their
    /// order within a group; songs with no artist or book come last.
    /// </summary>
    public static List<SongGroup> Group(IEnumerable<SongInfo> songs, LibraryGrouping grouping)
    {
        var fallback = grouping == LibraryGrouping.Book ? SongGroup.NoBook : SongGroup.UnknownArtist;
        return songs
            .SelectMany(song => GroupNames(song, grouping).Select(name => (Name: name, Song: song)))
            .GroupBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g.Key == fallback)
            .ThenBy(g => grouping == LibraryGrouping.Artist ? ArtistSortKey(g.Key) : g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new SongGroup(g.Key, g.Select(entry => entry.Song).ToList()))
            .ToList();
    }

    /// <summary>The names of the groups a song is listed under.</summary>
    public static IEnumerable<string> GroupNames(SongInfo song, LibraryGrouping grouping)
    {
        if (grouping == LibraryGrouping.Book)
        {
            var books = song.Books.Select(b => b.Trim()).Where(b => b.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            return books.Count > 0 ? books : [SongGroup.NoBook];
        }
        return [song.Artist?.Trim() is { Length: > 0 } artist ? artist : SongGroup.UnknownArtist];
    }

    /// <summary>The artist name used for sorting: without a leading "The ".</summary>
    public static string ArtistSortKey(string artist) =>
        artist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? artist[4..].TrimStart() : artist;

    /// <summary>
    /// Songs matching the search text, a key you hear (null for any) and a capo fret (null for any, 0
    /// for songs played without a capo). <paramref name="keyIncludesCapo"/>: see <see cref="ChordTransposer.WrittenKey"/>.
    /// </summary>
    public static List<SongInfo> Filter(IEnumerable<SongInfo> songs, string? search, string? key = null, int? capo = null,
        bool keyIncludesCapo = false) =>
        songs.Where(s => string.IsNullOrWhiteSpace(search) || s.Matches(search))
            .Where(s => key is null || SameKey(ChordTransposer.SoundingKey(s.Key, s.Capo, keyIncludesCapo), key))
            .Where(s => capo is null || s.Capo == capo)
            .ToList();

    /// <summary>
    /// The keys you hear in a library, in pitch order from C with each major key before its minor.
    /// Spellings of the same key (C# and Db) are one choice.
    /// </summary>
    public static List<KeyChoice> Keys(IEnumerable<SongInfo> songs, bool keyIncludesCapo = false) =>
        songs.Select(s => ChordTransposer.SoundingKey(s.Key, s.Capo, keyIncludesCapo)?.Trim()).OfType<string>().Where(k => k.Length > 0)
            .GroupBy(k => ParseKey(k) is var (root, minor) ? $"{root}{(minor ? "m" : "")}" : k.ToLowerInvariant())
            .Select(g =>
            {
                // Sharps before flats, as the circle of fifths reaches them first going up.
                var spellings = g.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k.Length > 1 && k[1] is 'b' or '♭').ThenBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).ToList();
                return (Parsed: ParseKey(spellings[0]), Choice: new KeyChoice(spellings[0], string.Join("/", spellings)));
            })
            .OrderBy(k => k.Parsed?.Root ?? 12)
            .ThenBy(k => k.Parsed?.Minor ?? false)
            .ThenBy(k => k.Choice.Label, StringComparer.Ordinal)
            .Select(k => k.Choice)
            .ToList();

    /// <summary>
    /// Guesses whether a library's <c>{key}</c> includes the capo (the key you hear, as LivePrompter
    /// charts are written) or is the key of the chords as written (ChordPro's meaning). Songs with a
    /// capo decide it: a song usually starts or ends on its key chord, so if that chord plus the capo
    /// is the <c>{key}</c>, the key includes the capo. Null when no song gives a clear answer.
    /// </summary>
    public static bool? GuessKeyIncludesCapo(IEnumerable<SongInfo> songs)
    {
        int written = 0, heard = 0;
        foreach (var info in songs.Where(s => s.Capo > 0 && s.Key is not null))
        {
            if (ParseKey(info.Key!) is not var (root, minor))
                continue;
            List<ChordName> chords;
            try
            {
                chords = [.. ChordProParser.ParseFile(info.Path).ChordUsages().Select(ChordName.TryParse).OfType<ChordName>()];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (chords.Count == 0)
                continue;

            bool EndsOnKey(int shift) => new[] { chords[0], chords[^1] }
                .Any(c => (c.Root + shift) % 12 == root && c.Intervals.Contains(3) == minor);
            var (asWritten, asHeard) = (EndsOnKey(0), EndsOnKey(info.Capo));
            if (asWritten && !asHeard)
                written++;
            else if (asHeard && !asWritten)
                heard++;
        }
        return written == heard ? null : heard > written;
    }

    /// <summary>True when two keys are the same, however they're spelled: <c>C#</c> and <c>Db</c>, <c>Em</c> and <c>E minor</c>.</summary>
    public static bool SameKey(string? a, string? b)
    {
        if (a is null || b is null)
            return false;
        return ParseKey(a) is { } x && ParseKey(b) is { } y ? x == y : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The tonic's pitch class (0 = C) and whether the key is minor, or null if it isn't a key name.</summary>
    public static (int Root, bool Minor)? ParseKey(string key)
    {
        var match = KeyRegex().Match(key);
        return match.Success ? (ChordName.Note(match.Groups["root"].Value.ToUpperInvariant()[..1] + match.Groups["root"].Value[1..]), match.Groups["minor"].Success) : null;
    }

    [GeneratedRegex(@"^\s*(?<root>[A-Ga-g][#b♯♭]?)\s*(?:(?<minor>m|min|minor)|maj|major)?\s*$")]
    private static partial Regex KeyRegex();

    /// <summary>
    /// Setlists inside a library folder: <c>.setlist</c> files anywhere, and LivePrompter setlists in
    /// a folder named Setlists. Sorted by name.
    /// </summary>
    public static List<string> FindSetlists(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(path => Path.GetExtension(path).Equals(Setlist.FileExtension, StringComparison.OrdinalIgnoreCase) ||
                (Path.GetFileName(Path.GetDirectoryName(path))?.Equals("Setlists", StringComparison.OrdinalIgnoreCase) == true &&
                 Setlist.IsLivePrompterSetlist(path)))
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The distinct capo frets used in a library, lowest first (0 means no capo).</summary>
    public static List<int> Capos(IEnumerable<SongInfo> songs) => songs.Select(s => s.Capo).Distinct().Order().ToList();

    public static SongInfo? ReadInfo(string path)
    {
        try
        {
            var song = ChordProParser.ParseFile(path);
            return new SongInfo(path, song.Title ?? Path.GetFileNameWithoutExtension(path), song.Artist,
                song.Key?.Trim(), song.CapoFret, song.GetMetaValues("book"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
