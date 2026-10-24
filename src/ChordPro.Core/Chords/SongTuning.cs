namespace ChordPro.Core.Chords;

/// <summary>
/// A tuning a song asks for: an instrument tuning (Drop D, DADGAD, D-A-A), how far the whole
/// instrument is tuned down (a half step down, as many rock songs are), or both.
/// </summary>
/// <param name="Instrument">The tuning's instrument, or null when only the pitch changes.</param>
/// <param name="Offset">Semitones the strings are tuned from their usual pitch (−1 for a half step down).</param>
public sealed record SongTuning(Instrument? Instrument, int Offset)
{
    // Ways people write tunings in charts, most specific first ("drop d" before "d standard").
    private static readonly (string Text, Instrument Instrument)[] InstrumentNames =
    [
        ("drop d", Instrument.GuitarDropD), ("dropped d", Instrument.GuitarDropD),
        ("open g", Instrument.GuitarOpenG), ("dgdgbd", Instrument.GuitarOpenG),
        ("dadgad", Instrument.GuitarDadgad),
        ("low g", Instrument.UkuleleLowG), ("baritone", Instrument.BaritoneUkulele),
        ("mandolin", Instrument.Mandolin), ("gdae", Instrument.Mandolin),
        ("g-d-gg", Instrument.MerlinGDgg), ("gdgg", Instrument.MerlinGDgg),
        ("d-a-dd", Instrument.DulcimerDAdd), ("dadd", Instrument.DulcimerDAdd), ("merlin", Instrument.DulcimerDAdd),
        ("d-a-a", Instrument.DulcimerDAA), ("daa", Instrument.DulcimerDAA),
        ("d-g-d", Instrument.DulcimerDGD), ("dgd", Instrument.DulcimerDGD),
        ("d-a-d", Instrument.DulcimerDAD),
        ("ukulele", Instrument.Ukulele), ("uke", Instrument.Ukulele),
    ];

    private static readonly (string Text, int Offset)[] OffsetNames =
    [
        ("half step down", -1), ("half-step down", -1), ("1/2 step down", -1), ("½ step down", -1),
        ("down a half step", -1), ("down 1/2", -1), ("semitone down", -1), ("eb standard", -1),
        ("e♭ standard", -1), ("eb tuning", -1), ("tuned to eb", -1), ("down to eb", -1),
        ("whole step down", -2), ("full step down", -2), ("down a whole step", -2), ("d standard", -2),
    ];

    /// <summary>
    /// Reads a tuning as charts write it: "Drop D", "DADGAD", "Half step down", "Eb standard",
    /// "Drop D, half step down", "D-A-A", "Baritone". Null when nothing is recognized (or it's
    /// just "standard").
    /// </summary>
    public static SongTuning? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var lower = text.Trim().ToLowerInvariant();
        var offset = OffsetNames.FirstOrDefault(o => lower.Contains(o.Text)).Offset;
        // "Eb" alone, or "Eb Eb standard": the guitar a half step down.
        if (offset == 0 && lower is "eb" or "e♭")
            offset = -1;
        var instrument = InstrumentNames.FirstOrDefault(i => Contains(lower, i.Text)).Instrument;
        return instrument is null && offset == 0 ? null : new SongTuning(instrument, offset);
    }

    // Matches whole words, so "dad" isn't found in "dadgad" (it's listed after it anyway).
    private static bool Contains(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var end = at + word.Length;
            var after = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The instrument to show for a song: its own tuning when that's for the same kind of
    /// instrument as the player's (a Drop D song on guitar), otherwise the player's.
    /// </summary>
    public Instrument Apply(Instrument player) =>
        Instrument is { } own && own.Family == player.Family ? own : player;

    /// <summary>
    /// A song's tuning: the one chosen for it (kept with its <see cref="SongSettings"/>), otherwise the
    /// one its file gives; null when it's played in the usual tuning.
    /// </summary>
    public static SongTuning? For(string? fileTuning, SongSettings? saved)
    {
        var file = Parse(fileTuning);
        var instrument = saved?.Tuning is { } name ? Instrument.Find(name) : file?.Instrument;
        var offset = saved?.TuningOffset ?? file?.Offset ?? 0;
        return instrument is null && offset == 0 ? null : new SongTuning(instrument, offset);
    }

    /// <summary>
    /// The tuning to note in a song's header ("Drop D, half step down"), when the song isn't played in
    /// the player's usual one; otherwise null.
    /// </summary>
    /// <param name="shown">The instrument the song is shown for (<see cref="Apply"/>).</param>
    public static string? Note(Instrument shown, Instrument player, int offset)
    {
        List<string> parts = [];
        if (shown != player)
            parts.Add(ShortName(shown));
        if (offset != 0)
            parts.Add(DescribeOffset(offset));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>An instrument's name without its family: "Guitar (Drop D)" → "Drop D"; "Mandolin" stays.</summary>
    public static string ShortName(Instrument instrument) =>
        instrument.Name.IndexOf('(') is var open and > 0 && instrument.Name.EndsWith(')')
            ? instrument.Name[(open + 1)..^1]
            : instrument.Name;

    /// <summary>The pitch offset for people: "half step down", "whole step down", "3 half steps down".</summary>
    public static string DescribeOffset(int semitones) => semitones switch
    {
        -1 => "half step down",
        -2 => "whole step down",
        1 => "half step up",
        2 => "whole step up",
        < 0 => $"{-semitones} half steps down",
        _ => $"{semitones} half steps up",
    };

    public override string ToString() => (Instrument, Offset) switch
    {
        (null, var o) => DescribeOffset(o),
        ({ } i, 0) => i.Name,
        ({ } i, var o) => $"{i.Name}, {DescribeOffset(o)}",
    };
}
