using System.Text.RegularExpressions;
using ChordPro.Core.Chords;

namespace ChordPro.Core;

/// <summary>What a new song (or a copy of one) is called and starts in.</summary>
/// <param name="Tempo">Beats per minute, if known.</param>
/// <param name="Capo">The capo fret; 0 for none.</param>
public sealed record NewSongDetails(string Title, string Artist, string Key, int? Tempo = null, int Capo = 0);

/// <summary>
/// Starting a song: its file name, and a ChordPro skeleton to write into (a verse and a chorus,
/// with the cursor in the verse), or a copy of a song under a new title, in a new key if asked.
/// </summary>
public static partial class NewSong
{
    [GeneratedRegex(@"\[([^\[\]]+)\]")]
    private static partial Regex InlineChordRegex();

    /// <summary>"Title - Artist.cho", as the library names songs, without characters files can't have.</summary>
    public static string FileName(string title, string artist)
    {
        var name = artist.Trim().Length > 0 ? $"{title.Trim()} - {artist.Trim()}" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars().Concat([':', '/', '\\']))
            name = name.Replace(c, '-');
        return name + ".cho";
    }

    /// <summary>A new song's text, and where the cursor starts: the empty first line of Verse 1.</summary>
    public static (string Text, int Caret) Template(NewSongDetails song)
    {
        var header = $"{{title: {song.Title.Trim()}}}\n";
        if (song.Artist.Trim().Length > 0)
            header += $"{{artist: {song.Artist.Trim()}}}\n";
        header += $"{{key: {song.Key}}}\n";
        if (song.Capo > 0)
            header += $"{{capo: {song.Capo}}}\n";
        if (song.Tempo is { } tempo)
            header += $"{{tempo: {tempo}}}\n";
        var verse = "\n{start_of_verse: label=\"Verse 1\"}\n";
        var text = header + verse + "\n{end_of_verse}\n\n{start_of_chorus}\n\n{end_of_chorus}\n";
        return (text, header.Length + verse.Length);
    }

    /// <summary>
    /// A copy of a song under new details. In a different key its chords (and any key changes) move
    /// to the new key, and its own fingerings are left out: a shape written for G isn't one for A.
    /// </summary>
    public static string Copy(string text, NewSongDetails song)
    {
        var lines = text.Split('\n').ToList();
        var oldKey = lines.Select(l => Directive(l, "key")).FirstOrDefault(v => v is not null);
        var shift = Shift(oldKey, song.Key);
        var flats = ChordTransposer.PrefersFlats(song.Key, 0);

        SetHeader(lines, "title", song.Title.Trim());
        SetHeader(lines, "artist", song.Artist.Trim().Length > 0 ? song.Artist.Trim() : null);
        SetHeader(lines, "key", song.Key);
        SetHeader(lines, "capo", song.Capo > 0 ? song.Capo.ToString() : null);
        SetHeader(lines, "tempo", song.Tempo?.ToString());

        if (shift == 0)
            return string.Join('\n', lines);
        // (found after the header changes above, which can move it)
        var headerKey = lines.FindIndex(l => Directive(l, "key") is not null);
        var moved = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, @"^\s*\{\s*(define(-[\w]+)?|chord)\s*:", RegexOptions.IgnoreCase))
                continue;
            if (Directive(line, "key") is { } key)
            {
                // the song's {key} was set above; later ones are key changes, which move too
                moved.Add(i == headerKey ? line : $"{{key: {ChordTransposer.Shift(key, shift)}}}");
                continue;
            }
            moved.Add(line.TrimStart().StartsWith('{') ? line
                : InlineChordRegex().Replace(line, m => $"[{ChordTransposer.Transpose(m.Groups[1].Value, shift, flats)}]"));
        }
        return string.Join('\n', moved);
    }

    // Semitones from one key to another, the shorter way round (−5 to +6); 0 if either isn't a key.
    private static int Shift(string? from, string to)
    {
        if (from is null || SongLibrary.ParseKey(from) is not var (a, _) || SongLibrary.ParseKey(to) is not var (b, _))
            return 0;
        var shift = ((b - a) % 12 + 12) % 12;
        return shift > 6 ? shift - 12 : shift;
    }

    private static string? Directive(string line, string name) =>
        Regex.Match(line, $@"^\s*\{{\s*{name}\s*:\s*(.*?)\s*\}}\s*$", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;

    // Sets {name: …} in the header (the directives the song starts with, before its first blank or
    // song line: a {key} further down is a key change) to value, adds it after the other header
    // tags when the song has none, or removes it when value is null.
    private static void SetHeader(List<string> lines, string name, string? value)
    {
        var end = lines.FindIndex(l => !l.TrimStart().StartsWith('{'));
        var header = lines.Take(end < 0 ? lines.Count : end).ToList();
        var at = header.FindIndex(l => Directive(l, name) is not null);
        if (at >= 0)
        {
            if (value is null)
                lines.RemoveAt(at);
            else
                lines[at] = $"{{{name}: {value}}}";
            return;
        }
        if (value is null)
            return;
        var last = header.FindLastIndex(l => Regex.IsMatch(l, @"^\s*\{\s*(title|artist|key|capo|tempo)\s*:", RegexOptions.IgnoreCase));
        lines.Insert(last + 1, $"{{{name}: {value}}}");
    }
}
