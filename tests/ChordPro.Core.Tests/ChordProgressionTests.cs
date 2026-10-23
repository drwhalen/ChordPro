using ChordPro.Core.Chords;

namespace ChordPro.Core.Tests;

public class ChordProgressionTests
{
    [Theory]
    [InlineData("G", "D", "G Em C")]        // V → I, vi, IV
    [InlineData("G", "D7", "G Em C")]       // a seventh counts as its chord
    [InlineData("G", "C", "G D Am Em")]     // IV → I, V, ii, vi
    [InlineData("G", "F", "G C")]           // ♭VII → I, IV
    [InlineData("Am", "Am", "Dm F G E")]    // i → iv, VI, VII, V
    public void SuggestsWhatUsuallyComesNext(string key, string chord, string next) =>
        Assert.Equal(next, string.Join(" ", ChordProgressions.Next(key, chord).Select(c => c.Chord)));

    [Fact]
    public void SuggestsNothingForAChordOutsideTheKey()
    {
        Assert.Empty(ChordProgressions.Next("G", "C#"));
        Assert.Empty(ChordProgressions.Next(null, "G"));
    }

    [Theory]
    [InlineData("C", "C", "1")]
    [InlineData("C", "Am", "6m")]
    [InlineData("C", "G7", "5⁷")]
    [InlineData("C", "Fmaj7", "4maj⁷")]
    [InlineData("C", "C/E", "1/3")]
    [InlineData("C", "Bb", "♭7")]
    [InlineData("G", "Dsus4", "5sus⁴")]
    [InlineData("Am", "C", "♭3")]
    [InlineData("Eb", "Ab", "4")]
    public void WritesChordsAsNashvilleNumbers(string key, string chord, string number) =>
        Assert.Equal(number, ChordProgressions.Number(chord, key));

    [Fact]
    public void LeavesNonChordsAlone()
    {
        Assert.Null(ChordProgressions.Number("N.C.", "C"));
        Assert.Null(ChordProgressions.Number("G", null));
    }
}
