using System.Text.RegularExpressions;

namespace ChordPro.Core;

/// <summary>Title and artist of a song file, for listing a library without keeping whole songs in memory.</summary>
public sealed record SongInfo(string Path, string Title, string? Artist)
{
    /// <summary>True when every word of <paramref name="search"/> appears in the title, artist or file name.</summary>
    public bool Matches(string search) =>
        search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
            Title.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
            (Artist?.Contains(word, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            System.IO.Path.GetFileName(Path).Contains(word, StringComparison.CurrentCultureIgnoreCase));
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

    public static SongInfo? ReadInfo(string path)
    {
        try
        {
            var song = ChordProParser.ParseFile(path);
            return new SongInfo(path, song.Title ?? Path.GetFileNameWithoutExtension(path), song.Artist);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
