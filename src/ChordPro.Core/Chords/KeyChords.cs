namespace ChordPro.Core.Chords;

/// <summary>A chord's place in a key ("IV"), and the chord ("C").</summary>
public sealed record KeyChord(string Numeral, string Chord);

/// <summary>
/// The chords that belong to a key, for writing songs: the seven built on its scale, the same with
/// sevenths, and the chords songwriters most often borrow from outside it.
/// </summary>
public sealed record KeyChords(string Key, IReadOnlyList<KeyChord> Triads, IReadOnlyList<KeyChord> Sevenths, IReadOnlyList<KeyChord> Borrowed)
{
    // Steps from the root, and the chord built on each: major keys...
    private static readonly (int Step, string Numeral, string Quality, string Seventh, string SeventhNumeral)[] Major =
    [
        (0, "I", "", "maj7", "Imaj7"), (2, "ii", "m", "m7", "ii7"), (4, "iii", "m", "m7", "iii7"), (5, "IV", "", "maj7", "IVmaj7"),
        (7, "V", "", "7", "V7"), (9, "vi", "m", "m7", "vi7"), (11, "vii°", "dim", "m7b5", "viiø7"),
    ];

    // ...and natural minor ones, whose V is usually made major (from the harmonic minor).
    private static readonly (int Step, string Numeral, string Quality, string Seventh, string SeventhNumeral)[] Minor =
    [
        (0, "i", "m", "m7", "i7"), (2, "ii°", "dim", "m7b5", "iiø7"), (3, "III", "", "maj7", "IIImaj7"), (5, "iv", "m", "m7", "iv7"),
        (7, "v", "m", "7", "V7"), (8, "VI", "", "maj7", "VImaj7"), (10, "VII", "", "7", "VII7"),
    ];

    // Borrowed in a major key: the flat seventh (the "Mixolydian" chord), the minor four, the flat
    // six, and the major two and three that lead to V and vi (secondary dominants).
    private static readonly (int Step, string Numeral, string Quality)[] MajorBorrowed =
        [(10, "♭VII", ""), (5, "iv", "m"), (8, "♭VI", ""), (2, "II", ""), (4, "III", "")];

    // Borrowed in a minor key: the major V and IV, and the Neapolitan flat two.
    private static readonly (int Step, string Numeral, string Quality)[] MinorBorrowed =
        [(7, "V", ""), (5, "IV", ""), (1, "♭II", "")];

    /// <summary>The chords of a key such as "G", "Bb" or "F#m"; null when it isn't a key.</summary>
    public static KeyChords? For(string? key)
    {
        if (key is null || SongLibrary.ParseKey(key.Trim()) is not var (root, minor))
            return null;
        // Spell the chords the way the key is written: flats in Bb, sharps in E.
        var flats = ChordTransposer.PrefersFlats(key.Trim(), 0) ?? false;
        string Chord(int step, string quality) => ChordTransposer.NoteName(root + step, flats) + quality;

        var scale = minor ? Minor : Major;
        return new KeyChords(
            key.Trim(),
            [.. scale.Select(c => new KeyChord(c.Numeral, Chord(c.Step, c.Quality)))],
            [.. scale.Select(c => new KeyChord(c.SeventhNumeral, Chord(c.Step, c.Seventh)))],
            // a chord a step below the scale ("♭VI") is spelled with a flat, whatever the key
            [.. (minor ? MinorBorrowed : MajorBorrowed).Select(c => new KeyChord(c.Numeral,
                c.Numeral.StartsWith('♭') ? ChordTransposer.NoteName(root + c.Step, preferFlats: true) + c.Quality : Chord(c.Step, c.Quality)))]);
    }

    /// <summary>The keys a song can change to, majors then minors, spelled the usual way.</summary>
    public static IReadOnlyList<string> AllKeys { get; } =
        ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B",
         "Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "Bbm", "Bm"];
}
