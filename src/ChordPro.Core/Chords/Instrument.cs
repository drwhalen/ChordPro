namespace ChordPro.Core.Chords;

/// <summary>A fretted instrument: its open-string tuning (MIDI note numbers, lowest string first).</summary>
public sealed record Instrument(string Name, IReadOnlyList<int> Tuning, bool BassMustBeRoot)
{
    public static Instrument Guitar { get; } = new("Guitar", [40, 45, 50, 55, 59, 64], BassMustBeRoot: true); // E A D G B E
    public static Instrument Ukulele { get; } = new("Ukulele", [67, 60, 64, 69], BassMustBeRoot: false);     // G C E A (re-entrant)

    // A mountain dulcimer's frets (and the Seagull Merlin's) are diatonic: a major scale with a flat
    // seventh from the open string, plus the extra 6½ fret ("6+") that gives the major seventh.
    private static readonly int[] DiatonicFrets = [0, 2, 4, 5, 7, 9, 10, 11, 12, 14, 16, 17, 19, 21];
    private static readonly string[] DiatonicFretNames = ["0", "1", "2", "3", "4", "5", "6", "6+", "7", "8", "9", "10", "11", "12"];

    private static Instrument Diatonic(string name, int[] tuning, bool pairedMelody) => new(name, tuning, BassMustBeRoot: false)
    {
        FretSemitones = DiatonicFrets,
        FretNames = DiatonicFretNames,
        DoubledStrings = pairedMelody ? [tuning.Length - 1] : [],
    };

    /// <summary>
    /// A dulcimer, or the Seagull Merlin, tuned D-A-D-D: a bass D, an A, and a pair of melody strings
    /// tuned to the same D and fretted together, so it plays as three courses.
    /// </summary>
    public static Instrument DulcimerDAdd { get; } = Diatonic("Dulcimer / Merlin (D-A-DD)", [50, 57, 62], pairedMelody: true); // D3 A3 D4 D4

    /// <summary>A dulcimer tuned D-A-A (Ionian: the melody string starts on the scale's fifth).</summary>
    public static Instrument DulcimerDAA { get; } = Diatonic("Dulcimer (D-A-A)", [50, 57, 57], pairedMelody: false); // D3 A3 A3

    /// <summary>A three-string dulcimer tuned D-A-D (as D-A-DD with a single melody string).</summary>
    public static Instrument DulcimerDAD { get; } = Diatonic("Dulcimer (D-A-D)", [50, 57, 62], pairedMelody: false); // D3 A3 D4

    /// <summary>The Seagull Merlin in G, tuned G-D-G-G: D-A-DD a fourth higher, so the same shapes play a fourth up.</summary>
    public static Instrument MerlinGDgg { get; } = Diatonic("Merlin (G-D-GG)", [55, 62, 67], pairedMelody: true); // G3 D4 G4 G4

    public static IReadOnlyList<Instrument> All { get; } = [Guitar, Ukulele, DulcimerDAdd, DulcimerDAA, DulcimerDAD, MerlinGDgg];

    /// <summary>The instrument with this name (as saved in settings), or the guitar.</summary>
    public static Instrument Find(string? name) => name switch
    {
        "Seagull Merlin" => DulcimerDAdd, // its name before the dulcimer tunings were added
        _ => All.FirstOrDefault(i => i.Name == name) ?? Guitar,
    };

    // A chromatic neck has a fret for every semitone; the shape finder doesn't search past this.
    private const int ChromaticFrets = 15;

    public int StringCount => Tuning.Count;

    /// <summary>
    /// For a diatonic neck, the semitones above the open string at each fret (from fret 0); null
    /// when there's a fret for every semitone. Shapes on a diatonic neck count frets as they're
    /// laid out, so on the Merlin fret 7 is the 6+ fret.
    /// </summary>
    public IReadOnlyList<int>? FretSemitones { get; init; }

    /// <summary>What players call each fret, where that differs from its number ("6+").</summary>
    public IReadOnlyList<string>? FretNames { get; init; }

    /// <summary>Strings (by index) that are a pair played as one, drawn doubled in diagrams.</summary>
    public IReadOnlyList<int> DoubledStrings { get; init; } = [];

    public bool IsDiatonic => FretSemitones is not null;

    /// <summary>The highest fret.</summary>
    public int HighestFret => FretSemitones is { } frets ? frets.Count - 1 : ChromaticFrets;

    /// <summary>How many semitones a fret raises a string.</summary>
    public int Semitones(int fret) => FretSemitones is { } frets ? frets[fret] : fret;

    /// <summary>The fret as players name it: its number, or "6+" for the Merlin's 6½ fret.</summary>
    public string FretName(int fret) => FretNames is { } names && fret >= 0 && fret < names.Count ? names[fret] : fret.ToString();

    /// <summary>
    /// A shape written out: compact for guitar and ukulele (<c>x32010</c>), and as dulcimer tab,
    /// bass string first, for a diatonic neck (<c>0-1-3</c>, <c>2-1-6+</c>).
    /// </summary>
    public string Notation(ChordShape shape) => IsDiatonic
        ? string.Join("-", shape.Frets.Select(f => f < 0 ? "x" : FretName(f)))
        : shape.ToString();

    public override string ToString() => Name;
}
