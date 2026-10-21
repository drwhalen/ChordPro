using ChordPro.Core;

namespace ChordPro.Core.Tests;

public class ChordTransposerTests
{
    [Theory]
    [InlineData("C", 2, "D")]
    [InlineData("G7", 1, "G#7")]
    [InlineData("Am", -2, "Gm")]
    [InlineData("Bb", 2, "C")]
    [InlineData("Eb", 1, "E")]
    [InlineData("Db", 1, "D")]
    [InlineData("B", 1, "C")]
    [InlineData("C", -1, "B")]
    [InlineData("F#m7b5", 1, "Gm7b5")]
    [InlineData("Cmaj7", 12, "Cmaj7")]
    [InlineData("C", -13, "B")]
    public void TransposesChordRoot(string chord, int semitones, string expected) =>
        Assert.Equal(expected, ChordTransposer.Transpose(chord, semitones));

    [Fact]
    public void KeepsFlatSpellingOfOriginalChord() =>
        Assert.Equal("Db", ChordTransposer.Transpose("Bb", 3));

    [Fact]
    public void TransposesSlashBassNote() =>
        Assert.Equal("A/C#", ChordTransposer.Transpose("G/B", 2));

    [Fact]
    public void ExplicitPreferenceOverridesSpelling()
    {
        Assert.Equal("Eb", ChordTransposer.Transpose("D", 1, preferFlats: true));
        Assert.Equal("C#", ChordTransposer.Transpose("Bb", 3, preferFlats: false));
    }

    [Theory]
    [InlineData("N.C.")]
    [InlineData("x")]
    [InlineData("")]
    public void LeavesNonChordsUnchanged(string text) =>
        Assert.Equal(text, ChordTransposer.Transpose(text, 3));

    [Theory]
    [InlineData("G", 3, true)]   // Bb
    [InlineData("G", 2, false)]  // A
    [InlineData("Am", 5, true)]  // Dm
    [InlineData("Em", 2, false)] // F#m
    [InlineData(null, 1, null)]
    [InlineData("unknown", 1, null)]
    public void ChoosesSpellingFromTransposedKey(string? key, int semitones, bool? expected) =>
        Assert.Equal(expected, ChordTransposer.PrefersFlats(key, semitones));
}
