using System.Text.RegularExpressions;
using ChordPro.Core.Chords;
using ChordPro.Core.Layout;

namespace ChordPro.Core;

/// <summary>A problem found in a song or setlist file by <see cref="LibraryCheck"/>.</summary>
public sealed record LibraryIssue(string Path, string Name, string Problem);

/// <summary>
/// Looks through a library for mistakes that are easy to make and hard to spot on stage: headings
/// with nothing under them, a key that disagrees with the file name, a title and artist swapped
/// relative to the file name, bracketed text that isn't a
/// chord, fingerings whose name doesn't match the chord as written, and setlist lines that match
/// no song, and artist names spelled two ways. For a LivePrompter library it also finds lines
/// LivePrompter would cut off.
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

    /// <param name="livePrompter">Also report lines too wide for LivePrompter's screen; otherwise,
    /// report where a song strays from ChordPro's own ways of writing things (see <see cref="CheckChordProStyle"/>).</param>
    public static List<LibraryIssue> Run(IEnumerable<SongInfo> songs, IEnumerable<string> setlists, bool livePrompter = false)
    {
        var library = songs.ToList();
        var issues = new List<LibraryIssue>();
        foreach (var info in library)
        {
            try
            {
                var text = File.ReadAllText(info.Path);
                var song = ChordProParser.Parse(text);
                var problems = CheckSong(info.Path, song);
                problems = livePrompter
                    ? problems.Concat(CheckLineWidths(text))
                    : problems.Concat(CheckChordProStyle(text, song));
                issues.AddRange(problems.Select(problem => new LibraryIssue(info.Path, info.Title, problem)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new LibraryIssue(info.Path, info.Title, $"Couldn't read the file: {ex.Message}"));
            }
        }

        foreach (var (song, problem) in CheckArtistSpellings(library))
            issues.Add(new LibraryIssue(song.Path, song.Title, problem));

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

    /// <summary>
    /// Songs whose artist is another artist in the library written differently: the same name with or
    /// without "The" or punctuation ("Eagles", "The Eagles"), which the library lists as two artists,
    /// or a near miss a letter or two apart ("Ozzy Osborne", "Ozzy Osbourne"). The spelling used by
    /// fewer songs is the one reported, though it isn't always the wrong one.
    /// </summary>
    public static IEnumerable<(SongInfo Song, string Problem)> CheckArtistSpellings(IEnumerable<SongInfo> songs)
    {
        var byArtist = songs.Where(s => !string.IsNullOrWhiteSpace(s.Artist))
            .GroupBy(s => s.Artist!.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Name: g.Key, Key: Letters(g.Key), Songs: g.ToList()))
            .ToList();
        for (var i = 0; i < byArtist.Count; i++)
        {
            for (var j = i + 1; j < byArtist.Count; j++)
            {
                var (a, b) = (byArtist[i], byArtist[j]);
                var sameName = a.Key == b.Key;
                if (!sameName && !NearMiss(a.Key, b.Key))
                    continue;
                var (typo, right) = a.Songs.Count < b.Songs.Count || (a.Songs.Count == b.Songs.Count && string.CompareOrdinal(a.Name, b.Name) > 0) ? (a, b) : (b, a);
                var count = $"{right.Songs.Count} {(right.Songs.Count == 1 ? "song" : "songs")}";
                foreach (var song in typo.Songs)
                    yield return (song, sameName
                        ? $"Artist “{typo.Name}” is also written “{right.Name}” ({count}), so they're listed as two artists"
                        : $"Artist “{typo.Name}” is spelled differently from “{right.Name}” ({count}); check which is right");
            }
        }

        static string Letters(string artist) =>
            new([.. SongLibrary.ArtistSortKey(artist).ToLowerInvariant().Where(char.IsLetterOrDigit)]);
    }

    // One letter added, removed or changed, or two in a long name. Short names are close to
    // everything ("Eagles" is two letters from "Beatles", "Cream" one from "Dream"), so they only
    // count at one letter apart, and very short ones not at all.
    private static bool NearMiss(string a, string b)
    {
        var shorter = Math.Min(a.Length, b.Length);
        var allowed = shorter >= 10 ? 2 : shorter >= 6 ? 1 : 0;
        if (allowed == 0 || Math.Abs(a.Length - b.Length) > allowed)
            return false;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length] <= allowed;
    }

    // A repeat count at the end of a line, or alone on one: "x2", "2x", "(x2)". One or two
    // digits, so a fingering like x57765 isn't one; a count inside a longer note in
    // parentheses ("(w/ Intro Riff x4)") isn't the line's.
    [GeneratedRegex(@"(?:^|\s)\*?(?:\(\s*(?:[xX]\s?\d{1,2}|\d{1,2}\s?[xX])\s*\)|[xX]\s?\d{1,2}|\d{1,2}\s?[xX])\*?\s*$")]
    private static partial Regex RepeatCountRegex();

    // A chart that mentions playing in another tuning.
    [GeneratedRegex(@"\b(drop(ped)? d|half[- ]step down|whole[- ]step down|tuned (down|to)|dadgad|open [a-g] tuning|e♭ tuning|eb tuning|eb standard|d standard)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TuningMentionRegex();

    /// <summary>
    /// Where a song strays from ChordPro's own ways of writing things, which other ChordPro apps
    /// rely on: sections as plain heading lines ("Verse 1:") rather than section tags, a repeat
    /// count left in a line rather than a comment above it, fingerings written with
    /// <c>{chord}</c> (which draws a diagram where it stands) rather than <c>{define}</c>, and a
    /// tuning mentioned in the text but not given as <c>{meta: tuning …}</c>.
    /// </summary>
    public static IEnumerable<string> CheckChordProStyle(string text, Song song)
    {
        var headings = song.Sections.Where(s => s.Kind == SectionKind.None && s.Label is not null).Select(s => s.Label!).ToList();
        if (headings.Count > 0)
            yield return $"{Count(headings.Count, "section is a heading line", "sections are heading lines")} (“{headings[0]}:”); "
                + $"ChordPro marks a section with {{start_of_verse: label=\"{headings[0]}\"}} … {{end_of_verse}}";

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var preformatted = false;
        var chordLines = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var directive = line.StartsWith('{');
            if (directive && (line.StartsWith("{start_of_tab", StringComparison.OrdinalIgnoreCase) || line.StartsWith("{start_of_grid", StringComparison.OrdinalIgnoreCase) || line is "{sot}" or "{sog}"))
                preformatted = true;
            else if (directive && (line.StartsWith("{end_of_tab", StringComparison.OrdinalIgnoreCase) || line.StartsWith("{end_of_grid", StringComparison.OrdinalIgnoreCase) || line is "{eot}" or "{eog}"))
                preformatted = false;
            else if (line.StartsWith("{chord:", StringComparison.OrdinalIgnoreCase))
                chordLines++;
            else if (!directive && !preformatted && RepeatCountRegex().Match(line) is { Success: true } count)
                yield return $"Line {i + 1} ends in a repeat count (“{count.Value.Trim()}”); put {{comment: Repeat …}} on the line above instead";
        }
        if (chordLines > 0)
            yield return $"{Count(chordLines, "fingering is", "fingerings are")} written {{chord: …}}, which draws a diagram where it stands; "
                + "use {define: …} for the song's fingerings";

        if (song.Tuning is null)
        {
            var mention = song.Sections.SelectMany(s => s.Lines).Select(l => l switch
            {
                CommentLine c => c.Text,
                LyricLine l2 => string.Concat(l2.Segments.Select(s => s.Lyric)),
                _ => "",
            }).Select(t => TuningMentionRegex().Match(t)).FirstOrDefault(m => m.Success);
            if (mention is not null)
                yield return $"Mentions “{mention.Value}” but has no {{meta: tuning …}}";
        }
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    /// <summary>Lines of a chart that LivePrompter would cut off, with where the cut falls.</summary>
    public static IEnumerable<string> CheckLineWidths(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (LivePrompterWidth.CutOff(lines[i]) is { } shown)
            {
                // The last few words before the cut are enough to find the line.
                var tail = shown.Length > 30 ? "…" + shown[^30..].TrimStart() : shown;
                yield return $"Line {i + 1} is too wide for LivePrompter, which cuts it off after “{tail}”";
            }
        }
    }

    /// <summary>The problems in one song; <paramref name="path"/> is only used for its file name.</summary>
    public static IEnumerable<string> CheckSong(string path, Song song)
    {
        // A song sung a cappella has no chords, and so nothing for a key to describe.
        if (song.Key is null && song.ChordUsages().Any())
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
