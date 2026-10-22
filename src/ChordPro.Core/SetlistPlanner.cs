namespace ChordPro.Core;

/// <summary>A song as the planner sees it: the key heard (with the capo on), tempo, capo, and how often it's been played.</summary>
public sealed record PlannedSong(SongInfo Song, string? Key, double? Tempo, int Capo, int Plays = 0);

/// <summary>How well one song leads into the next, with the reasons in words.</summary>
public sealed record SetlistStep(double Score, string Key, string? Tempo, string? Capo, string? Artist)
{
    /// <summary>
    /// Good, fine or a bump: for colouring the step. Capo and artist are usually fine, so the bars sit
    /// high enough that a far key or a tempo lurch shows as a bump on its own.
    /// </summary>
    public string Rating => Score >= 0.8 ? "good" : Score >= 0.6 ? "fine" : "bump";

    public override string ToString() => string.Join(" · ", new[] { Key, Tempo, Capo, Artist }.OfType<string>());
}

/// <summary>A set in order, with how each song leads into the next (one step fewer than songs).</summary>
public sealed record SetlistPlan(IReadOnlyList<PlannedSong> Songs, IReadOnlyList<SetlistStep> Steps, double Score);

/// <summary>What the planner is asked for.</summary>
public sealed record SetlistRequest(
    IReadOnlyList<PlannedSong> Pool,
    IReadOnlyList<PlannedSong> MustInclude,
    int Length,
    bool NearMustIncludeKeys = true,
    bool FavourPlayed = false,
    int Variation = 0,
    VoiceRange? Voice = null);

/// <summary>
/// Plans sets that flow: each song leads into the next by key (the same key, its relative major or
/// minor, a fifth away, or a step up for a lift), by tempo (small changes, not lurches) and by capo
/// (fewer moves between songs), with variety of artists. Across the whole set it follows a shape —
/// open with energy, ease off in the middle, lift to the end — avoids long runs in one key, and
/// prefers keys near the songs the singer picked (a stand-in for their range), songs from the same
/// collections (books) as those, and, if asked, songs they play often.
/// </summary>
public static class SetlistPlanner
{
    // How much each part of a step counts.
    private const double KeyWeight = 0.4, TempoWeight = 0.25, CapoWeight = 0.15, ArtistWeight = 0.2;
    // How much each song's own fit counts, against a step's 1.
    private const double VoiceWeight = 0.35, ShapeWeight = 0.5, PlayedWeight = 0.2, CollectionWeight = 0.45;
    // A third song in a row in the same key: the set starts to sound the same.
    private const double SameKeyRunPenalty = 0.35;
    private const int BeamWidth = 40;

    /// <summary>How <paramref name="b"/> follows <paramref name="a"/>.</summary>
    public static SetlistStep Step(PlannedSong a, PlannedSong b)
    {
        var (keyScore, keyText) = KeyStep(a.Key, b.Key);
        var (tempoScore, tempoText) = TempoStep(a.Tempo, b.Tempo);
        var (capoScore, capoText) = a.Capo == b.Capo ? (1.0, null as string) : (0.55, $"capo {CapoName(a.Capo)} → {CapoName(b.Capo)}");
        var sameArtist = a.Song.Artist is not null && string.Equals(a.Song.Artist, b.Song.Artist, StringComparison.CurrentCultureIgnoreCase);
        var (artistScore, artistText) = sameArtist ? (0.3, $"{a.Song.Artist} again") : (1.0, null as string);
        var score = KeyWeight * keyScore + TempoWeight * tempoScore + CapoWeight * capoScore + ArtistWeight * artistScore;
        return new SetlistStep(score, keyText, tempoText, capoText, artistText);

        static string CapoName(int capo) => capo == 0 ? "off" : capo.ToString();
    }

    /// <summary>The flow of a set in the order given.</summary>
    public static SetlistPlan Describe(IReadOnlyList<PlannedSong> songs)
    {
        var steps = songs.Zip(songs.Skip(1), Step).ToList();
        return new SetlistPlan(songs, steps, steps.Sum(s => s.Score));
    }

    /// <summary>
    /// Plans a set of <see cref="SetlistRequest.Length"/> songs from the pool, always including the
    /// must-include songs. A different <see cref="SetlistRequest.Variation"/> gives another good set.
    /// </summary>
    public static SetlistPlan Plan(SetlistRequest request)
    {
        var must = request.MustInclude.DistinctBy(s => s.Song.Path).ToList();
        var pool = must.Concat(request.Pool).DistinctBy(s => s.Song.Path).ToList();
        var length = Math.Clamp(Math.Max(request.Length, must.Count), 1, pool.Count);
        if (pool.Count == 0)
            return new SetlistPlan([], [], 0);

        var context = new Context(pool, must, request);
        var random = new Random(request.Variation);
        // Small random nudges make each variation a different, still good, set; none for the first.
        double Nudge() => request.Variation == 0 ? 0 : random.NextDouble() * 0.25;

        var beams = new List<(List<int> Order, HashSet<int> Used, double Score)> { ([], [], 0) };
        for (var position = 0; position < length; position++)
        {
            var next = new List<(List<int> Order, HashSet<int> Used, double Score)>();
            foreach (var (order, used, score) in beams)
            {
                var mustLeft = context.MustIndexes.Count(i => !used.Contains(i));
                var slotsLeft = length - position;
                foreach (var candidate in Enumerable.Range(0, pool.Count))
                {
                    if (used.Contains(candidate))
                        continue;
                    // Leave room for every song that has to be in the set.
                    if (mustLeft >= slotsLeft && !context.MustIndexes.Contains(candidate))
                        continue;
                    var gain = context.SongFit(candidate, position, length) + Nudge();
                    if (order.Count > 0)
                        gain += context.StepScore(order[^1], candidate);
                    if (order.Count > 1 && context.SameKey(order[^2], order[^1]) && context.SameKey(order[^1], candidate))
                        gain -= SameKeyRunPenalty;
                    next.Add(([.. order, candidate], [.. used, candidate], score + gain));
                }
            }
            beams = next.OrderByDescending(b => b.Score).Take(BeamWidth).ToList();
        }

        var best = beams[0].Order.Select(i => pool[i]).ToList();
        return Describe(best) with { Score = beams[0].Score };
    }

    // ---- key and tempo ---------------------------------------------------------------------

    private static readonly string[] Names = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    private static (double, string) KeyStep(string? from, string? to)
    {
        if (from is null || to is null || SongLibrary.ParseKey(from) is not var (a, aMinor) || SongLibrary.ParseKey(to) is not var (b, bMinor))
            return (0.5, from is null || to is null ? "key unknown" : $"{from} → {to}");
        var label = $"{from} → {to}";
        var up = (b - a + 12) % 12;
        // Good, but a little below a related key: a set needs some movement.
        if (up == 0 && aMinor == bMinor)
            return (0.85, $"{label}: same key");
        // Relative major and minor share their notes (C and Am).
        if (aMinor != bMinor && (aMinor ? (a + 3) % 12 == b : (b + 3) % 12 == a))
            return (0.92, $"{label}: relative {(bMinor ? "minor" : "major")}");
        if (up == 0)
            return (0.6, $"{label}: same root, {(bMinor ? "minor" : "major")} now");
        // Neighbours on the circle of fifths, compared through the relative major.
        var (ma, mb) = (aMinor ? (a + 3) % 12 : a, bMinor ? (b + 3) % 12 : b);
        var fifths = Math.Min((mb * 7 - ma * 7 + 120) % 12, (ma * 7 - mb * 7 + 120) % 12);
        if (aMinor == bMinor && up is 1 or 2)
            return (0.75, $"{label}: up a {(up == 1 ? "half" : "whole")} step, a lift");
        if (fifths == 1)
            return (0.85, $"{label}: {((mb - ma + 12) % 12 == 7 ? "up a fifth" : "up a fourth")}, a close key");
        if (fifths == 2)
            return (0.6, $"{label}: a related key");
        return (Math.Max(0.15, 0.45 - 0.08 * (fifths - 3)), $"{label}: a fresh start");
    }

    private static (double, string?) TempoStep(double? from, double? to)
    {
        if (from is not { } a || to is not { } b)
            return (0.5, null);
        var change = Math.Abs(Math.Log(b / a));
        var score = Math.Exp(-Math.Pow(change / 0.2, 2));
        var feel = change < 0.06 ? "steady" : b > a ? "picks up" : "eases off";
        return (score, $"{a:0} → {b:0} BPM, {feel}");
    }

    // Precomputed scores for one planning run.
    private sealed class Context
    {
        private readonly IReadOnlyList<PlannedSong> _pool;
        private readonly double[,] _steps;
        private readonly double[] _voice;
        private readonly double[] _energy;
        private readonly double[] _played;
        private readonly double[] _collection;
        private readonly (int Root, bool Minor)?[] _keys;
        private readonly SetlistRequest _request;

        public HashSet<int> MustIndexes { get; }

        public Context(IReadOnlyList<PlannedSong> pool, IReadOnlyList<PlannedSong> must, SetlistRequest request)
        {
            _pool = pool;
            _request = request;
            MustIndexes = [.. must.Select(m => pool.ToList().FindIndex(p => p.Song.Path == m.Song.Path))];

            _steps = new double[pool.Count, pool.Count];
            for (var i = 0; i < pool.Count; i++)
                for (var j = 0; j < pool.Count; j++)
                    if (i != j)
                        _steps[i, j] = Step(pool[i], pool[j]).Score;

            // Near the singer's comfortable keys: the ones they marked or transposed to, and the keys of
            // the songs picked for this set.
            var keys = (request.NearMustIncludeKeys ? must.Select(m => m.Key).OfType<string>() : [])
                .Concat(request.Voice?.Keys ?? []);
            var voice = new VoiceRange(keys);
            _voice = [.. pool.Select(p => voice.Fit(p.Key))];

            // Energy by tempo, as a rank within the pool (0 slowest, 1 fastest); unknown is middling.
            var tempos = pool.Select(p => p.Tempo).OfType<double>().Order().ToList();
            _energy = [.. pool.Select(p => p.Tempo is { } t && tempos.Count > 1 ? tempos.FindLastIndex(x => x <= t) / (double)(tempos.Count - 1) : 0.5)];

            var mostPlays = Math.Max(1, pool.Max(p => p.Plays));
            _played = [.. pool.Select(p => Math.Log(1 + p.Plays) / Math.Log(1 + mostPlays))];

            // Songs from the same collections as the picked ones belong together; a song only in other
            // books (Christmas songs beside rock songs) fits less well. Songs in no book are neutral.
            var pickedBooks = must.SelectMany(m => m.Song.Books).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            _collection = [.. pool.Select(p => pickedBooks.Count == 0 || p.Song.Books.Count == 0 ? 0.6
                : p.Song.Books.Any(pickedBooks.Contains) ? 1.0 : 0.0)];

            _keys = [.. pool.Select(p => p.Key is null ? null : SongLibrary.ParseKey(p.Key))];
        }

        public bool SameKey(int a, int b) => _keys[a] is { } x && _keys[b] is { } y && x == y;

        public double StepScore(int from, int to) => _steps[from, to];

        public double SongFit(int song, int position, int length)
        {
            // The shape of a set: start lively, ease off past the middle, finish strongest.
            var x = length <= 1 ? 0 : position / (double)(length - 1);
            var target = x < 0.55 ? 0.7 - 0.6 * x : 0.37 + 1.15 * (x - 0.55);
            var shape = 1 - Math.Abs(_energy[song] - Math.Clamp(target, 0, 1));
            var fit = VoiceWeight * _voice[song] + ShapeWeight * shape + CollectionWeight * _collection[song];
            if (_request.FavourPlayed)
                fit += PlayedWeight * _played[song];
            return fit;
        }
    }
}
