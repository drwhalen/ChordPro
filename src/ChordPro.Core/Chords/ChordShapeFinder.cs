using System.Collections.Concurrent;

namespace ChordPro.Core.Chords;

/// <summary>
/// Finds playable fingerings for a chord on any fretted <see cref="Instrument"/> by searching every
/// four-fret hand position and ranking the shapes a hand can actually play. Diatonic necks (the
/// Merlin) are searched by their frets as laid out; where a full chord isn't on the neck, the root
/// and fifth are played instead, as players of those instruments do.
/// </summary>
public static class ChordShapeFinder
{
    private const int HandSpan = 4;      // frets covered by one hand position
    private const int HighestPosition = 12;
    private const int MaxFingers = 4;

    private static readonly ConcurrentDictionary<(string, string), ChordShape?> Cache = new();

    /// <summary>The best fingering for <paramref name="chordName"/>, or null if it isn't a chord or nothing is playable.</summary>
    public static ChordShape? Find(string chordName, Instrument instrument) =>
        Cache.GetOrAdd((chordName, instrument.Name), _ =>
            ChordName.TryParse(chordName) is { } chord
                ? StandardShapes.Find(chord, instrument) ?? FindAll(chord, instrument).FirstOrDefault()
                : null);

    /// <summary>
    /// A few good fingerings in different places on the neck: the standard shape (if any) first,
    /// then the easiest comfortable shape at each other position, in order up the neck. At most
    /// <paramref name="max"/> shapes.
    /// </summary>
    public static IReadOnlyList<ChordShape> Alternatives(string chordName, Instrument instrument, int max = 6)
    {
        if (ChordName.TryParse(chordName) is not { } chord)
            return [];

        var first = StandardShapes.Find(chord, instrument) ?? FindAll(chord, instrument).FirstOrDefault();
        if (first is null)
            return [];

        // One shape per neck position, so paging shows genuinely different voicings. Up the neck,
        // only closed (movable) shapes within a three-fret reach: open strings ringing against
        // high frets are rarely what a player wants.
        var others = FindAll(chord, instrument)
            .Where(s => s.BaseFret != first.BaseFret && Span(s) <= 2 && (s.BaseFret == 1 || !s.Frets.Contains(0)))
            .GroupBy(s => s.BaseFret)
            .Select(g => g.First()) // FindAll is best first
            .OrderBy(s => s.BaseFret)
            .Take(max - 1);
        return [first, .. others];
    }

    private static int Span(ChordShape shape)
    {
        var fretted = shape.Frets.Where(f => f > 0).ToList();
        return fretted.Count == 0 ? 0 : fretted.Max() - fretted.Min();
    }

    /// <summary>
    /// The chord tones the best fingering of <paramref name="chordName"/> leaves out, beyond those any
    /// voicing may drop (the fifth of a big chord): on a dulcimer, the third it plays root and fifth
    /// without. Empty when the chord is complete, or isn't a chord.
    /// </summary>
    public static IReadOnlyList<int> MissingTones(string chordName, ChordShape shape, Instrument instrument)
    {
        if (ChordName.TryParse(chordName) is not { } chord || shape.Frets.Count != instrument.StringCount)
            return [];
        var notes = shape.Frets
            .Select((fret, i) => fret < 0 ? -1 : (instrument.Tuning[i] + instrument.Semitones(fret)) % 12)
            .ToHashSet();
        return [.. RequiredTones(chord, instrument).Where(tone => !notes.Contains(tone)).Order()];
    }

    /// <summary>All playable fingerings, best first.</summary>
    public static IReadOnlyList<ChordShape> FindAll(ChordName chord, Instrument instrument)
    {
        var shapes = FindAll(chord, instrument, RequiredTones(chord, instrument));
        // Three courses on a diatonic neck often can't hold the third (no F on a Merlin): play the
        // root and fifth (or the root alone) rather than nothing.
        if (shapes.Count == 0 && instrument.IsDiatonic)
        {
            HashSet<int> powerChord = [chord.Root];
            if (chord.Intervals.Contains(7))
                powerChord.Add((chord.Root + 7) % 12);
            shapes = FindAll(chord, instrument, powerChord);
        }
        return shapes;
    }

    private static IReadOnlyList<ChordShape> FindAll(ChordName chord, Instrument instrument, HashSet<int> required)
    {
        var tones = chord.PitchClasses.ToHashSet();
        var candidates = new Dictionary<ChordShape, double>();

        for (var position = 1; position <= Math.Min(HighestPosition, instrument.HighestFret); position++)
        {
            // Each string can be muted, open, or fretted on a chord tone inside the hand span.
            var options = instrument.Tuning.Select(open =>
            {
                var list = new List<int>();
                if (instrument.BassMustBeRoot)
                    list.Add(-1);
                for (var fret = 0; fret < position + HandSpan && fret <= instrument.HighestFret; fret++)
                {
                    if (fret != 0 && fret < position)
                        continue;
                    if (tones.Contains((open + instrument.Semitones(fret)) % 12))
                        list.Add(fret);
                }
                return list;
            }).ToList();

            foreach (var frets in Combinations(options))
            {
                var shape = new ChordShape(frets);
                if (Score(shape, chord, instrument, required) is { } score)
                    candidates[shape] = Math.Min(score, candidates.GetValueOrDefault(shape, double.MaxValue));
            }
        }

        return candidates.OrderBy(c => c.Value).ThenBy(c => c.Key.ToString()).Select(c => c.Key).ToList();
    }

    /// <summary>Chord tones a shape must contain; the perfect fifth (and on small instruments the root) can be left out.</summary>
    private static HashSet<int> RequiredTones(ChordName chord, Instrument instrument)
    {
        var required = chord.PitchClasses.ToHashSet();
        if (required.Count >= 4 && chord.Intervals.Contains(7))
            required.Remove((chord.Root + 7) % 12);
        if (required.Count > instrument.StringCount && !instrument.BassMustBeRoot)
            required.Remove(chord.Root);
        return required;
    }

    // Lower is better; null means the shape isn't valid or isn't playable.
    private static double? Score(ChordShape shape, ChordName chord, Instrument instrument, HashSet<int> required)
    {
        var frets = shape.Frets;
        var strings = frets.Count;

        // Muted strings are only allowed below the lowest sounding string.
        var firstSounding = 0;
        while (firstSounding < strings && frets[firstSounding] < 0)
            firstSounding++;
        if (frets.Skip(firstSounding).Any(f => f < 0))
            return null;
        var muted = firstSounding;
        if (strings - muted < Math.Min(4, strings))
            return null;

        var notes = Enumerable.Range(firstSounding, strings - firstSounding)
            .Select(i => (instrument.Tuning[i] + instrument.Semitones(frets[i])) % 12)
            .ToList();
        if (!required.IsSubsetOf(notes))
            return null;

        if (instrument.BassMustBeRoot)
        {
            var lowest = Enumerable.Range(firstSounding, strings - firstSounding)
                .MinBy(i => instrument.Tuning[i] + instrument.Semitones(frets[i]));
            if ((instrument.Tuning[lowest] + instrument.Semitones(frets[lowest])) % 12 != chord.Bass)
                return null;
        }

        var fretted = frets.Select((f, i) => (Fret: f, String: i)).Where(x => x.Fret > 0).ToList();
        var lowestFret = fretted.Count > 0 ? fretted.Min(x => x.Fret) : 0;
        var highestFret = fretted.Count > 0 ? fretted.Max(x => x.Fret) : 0;

        // One finger can barre the lowest fret if every string from there up is held at or above it.
        var atLowest = fretted.Where(x => x.Fret == lowestFret).ToList();
        var barre = atLowest.Count >= 2 &&
            frets.Skip(atLowest.Min(x => x.String)).All(f => f >= lowestFret);
        var fingers = (barre ? 1 : atLowest.Count) + fretted.Count(x => x.Fret > lowestFret);
        if (fingers > MaxFingers)
            return null;

        var openStrings = frets.Count(f => f == 0);
        var missingTones = chord.PitchClasses.Count(pc => !notes.Contains(pc));
        // Open strings ringing in the middle of a shape played higher up the neck sound muddy and are rare.
        var openInsideShape = lowestFret >= 2 && fretted.Count > 0 &&
            frets.Select((f, i) => (f, i)).Any(x => x.f == 0 && x.i > fretted.Min(y => y.String) && x.i < fretted.Max(y => y.String));

        // Likewise open strings combined with a stretch up to the fourth fret or beyond.
        var openWithStretch = openStrings > 0 && highestFret >= 4;

        return highestFret * 1.0
            + muted * 0.9
            + fingers * 0.5
            + (barre ? 0.8 : 0)
            + missingTones * 0.8
            + (openInsideShape ? 1.5 : 0)
            + (openWithStretch ? 2.5 : 0)
            - openStrings * 0.1;
    }

    private static IEnumerable<int[]> Combinations(List<List<int>> options)
    {
        var current = new int[options.Count];
        return Recurse(0);

        IEnumerable<int[]> Recurse(int index)
        {
            if (index == options.Count)
            {
                yield return (int[])current.Clone();
                yield break;
            }
            foreach (var option in options[index])
            {
                current[index] = option;
                foreach (var combination in Recurse(index + 1))
                    yield return combination;
            }
        }
    }
}
