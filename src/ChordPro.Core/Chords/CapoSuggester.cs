namespace ChordPro.Core.Chords;

/// <summary>A capo position and what the song's chords become with it.</summary>
public sealed record CapoSuggestion(int Capo, IReadOnlyList<string> Chords, double Difficulty, int BarreChords, int UnplayableChords);

/// <summary>A capo position offered for a song, described for people.</summary>
/// <param name="Title">"Capo 2 · G shapes", or "No capo".</param>
/// <param name="Detail">The chords played with it, and how many are barre chords.</param>
public sealed record CapoChoice(int Capo, string Title, string Detail);

/// <summary>
/// Ranks capo positions by how easy the song's chords are to play with them. The song sounds the
/// same at every position; only the shapes change.
/// </summary>
public static class CapoSuggester
{
    public const int MaxCapo = 7;

    /// <summary>
    /// The easiest capo positions for a song as it's shown now (transposed, with its file's capo), each
    /// sounding the same as now; empty when it has no chords.
    /// </summary>
    /// <param name="simplify">The song is shown with easy chords (<see cref="ChordSimplifier"/>).</param>
    public static IReadOnlyList<CapoChoice> ForSong(Song song, int transpose, bool keyIncludesCapo, int tuningOffset,
        bool simplify, Instrument instrument, int count = 4)
    {
        // Work from the chords as heard, so every suggestion sounds the same as now.
        var sounding = transpose + song.CapoFret;
        var writtenKey = ChordTransposer.WrittenKey(song.Key, song.CapoFret, keyIncludesCapo, tuningOffset);
        var preferFlats = ChordTransposer.PrefersFlats(writtenKey, sounding);
        var chords = song.ChordUsages()
            .Select(c => ChordTransposer.Transpose(simplify ? ChordSimplifier.Simplify(c) : c, sounding, preferFlats))
            .GroupBy(c => c)
            .Select(g => (g.Key, g.Count()))
            .ToList();
        if (chords.Count == 0)
            return [];

        var soundingKey = writtenKey is { } key ? ChordTransposer.Transpose(key, sounding, preferFlats) : null;
        return [.. Suggest(chords, instrument, soundingKey).Take(count).Select(suggestion =>
        {
            var shapesKey = soundingKey is null ? null
                : ChordTransposer.Transpose(soundingKey, -suggestion.Capo, ChordTransposer.PrefersFlats(soundingKey, -suggestion.Capo));
            var title = suggestion.Capo == 0 ? "No capo" : $"Capo {suggestion.Capo}";
            if (shapesKey is not null)
                title += $" · {shapesKey} shapes";
            var barre = suggestion.BarreChords switch { 0 => "no barre chords", 1 => "1 barre chord", var n => $"{n} barre chords" };
            return new CapoChoice(suggestion.Capo, title, $"{string.Join("  ", suggestion.Chords)}  ·  {barre}");
        })];
    }

    /// <param name="soundingChords">The chords as heard (written chords moved by any transposition and the file's capo), with how often each is played.</param>
    /// <param name="soundingKey">The key as heard, used to spell the shapes with sharps or flats; may be null.</param>
    /// <returns>Every capo position from 0 to <see cref="MaxCapo"/>, easiest first.</returns>
    public static IReadOnlyList<CapoSuggestion> Suggest(
        IReadOnlyList<(string Chord, int Count)> soundingChords, Instrument instrument, string? soundingKey = null)
    {
        var suggestions = new List<CapoSuggestion>();
        // On a diatonic neck a capo changes the mode as well as the key, so the shapes don't simply
        // move down: only playing without one is suggested.
        for (var capo = 0; capo <= (instrument.IsDiatonic ? 0 : MaxCapo); capo++)
        {
            var preferFlats = ChordTransposer.PrefersFlats(soundingKey, -capo);
            var shapes = soundingChords.Select(c => (Name: ChordTransposer.Transpose(c.Chord, -capo, preferFlats), c.Count)).ToList();

            var difficulty = 0.0;
            var barre = 0;
            var unplayable = 0;
            foreach (var (name, count) in shapes)
            {
                var shape = ChordShapeFinder.Find(name, instrument);
                if (shape is null)
                {
                    unplayable++;
                    difficulty += ChordDifficulty.Unplayable * count;
                    continue;
                }
                difficulty += ChordDifficulty.Of(shape) * count;
                if (ChordDifficulty.NeedsBarre(shape))
                    barre++;
            }

            // A capo high up the neck is a little less comfortable and sounds thinner.
            difficulty += capo * 0.2 * soundingChords.Sum(c => c.Count) / Math.Max(1, soundingChords.Count);
            suggestions.Add(new CapoSuggestion(capo, [.. shapes.Select(s => s.Name).Distinct()], difficulty, barre, unplayable));
        }

        return [.. suggestions.OrderBy(s => s.Difficulty).ThenBy(s => s.Capo)];
    }
}
