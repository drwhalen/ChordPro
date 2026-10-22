namespace ChordPro.Core.Chords;

/// <summary>
/// Which of a song's chords an instrument can't play: those with no fingering at all (F on a
/// D-tuned dulcimer), and those it can only play without a note (E as root and fifth).
/// </summary>
public sealed record Playability(IReadOnlyList<string> Unplayable, IReadOnlyList<(string Chord, IReadOnlyList<int> Missing)> Partial)
{
    public bool AllPlayable => Unplayable.Count == 0;
    public bool Complete => Unplayable.Count == 0 && Partial.Count == 0;

    // Fewer chords that can't be played at all matters most, then fewer played without a note.
    internal (int, int) Rank => (Unplayable.Count, Partial.Count);
}

/// <summary>A transposition that suits the instrument better, and how the chords fare after it.</summary>
public sealed record TranspositionSuggestion(int Semitones, Playability Result);

/// <summary>
/// Checks a song's chords against an instrument, and finds the transposition that makes the most
/// of them playable. Mostly for dulcimers and the Merlin, whose diatonic frets leave some chords
/// out in any key; a guitar or ukulele can play anything.
/// </summary>
public static class InstrumentFit
{
    /// <summary>How the chords (as they'll be shown) fare on the instrument.</summary>
    public static Playability Check(IEnumerable<string> chords, Instrument instrument)
    {
        var unplayable = new List<string>();
        var partial = new List<(string, IReadOnlyList<int>)>();
        foreach (var chord in chords.Distinct())
        {
            if (ChordName.TryParse(chord) is null)
                continue;
            if (ChordShapeFinder.Find(chord, instrument) is not { } shape)
                unplayable.Add(chord);
            else if (ChordShapeFinder.MissingTones(chord, shape, instrument) is { Count: > 0 } missing)
                partial.Add((chord, missing));
        }
        return new Playability(unplayable, partial);
    }

    /// <summary>
    /// The transposition (−5 to +6 semitones) that makes the most chords playable, the smallest
    /// such move first; null when none does better than the chords as they are.
    /// </summary>
    /// <param name="key">The key as shown, to spell the moved chords with sharps or flats; may be null.</param>
    public static TranspositionSuggestion? SuggestTransposition(IReadOnlyCollection<string> chords, Instrument instrument, string? key = null)
    {
        var current = Check(chords, instrument);
        if (current.Complete)
            return null;
        var best = Enumerable.Range(-5, 12)
            .Where(shift => shift != 0)
            .Select(shift => new TranspositionSuggestion(shift, Check(Transpose(chords, shift, key), instrument)))
            .OrderBy(s => s.Result.Rank)
            .ThenBy(s => Math.Abs(s.Semitones))
            .ThenByDescending(s => s.Semitones) // up rather than down, for the same distance
            .First();
        return best.Result.Rank.CompareTo(current.Rank) < 0 ? best : null;
    }

    /// <summary>Chords moved by some semitones, spelled for the key they land in.</summary>
    public static IEnumerable<string> Transpose(IEnumerable<string> chords, int semitones, string? key = null)
    {
        var flats = ChordTransposer.PrefersFlats(key, semitones);
        return chords.Select(c => ChordTransposer.Transpose(c, semitones, flats));
    }
}
