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

public static class SongLibrary
{
    public static readonly string[] FileExtensions = [".cho", ".chopro", ".chordpro", ".crd", ".pro"];

    public static bool IsSongFile(string path) =>
        FileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds every ChordPro file under <paramref name="folder"/>, sorted by title.</summary>
    public static List<SongInfo> Scan(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(IsSongFile)
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
