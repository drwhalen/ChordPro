namespace ChordPro.Core.Chords;

/// <summary>A capo position and what the song's chords become with it.</summary>
public sealed record CapoSuggestion(int Capo, IReadOnlyList<string> Chords, double Difficulty, int BarreChords, int UnplayableChords);

/// <summary>
/// Ranks capo positions by how easy the song's chords are to play with them. The song sounds the
/// same at every position; only the shapes change.
/// </summary>
public static class CapoSuggester
{
    public const int MaxCapo = 7;

    /// <param name="soundingChords">The chords as heard (written chords moved by any transposition and the file's capo), with how often each is played.</param>
    /// <param name="soundingKey">The key as heard, used to spell the shapes with sharps or flats; may be null.</param>
    /// <returns>Every capo position from 0 to <see cref="MaxCapo"/>, easiest first.</returns>
    public static IReadOnlyList<CapoSuggestion> Suggest(
        IReadOnlyList<(string Chord, int Count)> soundingChords, Instrument instrument, string? soundingKey = null)
    {
        var suggestions = new List<CapoSuggestion>();
        for (var capo = 0; capo <= MaxCapo; capo++)
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
