namespace ChordPro.Core.Chords;

/// <summary>
/// The conventional fingerings for common chords, used in preference to <see cref="ChordShapeFinder"/>'s
/// search where players expect a particular shape (mostly barre chords).
/// </summary>
internal static class StandardShapes
{
    private static readonly Dictionary<string, Dictionary<(int Root, string Intervals), ChordShape>> ByInstrument = new()
    {
        [Instrument.Guitar.Name] = Build(
            "C x32010", "C# x46664", "D xx0232", "Eb xx1343", "E 022100", "F 133211",
            "F# 244322", "G 320003", "Ab 466544", "A x02220", "Bb x13331", "B x24442",
            "Cm x35543", "C#m x46654", "Dm xx0231", "Ebm xx1342", "Em 022000", "Fm 133111",
            "F#m 244222", "Gm 355333", "Abm 466444", "Am x02210", "Bbm x13321", "Bm x24432",
            "C7 x32310", "C#7 x46464", "D7 xx0212", "Eb7 xx1323", "E7 020100", "F7 131211",
            "F#7 242322", "G7 320001", "Ab7 464544", "A7 x02020", "Bb7 x13131", "B7 x21202",
            "Cm7 x35343", "C#m7 x46454", "Dm7 xx0211", "Ebm7 xx1322", "Em7 020000", "Fm7 131111",
            "F#m7 242222", "Gm7 353333", "Abm7 464444", "Am7 x02010", "Bbm7 x13121", "Bm7 x20202",
            "Cmaj7 x32000", "C#maj7 x46564", "Dmaj7 xx0222", "Ebmaj7 xx1333", "Emaj7 021100", "Fmaj7 xx3210",
            "F#maj7 243322", "Gmaj7 320002", "Abmaj7 465544", "Amaj7 x02120", "Bbmaj7 x13231", "Bmaj7 x24342"),
        [Instrument.Ukulele.Name] = Build("Em 0432"),
    };

    public static ChordShape? Find(ChordName chord, Instrument instrument) =>
        chord.Bass == chord.Root &&
        ByInstrument.TryGetValue(instrument.Name, out var shapes) &&
        shapes.TryGetValue(Key(chord), out var shape)
            ? shape
            : null;

    private static Dictionary<(int, string), ChordShape> Build(params string[] entries) =>
        entries.Select(e => e.Split(' '))
            .ToDictionary(p => Key(ChordName.TryParse(p[0])!), p => ChordShape.Parse(p[1]));

    // Keyed by notes rather than spelling, so "Bbm7", "A#min7" and "Bb-7" share an entry.
    private static (int, string) Key(ChordName chord) =>
        (chord.Root, string.Join(",", chord.Intervals.Select(i => i % 12).Distinct().Order()));
}
