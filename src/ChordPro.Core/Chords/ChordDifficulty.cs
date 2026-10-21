namespace ChordPro.Core.Chords;

/// <summary>
/// How hard a fingering is to play, as a rough score: open shapes with few fingers are easiest;
/// barres, wide stretches and higher positions add difficulty.
/// </summary>
public static class ChordDifficulty
{
    /// <summary>Score for a chord with no playable shape at all.</summary>
    public const double Unplayable = 10;

    /// <summary>Shapes at or below this score are treated as easy (open-position, no barre).</summary>
    public const double EasyThreshold = 2.5;

    public static double Of(ChordShape shape)
    {
        var fretted = shape.Frets.Where(f => f > 0).ToList();
        if (fretted.Count == 0)
            return 0;

        var lowest = fretted.Min();
        var span = fretted.Max() - lowest;
        var score = fretted.Count * 0.5;
        if (NeedsBarre(shape))
            score += 3;
        if (span > 2)
            score += span - 2;
        if (lowest > 3)
            score += 0.5;
        return score;
    }

    /// <summary>True when the shape can only be held with one finger across several strings.</summary>
    public static bool NeedsBarre(ChordShape shape)
    {
        var fretted = shape.Frets.Where(f => f > 0).ToList();
        if (fretted.Count <= 4)
            return false;
        var lowest = fretted.Min();
        return fretted.Count(f => f == lowest) >= 2;
    }

    /// <summary>The difficulty of the best fingering for <paramref name="chord"/>.</summary>
    public static double Of(string chord, Instrument instrument) =>
        ChordShapeFinder.Find(chord, instrument) is { } shape ? Of(shape) : Unplayable;
}
