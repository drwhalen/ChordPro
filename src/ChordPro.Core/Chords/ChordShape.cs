namespace ChordPro.Core.Chords;

/// <summary>
/// A fingering for a fretted instrument. <see cref="Frets"/> lists absolute fret numbers from the
/// lowest-pitched string, with 0 for an open string and -1 for a muted one.
/// </summary>
public sealed record ChordShape(IReadOnlyList<int> Frets)
{
    /// <summary>The lowest fretted position (1 when every fretted note fits in the first frets).</summary>
    public int BaseFret
    {
        get
        {
            var fretted = Frets.Where(f => f > 0).ToList();
            return fretted.Count == 0 || fretted.Max() <= 4 ? 1 : fretted.Min();
        }
    }

    public bool Equals(ChordShape? other) => other is not null && Frets.SequenceEqual(other.Frets);

    public override int GetHashCode() => Frets.Aggregate(17, (h, f) => h * 31 + f);

    /// <summary>Compact notation such as <c>x32010</c> (frets above 9 are written in parentheses).</summary>
    public override string ToString() =>
        string.Concat(Frets.Select(f => f switch { < 0 => "x", < 10 => f.ToString(), _ => $"({f})" }));

    /// <summary>Parses compact notation (<c>x32010</c>) or a space-separated list (<c>x 3 2 0 1 0</c>).</summary>
    public static ChordShape Parse(string text)
    {
        var tokens = text.Contains(' ')
            ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : text.Select(c => c.ToString()).ToArray();
        return new ChordShape(tokens.Select(t => t is "x" or "X" or "-" or "N" ? -1 : int.Parse(t)).ToArray());
    }
}
