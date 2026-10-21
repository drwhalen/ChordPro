using System.Text.RegularExpressions;

namespace ChordPro.Core;

/// <summary>Transposes chord names (e.g. <c>C#m7</c>, <c>Bb/D</c>) by a number of semitones.</summary>
public static partial class ChordTransposer
{
    private static readonly string[] SharpNotes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] FlatNotes = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
    private static readonly int[] LetterSemitones = [9, 11, 0, 2, 4, 5, 7]; // A B C D E F G

    // Keys conventionally written with flats. (F#/Gb could go either way; F# is more usual on guitar.)
    private static readonly HashSet<string> FlatKeys =
        ["F", "Bb", "Eb", "Ab", "Db", "Dm", "Gm", "Cm", "Fm", "Bbm", "Ebm"];

    // Root note, the rest of the chord, and an optional slash bass note.
    [GeneratedRegex(@"^(?<root>[A-G][#b♯♭]?)(?<quality>[^/]*)(?:/(?<bass>[A-G][#b♯♭]?)(?<bassRest>.*))?$")]
    private static partial Regex ChordRegex();

    /// <summary>
    /// Transposes <paramref name="chord"/> by <paramref name="semitones"/>. Text that is not a chord
    /// (e.g. "N.C.") is returned unchanged. When <paramref name="preferFlats"/> is null, the chord's
    /// own spelling decides between sharps and flats.
    /// </summary>
    public static string Transpose(string chord, int semitones, bool? preferFlats = null)
    {
        var match = ChordRegex().Match(chord);
        if (!match.Success || Mod12(semitones) == 0)
            return chord;

        var root = match.Groups["root"].Value;
        var flats = preferFlats ?? IsFlat(root);
        var result = TransposeNote(root, semitones, flats) + match.Groups["quality"].Value;

        if (match.Groups["bass"].Success)
            result += "/" + TransposeNote(match.Groups["bass"].Value, semitones, flats) + match.Groups["bassRest"].Value;

        return result;
    }

    /// <summary>
    /// Whether chords should be spelled with flats after transposing a song in <paramref name="key"/>,
    /// or null if the key is missing or unrecognised.
    /// </summary>
    public static bool? PrefersFlats(string? key, int semitones)
    {
        if (key is null || !ChordRegex().IsMatch(key.Trim()))
            return null;

        var transposed = Transpose(key.Trim(), semitones);
        var match = ChordRegex().Match(transposed);
        var quality = match.Groups["quality"].Value;
        var minor = quality.StartsWith('m') && !quality.StartsWith("maj", StringComparison.Ordinal);
        var root = TransposeNote(match.Groups["root"].Value, 0, flats: true);

        return FlatKeys.Contains(minor ? root + "m" : root);
    }

    private static string TransposeNote(string note, int semitones, bool flats)
    {
        var semitone = LetterSemitones[note[0] - 'A'];
        if (note.Length > 1)
            semitone += IsFlat(note) ? -1 : 1;

        return (flats ? FlatNotes : SharpNotes)[Mod12(semitone + semitones)];
    }

    private static bool IsFlat(string note) => note.Length > 1 && note[1] is 'b' or '♭';

    private static int Mod12(int value) => ((value % 12) + 12) % 12;
}
