using System.Text.RegularExpressions;

namespace ChordPro.Core.Chords;

/// <summary>
/// Reduces chords to easier forms that keep their character: extensions go (Cmaj9 → C, Am7 → Am,
/// E9 → E7), slash basses go (G/B → G), half-diminished becomes minor (F#m7b5 → F#m) and augmented
/// becomes major. Dominant sevenths, sus chords, diminished and power chords are kept, since their
/// common shapes are already easy.
/// </summary>
public static partial class ChordSimplifier
{
    [GeneratedRegex(@"^(?<root>[A-G][#b♯♭]?)")]
    private static partial Regex RootRegex();

    /// <summary>The simplified chord, or <paramref name="chord"/> unchanged if it isn't a recognizable chord.</summary>
    public static string Simplify(string chord)
    {
        if (ChordName.TryParse(chord) is not { } parsed)
            return chord;

        var root = RootRegex().Match(chord.Trim()).Value;
        var intervals = parsed.Intervals.Select(i => i % 12).ToHashSet();
        var hasMinorSeventh = intervals.Contains(10);

        string suffix;
        if (intervals.SetEquals([0, 7]))
            suffix = "5";
        else if (intervals.Contains(3) && intervals.Contains(6))
            suffix = hasMinorSeventh ? "m" : "dim";        // half-diminished → minor
        else if (intervals.Contains(3))
            suffix = "m";
        else if (intervals.Contains(4))
            suffix = hasMinorSeventh ? "7" : "";            // dominant keeps its 7th; maj7, 6, add9, aug → major
        else if (intervals.Contains(5))
            suffix = "sus4";
        else if (intervals.Contains(2))
            suffix = "sus2";
        else
            suffix = "5";

        return root + suffix;
    }
}
