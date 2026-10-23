using System.Text.RegularExpressions;

namespace ChordPro.Core.Chords;

/// <summary>
/// Writes a chosen fingering into a song's text, so it comes back next time and in other ChordPro
/// apps: <c>{define: C base-fret 1 frets x 3 2 0 1 0}</c> for the standard guitar, or tagged for
/// another instrument (<c>{define-ukulele: …}</c>), which other instruments then leave alone.
/// </summary>
public static partial class SongFingerings
{
    [GeneratedRegex(@"^\s*\{\s*(?<name>define(?:-[\w]+)?|chord)\s*:\s*(?<chord>[^\s}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DefinitionRegex();

    [GeneratedRegex(@"^\s*\{\s*(title|t|subtitle|st|artist|composer|lyricist|album|year|copyright|key|capo|tempo|time|duration|book|meta|x_tuning|tuning)\s*[:}]", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderRegex();

    /// <summary>The directive a fingering for <paramref name="instrument"/> is written with.</summary>
    public static string Directive(Instrument instrument) =>
        instrument.Selector is { } selector ? $"define-{selector}" : "define";

    /// <summary>
    /// The text with <paramref name="shape"/> as the song's fingering for <paramref name="chord"/>
    /// on <paramref name="instrument"/>: replacing one already there, or added after the song's
    /// other fingerings (or its header).
    /// </summary>
    public static string Set(string text, string chord, ChordShape shape, Instrument instrument)
    {
        var directive = Directive(instrument);
        var frets = string.Join(" ", shape.Frets.Select(f => f < 0 ? "x" : f.ToString()));
        var line = $"{{{directive}: {chord} base-fret 1 frets {frets}}}";
        var lines = text.Split('\n').ToList();

        var existing = lines.FindIndex(l => Matches(l, directive, chord));
        if (existing >= 0)
        {
            lines[existing] = line;
            return string.Join('\n', lines);
        }
        var lastDefinition = lines.FindLastIndex(l => DefinitionRegex().IsMatch(l));
        if (lastDefinition >= 0)
        {
            lines.Insert(lastDefinition + 1, line);
            return string.Join('\n', lines);
        }
        // After the header, as a block of its own.
        var header = lines.FindLastIndex(l => HeaderRegex().IsMatch(l));
        var at = header + 1;
        lines.InsertRange(at, at < lines.Count && lines[at].Trim().Length == 0 ? ["", line] : ["", line, ""]);
        return string.Join('\n', lines);
    }

    /// <summary>The text without the song's fingering for <paramref name="chord"/> on <paramref name="instrument"/>.</summary>
    public static string Remove(string text, string chord, Instrument instrument)
    {
        var directive = Directive(instrument);
        return string.Join('\n', text.Split('\n').Where(l => !Matches(l, directive, chord)));
    }

    // {chord: …} is an older way to write a definition, so the plain guitar one matches it too.
    private static bool Matches(string line, string directive, string chord) =>
        DefinitionRegex().Match(line) is { Success: true } m &&
        m.Groups["chord"].Value == chord &&
        (m.Groups["name"].Value.Equals(directive, StringComparison.OrdinalIgnoreCase) ||
         (directive == "define" && m.Groups["name"].Value.Equals("chord", StringComparison.OrdinalIgnoreCase)));
}
