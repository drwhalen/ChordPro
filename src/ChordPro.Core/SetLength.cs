namespace ChordPro.Core;

/// <summary>
/// How long a set runs: the songs' lengths (<c>{duration}</c>) added up, and how many songs have
/// none, so a total is never quietly short.
/// </summary>
public sealed record SetLength(int Songs, TimeSpan Known, int WithoutLength)
{
    public static SetLength Empty { get; } = new(0, TimeSpan.Zero, 0);

    /// <summary>The total of some songs' lengths (null for a song without one).</summary>
    public static SetLength Of(IEnumerable<TimeSpan?> lengths)
    {
        var list = lengths.ToList();
        return new(list.Count, TimeSpan.FromSeconds(list.Sum(l => l?.TotalSeconds ?? 0)), list.Count(l => l is null));
    }

    /// <summary>"11 songs · 42 min", with "(2 without a length)" when some songs don't say.</summary>
    public override string ToString()
    {
        var text = $"{Songs} {(Songs == 1 ? "song" : "songs")}";
        if (Known > TimeSpan.Zero)
            text += $" · {Minutes(Known)}";
        if (WithoutLength > 0 && Known > TimeSpan.Zero)
            text += $" ({WithoutLength} without a length)";
        return text;
    }

    /// <summary>A length the way a set list gives it: "42 min", "1 h 05 min".</summary>
    public static string Minutes(TimeSpan length)
    {
        var minutes = (int)Math.Round(length.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
    }
}
