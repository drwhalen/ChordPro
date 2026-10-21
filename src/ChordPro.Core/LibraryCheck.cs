using System.Text.RegularExpressions;
using ChordPro.Core.Chords;

namespace ChordPro.Core;

/// <summary>A problem found in a song or setlist file by <see cref="LibraryCheck"/>.</summary>
public sealed record LibraryIssue(string Path, string Name, string Problem);

/// <summary>
/// Looks through a library for mistakes that are easy to make and hard to spot on stage: headings
/// with nothing under them, a key that disagrees with the file name, a title and artist swapped
/// relative to the file name, bracketed text that isn't a
/// chord, fingerings whose name doesn't match the chord as written, and setlist lines that match
/// no song.
/// </summary>
public static partial class LibraryCheck
{
    // "Amazing Grace - John Newton (G)" names the key in brackets at the end.
    [GeneratedRegex(@"\((?<key>[^()]+)\)\s*$")]
    private static partial Regex FileNameKeyRegex();

    // "Title - Artist (Key)", the way LivePrompter libraries name songs.
    [GeneratedRegex(@"^(?<title>.+?) - (?<artist>.+?)(?:\s*\([^()]+\))?\s*$")]
    private static partial Regex FileNameRegex();

    // "No chord" markers that aren't chord names but are used in brackets on purpose.
    private static readonly HashSet<string> NoChord = new(StringComparer.OrdinalIgnoreCase) { "N.C.", "NC", "N.C", "-", "x", "/", "|" };

    public static List<LibraryIssue> Run(IEnumerable<SongInfo> songs, IEnumerable<string> setlists)
    {
        var library = songs.ToList();
        var issues = new List<LibraryIssue>();
        foreach (var info in library)
        {
            try
            {
                issues.AddRange(CheckSong(info.Path, ChordProParser.ParseFile(info.Path)).Select(problem => new LibraryIssue(info.Path, info.Title, problem)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new LibraryIssue(info.Path, info.Title, $"Couldn't read the file: {ex.Message}"));
            }
        }

        var paths = library.Select(s => s.Path).ToList();
        foreach (var path in setlists)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            try
            {
                foreach (var song in Setlist.Load(path, paths).Songs.Where(song => !Setlist.IsDivider(song) && !File.Exists(song)))
                    issues.Add(new LibraryIssue(path, name, $"Setlist line “{Path.GetFileNameWithoutExtension(song)}” matches no song"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new LibraryIssue(path, name, $"Couldn't read the setlist: {ex.Message}"));
            }
        }
        return issues;
    }

    /// <summary>The problems in one song; <paramref name="path"/> is only used for its file name.</summary>
    public static IEnumerable<string> CheckSong(string path, Song song)
    {
        if (song.Key is null)
            yield return "No {key:}";
        else if (FileNameKeyRegex().Match(Path.GetFileNameWithoutExtension(path)) is { Success: true } named &&
                 SongLibrary.ParseKey(named.Groups["key"].Value) is not null &&
                 !SongLibrary.SameKey(named.Groups["key"].Value, song.Key))
            yield return $"File name says key {named.Groups["key"].Value.Trim()} but {{key:}} is {song.Key}";

        if (FileNameRegex().Match(Path.GetFileNameWithoutExtension(path)) is { Success: true } name && song.Title is { } title &&
            title.Trim().Equals(name.Groups["artist"].Value.Trim(), StringComparison.CurrentCultureIgnoreCase) &&
            !title.Trim().Equals(name.Groups["title"].Value.Trim(), StringComparison.CurrentCultureIgnoreCase))
            yield return $"{{title:}} is “{title}”, which the file name gives as the artist — title and artist look swapped";

        foreach (var section in song.Sections.Where(s => s.Label is not null && s.Lines.Count == 0))
            yield return $"Heading “{section.Label}” has nothing under it";

        var used = song.ChordUsages().Distinct().ToList();
        foreach (var chord in used.Where(c => !NoChord.Contains(c.Trim()) && ChordName.TryParse(c) is null))
            yield return $"[{chord}] isn't a chord name";

        // A fingering only shows when its name matches the chord exactly as written in the song.
        foreach (var defined in song.ChordDefinitions.Keys.Where(d => !used.Contains(d)))
            yield return $"Fingering for {defined} is never used (the name must match the chord as written)";
    }
}
