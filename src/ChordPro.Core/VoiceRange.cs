namespace ChordPro.Core;

/// <summary>
/// The keys a singer is comfortable in, learned from songs they marked as sitting well for their
/// voice and songs they transposed (moving a song is usually for the voice). Charts don't hold the
/// melody, so a key is a stand-in for range: a song whose key is close to one of these probably
/// sits well too, and one far from them can be moved to the nearest.
/// </summary>
public sealed class VoiceRange(IEnumerable<string> comfortableKeys)
{
    // Within a semitone of a comfortable key is close enough not to suggest moving it.
    private const int CloseEnough = 1;

    private readonly List<int> _tonics = [.. comfortableKeys
        .Select(SongLibrary.ParseKey).OfType<(int Root, bool Minor)>()
        .Select(k => k.Root).Distinct()];

    /// <summary>The comfortable keys this was built from.</summary>
    public IReadOnlyList<string> Keys { get; } = [.. comfortableKeys];

    public bool IsEmpty => _tonics.Count == 0;

    /// <summary>How well a key (as heard) suits the voice, from 0 to 1; 0.6 when there's nothing to go on.</summary>
    public double Fit(string? key)
    {
        if (IsEmpty || key is null || SongLibrary.ParseKey(key) is not var (root, _))
            return 0.6;
        var distance = Distance(root);
        return Math.Exp(-distance * distance / 6.0);
    }

    /// <summary>
    /// For a key far from the comfortable ones, the smallest transposition (−5 to +6 semitones) that
    /// brings it close; null when it's already close or there's nothing to go on.
    /// </summary>
    public int? SuggestShift(string? key)
    {
        if (IsEmpty || key is null || SongLibrary.ParseKey(key) is not var (root, _) || Distance(root) <= CloseEnough)
            return null;
        return Enumerable.Range(-5, 12)
            .OrderBy(shift => Distance((root + shift + 12) % 12))
            .ThenBy(Math.Abs)
            .First();
    }

    private int Distance(int root) => _tonics.Min(t => Math.Min((root - t + 12) % 12, (t - root + 12) % 12));
}
