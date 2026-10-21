namespace ChordPro.Core;

/// <summary>
/// An ordered list of song files. Saved as plain text, one path per line (relative to the setlist
/// file where possible); blank lines and lines starting with '#' are ignored.
/// </summary>
public sealed class Setlist
{
    public const string FileExtension = ".setlist";

    public List<string> Songs { get; } = [];

    public static Setlist Load(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var setlist = new Setlist();
        foreach (var line in File.ReadAllLines(path))
        {
            var entry = line.Trim();
            if (entry.Length > 0 && !entry.StartsWith('#'))
                setlist.Songs.Add(Path.GetFullPath(entry, directory));
        }
        return setlist;
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        File.WriteAllLines(path, Songs.Select(song => Path.GetRelativePath(directory, song)));
    }
}
