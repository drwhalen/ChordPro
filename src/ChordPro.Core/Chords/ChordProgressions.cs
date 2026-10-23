using System.Text;
using System.Text.RegularExpressions;

namespace ChordPro.Core.Chords;

/// <summary>
/// Chords in relation to their key: the chords that usually come next (for the key companion) and a
/// chord as a Nashville number ("5⁷" for G7 in C), so a song can be played in any key.
/// </summary>
public static partial class ChordProgressions
{
    // What usually follows each chord of a key, most common first: the moves of most pop, rock,
    // folk and hymn writing (I–IV–V, I–V–vi–IV, ii–V–I, and the borrowed ♭VII and iv).
    private static readonly Dictionary<string, string[]> MajorNext = new()
    {
        ["I"] = ["IV", "V", "vi", "ii"], ["ii"] = ["V", "IV", "vii°"], ["iii"] = ["vi", "IV", "ii"],
        ["IV"] = ["I", "V", "ii", "vi"], ["V"] = ["I", "vi", "IV"], ["vi"] = ["IV", "ii", "V", "iii"],
        ["vii°"] = ["I", "iii"], ["♭VII"] = ["I", "IV"], ["iv"] = ["I"], ["♭VI"] = ["♭VII", "V"],
        ["II"] = ["V", "IV"], ["III"] = ["vi", "IV"],
    };

    private static readonly Dictionary<string, string[]> MinorNext = new()
    {
        ["i"] = ["iv", "VI", "VII", "V"], ["ii°"] = ["V", "v"], ["III"] = ["VI", "VII", "iv"],
        ["iv"] = ["i", "V", "VII"], ["v"] = ["i", "VI"], ["V"] = ["i", "VI"], ["VI"] = ["VII", "III", "iv", "i"],
        ["VII"] = ["III", "i", "VI"], ["IV"] = ["i"], ["♭II"] = ["V", "i"],
    };

    private static readonly string[] Degrees = ["1", "♭2", "2", "♭3", "3", "4", "♭5", "5", "♭6", "6", "♭7", "7"];

    [GeneratedRegex(@"^(?<root>[A-G][#b]?)(?<quality>[^/]*)(?:/(?<bass>[A-G][#b]?))?$")]
    private static partial Regex ChordPartsRegex();

    /// <summary>
    /// The chords that usually follow <paramref name="chord"/> in <paramref name="key"/>, most
    /// common first; empty when the chord isn't one of the key's (or a commonly borrowed one).
    /// </summary>
    public static IReadOnlyList<KeyChord> Next(string? key, string chord)
    {
        if (KeyChords.For(key) is not { } chords || Numeral(chords, chord) is not { } numeral)
            return [];
        var minor = key!.Trim().EndsWith('m');
        var table = minor ? MinorNext : MajorNext;
        if (!table.TryGetValue(numeral, out var next))
            return [];
        var all = chords.Triads.Concat(chords.Borrowed).ToList();
        return [.. next.Select(n => all.FirstOrDefault(c => c.Numeral == n)).OfType<KeyChord>()];
    }

    // The chord's numeral in the key, by its root and whether it's major, minor or diminished
    // (a seventh or sus is taken as the chord it's built on).
    private static string? Numeral(KeyChords chords, string chord)
    {
        if (ChordName.TryParse(chord) is not { } parsed)
            return null;
        var kind = Kind(parsed);
        foreach (var candidate in chords.Triads.Concat(chords.Borrowed))
        {
            if (ChordName.TryParse(candidate.Chord) is { } c && c.Root == parsed.Root && Kind(c) == kind)
                return candidate.Numeral;
        }
        return null;
    }

    private static string Kind(ChordName chord) =>
        chord.Intervals.Contains(3) && chord.Intervals.Contains(6) && !chord.Intervals.Contains(7) ? "dim"
        : chord.Intervals.Contains(3) && !chord.Intervals.Contains(4) ? "minor"
        : "major";

    /// <summary>
    /// A chord as a Nashville number in <paramref name="key"/>: "5⁷" for G7 in C, "6m" for Am,
    /// "1/3" for C/E, "♭7" for Bb. A minor key counts from its own tonic. Null when either isn't
    /// a chord or key.
    /// </summary>
    public static string? Number(string chord, string? key)
    {
        if (key is null || SongLibrary.ParseKey(key.Trim()) is not var (root, _))
            return null;
        if (ChordPartsRegex().Match(chord.Trim()) is not { Success: true } m || ChordName.TryParse(chord) is null)
            return null;
        var number = Degree(PitchClass(m.Groups["root"].Value), root) + Superscript(m.Groups["quality"].Value);
        if (m.Groups["bass"].Success)
            number += "/" + Degree(PitchClass(m.Groups["bass"].Value), root);
        return number;
    }

    private static string Degree(int pitchClass, int root) => Degrees[((pitchClass - root) % 12 + 12) % 12];

    private static int PitchClass(string note) => ChordName.TryParse(note)!.Root;

    // Extensions are written raised in Nashville charts: 5⁷, 1maj⁷, 2sus⁴.
    private static string Superscript(string quality)
    {
        var text = new StringBuilder();
        foreach (var c in quality)
            text.Append(c switch
            {
                '0' => '⁰', '1' => '¹', '2' => '²', '3' => '³', '4' => '⁴', '5' => '⁵', '6' => '⁶', '7' => '⁷', '8' => '⁸', '9' => '⁹',
                _ => c,
            });
        return text.ToString();
    }
}
