using System.Text.RegularExpressions;
using ChordPro.Core.Chords;

namespace ChordPro.Core;

/// <summary>
/// Parses text in ChordPro format (https://www.chordpro.org/chordpro/) into a <see cref="Song"/>.
/// Supports metadata, comments, chorus/verse/bridge/tab/grid sections and the {chorus} recall
/// directive. Unrecognised directives (fonts, page layout, chord definitions…) are ignored.
/// </summary>
public static partial class ChordProParser
{
    // {name}, {name: value} or {name value}
    [GeneratedRegex(@"^\{\s*(?<name>[A-Za-z_][\w-]*)\s*(?:[:\s]\s*(?<value>.*?))?\s*\}\s*$")]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex(@"\[(?<chord>[^\]]*)\]")]
    private static partial Regex ChordRegex();

    [GeneratedRegex(@"^label\s*=\s*""(?<label>[^""]*)""")]
    private static partial Regex LabelAttributeRegex();

    private static readonly Dictionary<string, string> MetaAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["t"] = "title", ["title"] = "title",
        ["st"] = "subtitle", ["su"] = "subtitle", ["subtitle"] = "subtitle",
        ["artist"] = "artist", ["composer"] = "composer", ["lyricist"] = "lyricist",
        ["album"] = "album", ["year"] = "year", ["copyright"] = "copyright",
        ["key"] = "key", ["capo"] = "capo", ["tempo"] = "tempo",
        ["time"] = "time", ["duration"] = "duration",
    };

    private static readonly Dictionary<string, SectionKind> SectionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c"] = SectionKind.Chorus, ["chorus"] = SectionKind.Chorus,
        ["v"] = SectionKind.Verse, ["verse"] = SectionKind.Verse,
        ["b"] = SectionKind.Bridge, ["bridge"] = SectionKind.Bridge,
        ["t"] = SectionKind.Tab, ["tab"] = SectionKind.Tab,
        ["g"] = SectionKind.Grid, ["grid"] = SectionKind.Grid,
    };

    public static Song ParseFile(string path) => Parse(File.ReadAllText(path));

    public static Song Parse(string text)
    {
        var song = new Song();
        Section current = new(SectionKind.None);
        Section? lastChorus = null;
        var inSection = false;

        void StartSection(Section section)
        {
            if (current.Lines.Count > 0)
                song.Sections.Add(current);
            current = section;
        }

        void EndSection()
        {
            if (current.Kind == SectionKind.Chorus)
                lastChorus = current;
            StartSection(new Section(SectionKind.None));
            inSection = false;
        }

        foreach (var rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var preformatted = inSection && current.Kind is SectionKind.Tab or SectionKind.Grid;

            if (line.TrimStart().StartsWith('#'))
                continue; // ChordPro comment, never displayed

            var directive = DirectiveRegex().Match(line.Trim());
            if (!directive.Success)
            {
                if (line.Length == 0)
                    current.Lines.Add(new BlankLine());
                else if (preformatted)
                    current.Lines.Add(new PreformattedLine(line));
                else
                    current.Lines.Add(ParseLyricLine(line));
                continue;
            }

            var name = directive.Groups["name"].Value.ToLowerInvariant();
            var value = directive.Groups["value"].Value;

            if (TryParseSectionDirective(name, out var kind, out var isStart))
            {
                if (isStart)
                {
                    if (inSection)
                        EndSection();
                    StartSection(new Section(kind, ParseLabel(value)));
                    inSection = true;
                }
                else if (inSection)
                {
                    EndSection();
                }
                continue;
            }

            switch (name)
            {
                case "meta":
                    var parts = value.Split((char[])[' ', '\t'], 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2)
                        song.AddMeta(MetaAliases.GetValueOrDefault(parts[0], parts[0].ToLowerInvariant()), parts[1]);
                    break;

                case "c" or "comment" or "highlight":
                    current.Lines.Add(new CommentLine(value, CommentStyle.Normal));
                    break;
                case "ci" or "comment_italic":
                    current.Lines.Add(new CommentLine(value, CommentStyle.Italic));
                    break;
                case "cb" or "comment_box":
                    current.Lines.Add(new CommentLine(value, CommentStyle.Box));
                    break;

                case "chorus":
                    // Recall the most recent chorus. Not allowed inside another section.
                    if (inSection)
                        break;
                    if (lastChorus is null)
                    {
                        current.Lines.Add(new CommentLine(value.Length > 0 ? value : "Chorus", CommentStyle.Normal));
                        break;
                    }
                    var recalled = new Section(SectionKind.Chorus, value.Length > 0 ? value : lastChorus.Label);
                    recalled.Lines.AddRange(lastChorus.Lines);
                    StartSection(recalled);
                    StartSection(new Section(SectionKind.None));
                    break;

                case "define" or "chord":
                    if (ParseDefinition(value) is var (chordName, shape))
                        song.ChordDefinitions[chordName] = shape;
                    break;

                default:
                    if (MetaAliases.TryGetValue(name, out var metaName) && value.Length > 0)
                        song.AddMeta(metaName, value);
                    break;
            }
        }

        if (inSection && current.Kind == SectionKind.Chorus)
            lastChorus = current;
        if (current.Lines.Count > 0)
            song.Sections.Add(current);

        TrimBlankLines(song);
        return song;
    }

    public static LyricLine ParseLyricLine(string line)
    {
        var segments = new List<ChordSegment>();
        var position = 0;
        string? chord = null;

        foreach (Match match in ChordRegex().Matches(line))
        {
            var lyric = line[position..match.Index];
            if (chord is not null || lyric.Length > 0)
                segments.Add(new ChordSegment(chord, lyric));
            chord = match.Groups["chord"].Value;
            position = match.Index + match.Length;
        }

        var rest = line[position..];
        if (chord is not null || rest.Length > 0)
            segments.Add(new ChordSegment(chord, rest));

        return new LyricLine(segments);
    }

    /// <summary>
    /// Parses a chord definition such as <c>Am base-fret 1 frets x 0 2 2 1 0 fingers 0 0 2 3 1 0</c>.
    /// Fret numbers are relative to the base fret, as in the ChordPro spec.
    /// </summary>
    public static (string Name, ChordShape Shape)? ParseDefinition(string value)
    {
        var tokens = value.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
            return null;

        var baseFret = 1;
        List<int>? frets = null;
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] == "base-fret" && i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out var b))
            {
                baseFret = Math.Max(1, b);
                i++;
            }
            else if (tokens[i] == "frets")
            {
                frets = [];
                while (i + 1 < tokens.Length && TryParseFret(tokens[i + 1], out var fret))
                {
                    frets.Add(fret);
                    i++;
                }
            }
        }

        if (frets is not { Count: > 0 })
            return null;

        var absolute = frets.Select(f => f > 0 ? f + baseFret - 1 : f).ToArray();
        return (tokens[0], new ChordShape(absolute));

        static bool TryParseFret(string token, out int fret)
        {
            fret = -1;
            return token is "x" or "X" or "N" or "-1" || int.TryParse(token, out fret);
        }
    }

    private static bool TryParseSectionDirective(string name, out SectionKind kind, out bool isStart)
    {
        kind = SectionKind.None;
        isStart = false;

        string suffix;
        if (name.StartsWith("start_of_", StringComparison.Ordinal)) { isStart = true; suffix = name["start_of_".Length..]; }
        else if (name.StartsWith("end_of_", StringComparison.Ordinal)) { suffix = name["end_of_".Length..]; }
        else if (name.Length == 3 && name.StartsWith("so", StringComparison.Ordinal)) { isStart = true; suffix = name[2..]; }
        else if (name.Length == 3 && name.StartsWith("eo", StringComparison.Ordinal)) { suffix = name[2..]; }
        else return false;

        if (SectionAliases.TryGetValue(suffix, out kind))
            return true;

        // Only long-form start_of_xxx / end_of_xxx define custom sections.
        if (suffix.Length > 1)
        {
            kind = SectionKind.Other;
            return true;
        }
        return false;
    }

    private static string? ParseLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var attr = LabelAttributeRegex().Match(value);
        return attr.Success ? attr.Groups["label"].Value : value;
    }

    private static void TrimBlankLines(Song song)
    {
        foreach (var section in song.Sections)
        {
            while (section.Lines.Count > 0 && section.Lines[0] is BlankLine)
                section.Lines.RemoveAt(0);
            while (section.Lines.Count > 0 && section.Lines[^1] is BlankLine)
                section.Lines.RemoveAt(section.Lines.Count - 1);
        }
        song.Sections.RemoveAll(s => s.Lines.Count == 0);
    }
}
