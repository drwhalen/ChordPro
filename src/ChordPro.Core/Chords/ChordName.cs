using System.Text.RegularExpressions;

namespace ChordPro.Core.Chords;

/// <summary>A chord name broken down into its root, bass and the intervals it contains.</summary>
public sealed partial record ChordName(int Root, int Bass, IReadOnlyList<int> Intervals)
{
    [GeneratedRegex(@"^(?<root>[A-G][#b♯♭]?)(?<quality>[^/]*)(?:/(?<bass>[A-G][#b♯♭]?))?$")]
    private static partial Regex ChordRegex();

    private static readonly int[] LetterSemitones = [9, 11, 0, 2, 4, 5, 7]; // A B C D E F G

    /// <summary>Pitch classes (0 = C) of every note in the chord, root first.</summary>
    public IEnumerable<int> PitchClasses => Intervals.Select(i => (Root + i) % 12).Distinct();

    /// <summary>
    /// Parses names such as <c>C</c>, <c>F#m7b5</c>, <c>Bbmaj9</c>, <c>Dsus4</c> or <c>G/B</c>.
    /// Returns null for text that isn't a recognisable chord (e.g. "N.C.").
    /// </summary>
    public static ChordName? TryParse(string name)
    {
        var match = ChordRegex().Match(name.Trim());
        if (!match.Success)
            return null;

        var root = Note(match.Groups["root"].Value);
        var bass = match.Groups["bass"].Success ? Note(match.Groups["bass"].Value) : root;
        var intervals = ParseQuality(match.Groups["quality"].Value);
        return intervals is null ? null : new ChordName(root, bass, intervals);
    }

    public static int Note(string note)
    {
        var semitone = LetterSemitones[note[0] - 'A'];
        if (note.Length > 1)
            semitone += note[1] is 'b' or '♭' ? -1 : 1;
        return (semitone + 12) % 12;
    }

    // Intervals in semitones from the root. Recognises the usual triad prefixes, a 6/7/9/11/13
    // extension, and sus/add/altered-note modifiers; returns null if anything is left over.
    private static List<int>? ParseQuality(string quality)
    {
        var q = quality.Replace("(", "").Replace(")", "").Replace(",", "");
        int third = 4, fifth = 7;
        int? seventh = null;
        var extras = new List<int>();
        var majorSeventh = false;

        bool Take(string token)
        {
            if (!q.StartsWith(token, StringComparison.Ordinal))
                return false;
            q = q[token.Length..];
            return true;
        }

        // Triad
        if (Take("ø")) { third = 3; fifth = 6; seventh = 10; Take("7"); }
        else if (Take("dim") || Take("°") || Take("o")) { third = 3; fifth = 6; }
        else if (Take("aug") || Take("+")) fifth = 8;
        else if (Take("min") || Take("mi") || (q.StartsWith('m') && !q.StartsWith("maj", StringComparison.Ordinal) && Take("m")) || Take("-"))
            third = 3;
        else if (q == "5") return [0, 7];

        // Major-seventh marker (maj7, M7, Δ)
        if (Take("maj") || Take("ma") || Take("M") || Take("Δ"))
            majorSeventh = true;

        // Extension
        var isDim = third == 3 && fifth == 6 && seventh is null;
        var sevenInterval = majorSeventh ? 11 : isDim ? 9 : 10;
        if (Take("69") || Take("6/9")) { extras.Add(9); extras.Add(14); }
        else if (Take("13")) { seventh = sevenInterval; extras.Add(14); extras.Add(21); }
        else if (Take("11")) { seventh = sevenInterval; extras.Add(14); extras.Add(17); }
        else if (Take("9")) { seventh = sevenInterval; extras.Add(14); }
        else if (Take("7")) seventh = sevenInterval;
        else if (Take("6")) extras.Add(9);
        else if (majorSeventh) seventh = 11; // "Cmaj", "CΔ"

        // Modifiers
        while (q.Length > 0)
        {
            if (Take("sus2")) third = 2;
            else if (Take("sus4") || Take("sus")) third = 5;
            else if (Take("add9") || Take("add2")) extras.Add(14);
            else if (Take("add11") || Take("add4")) extras.Add(17);
            else if (Take("add#11") || Take("add+11")) extras.Add(18);
            else if (Take("add13") || Take("add6")) extras.Add(21);
            else if (Take("b5") || Take("-5")) fifth = 6;
            else if (Take("#5") || Take("+5")) fifth = 8;
            else if (Take("b9") || Take("-9")) { extras.Remove(14); extras.Add(13); }
            else if (Take("#9") || Take("+9")) { extras.Remove(14); extras.Add(15); }
            else if (Take("#11") || Take("+11")) { extras.Remove(17); extras.Add(18); }
            else if (Take("b13") || Take("-13")) { extras.Remove(21); extras.Add(20); }
            else if (Take("no3")) third = -1;
            else if (Take("2")) extras.Add(14);
            else if (Take("4")) extras.Add(17);
            else if (Take("7")) seventh ??= 10;
            else return null;
        }

        var intervals = new List<int> { 0 };
        if (third >= 0) intervals.Add(third);
        intervals.Add(fifth);
        if (seventh is { } s) intervals.Add(s);
        intervals.AddRange(extras);
        return intervals;
    }
}
