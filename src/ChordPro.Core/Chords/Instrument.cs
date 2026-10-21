namespace ChordPro.Core.Chords;

/// <summary>A fretted instrument: its open-string tuning (MIDI note numbers, lowest string first).</summary>
public sealed record Instrument(string Name, IReadOnlyList<int> Tuning, bool BassMustBeRoot)
{
    public static Instrument Guitar { get; } = new("Guitar", [40, 45, 50, 55, 59, 64], BassMustBeRoot: true); // E A D G B E
    public static Instrument Ukulele { get; } = new("Ukulele", [67, 60, 64, 69], BassMustBeRoot: false);     // G C E A (re-entrant)

    public static IReadOnlyList<Instrument> All { get; } = [Guitar, Ukulele];

    public int StringCount => Tuning.Count;

    public override string ToString() => Name;
}
