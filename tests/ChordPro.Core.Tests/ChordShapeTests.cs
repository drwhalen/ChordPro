using ChordPro.Core.Chords;

namespace ChordPro.Core.Tests;

public class ChordShapeTests
{
    [Theory]
    [InlineData("C", new[] { 0, 4, 7 })]
    [InlineData("Am", new[] { 0, 3, 7 })]
    [InlineData("G7", new[] { 0, 4, 7, 10 })]
    [InlineData("Fmaj7", new[] { 0, 4, 7, 11 })]
    [InlineData("Bbm7", new[] { 0, 3, 7, 10 })]
    [InlineData("F#m7b5", new[] { 0, 3, 6, 10 })]
    [InlineData("Bdim7", new[] { 0, 3, 6, 9 })]
    [InlineData("Dsus4", new[] { 0, 5, 7 })]
    [InlineData("A7sus4", new[] { 0, 5, 7, 10 })]
    [InlineData("Cadd9", new[] { 0, 4, 7, 14 })]
    [InlineData("Cadd#11", new[] { 0, 4, 7, 18 })]
    [InlineData("Dadd+11", new[] { 0, 4, 7, 18 })]
    [InlineData("E9", new[] { 0, 4, 7, 10, 14 })]
    [InlineData("Caug", new[] { 0, 4, 8 })]
    [InlineData("E5", new[] { 0, 7 })]
    [InlineData("C7#9", new[] { 0, 4, 7, 10, 15 })]
    public void ParsesChordIntervals(string name, int[] intervals) =>
        Assert.Equal(intervals, ChordName.TryParse(name)!.Intervals);

    [Fact]
    public void ParsesRootAndSlashBass()
    {
        var chord = ChordName.TryParse("D/F#")!;
        Assert.Equal(2, chord.Root);
        Assert.Equal(6, chord.Bass);
    }

    [Theory]
    [InlineData("N.C.")]
    [InlineData("x")]
    [InlineData("Cfoo")]
    public void RejectsNonChords(string name) => Assert.Null(ChordName.TryParse(name));

    [Theory]
    [InlineData("C", "x32010")]
    [InlineData("G", "320003")]
    [InlineData("D", "xx0232")]
    [InlineData("A", "x02220")]
    [InlineData("E", "022100")]
    [InlineData("Am", "x02210")]
    [InlineData("Em", "022000")]
    [InlineData("Dm", "xx0231")]
    [InlineData("F", "133211")]
    [InlineData("Bm", "x24432")]
    [InlineData("B7", "x21202")]
    [InlineData("A#m7", "x13121")] // same notes as Bbm7
    [InlineData("Dsus4", "xx0233")]
    [InlineData("Asus2", "x02200")]
    [InlineData("D/F#", "200232")]
    [InlineData("G/B", "x20003")]
    public void FindsStandardGuitarShapes(string chord, string expected) =>
        Assert.Equal(expected, ChordShapeFinder.Find(chord, Instrument.Guitar)?.ToString());

    [Theory]
    [InlineData("C", "0003")]
    [InlineData("G", "0232")]
    [InlineData("F", "2010")]
    [InlineData("Am", "2000")]
    [InlineData("Em", "0432")]
    [InlineData("D", "2220")]
    [InlineData("G7", "0212")]
    [InlineData("A7", "0100")]
    [InlineData("Bb", "3211")]
    public void FindsStandardUkuleleShapes(string chord, string expected) =>
        Assert.Equal(expected, ChordShapeFinder.Find(chord, Instrument.Ukulele)?.ToString());

    [Theory]
    [InlineData("Cm9")]
    [InlineData("F#m7b5")]
    [InlineData("Ebdim7")]
    [InlineData("Bb13")]
    [InlineData("C#7#9")]
    public void FindsSomePlayableShapeForHarderChords(string chord)
    {
        Assert.NotNull(ChordShapeFinder.Find(chord, Instrument.Guitar));
        Assert.NotNull(ChordShapeFinder.Find(chord, Instrument.Ukulele));
    }

    [Fact]
    public void ReturnsNullForNonChords() => Assert.Null(ChordShapeFinder.Find("N.C.", Instrument.Guitar));

    [Theory]
    [InlineData("x32010", 1)]
    [InlineData("x46664", 4)]
    [InlineData("133211", 1)]
    [InlineData("000000", 1)]
    public void ComputesBaseFret(string shape, int baseFret) =>
        Assert.Equal(baseFret, ChordShape.Parse(shape).BaseFret);

    [Fact]
    public void ParsesSpaceSeparatedFretsWithTwoDigits() =>
        Assert.Equal(new[] { -1, 10, 12, 12, 11, 10 }, ChordShape.Parse("x 10 12 12 11 10").Frets);

    [Theory]
    [InlineData("D", "0-0-2")]
    [InlineData("G", "0-1-3")]
    [InlineData("Em", "1-1-3")]
    [InlineData("Bm", "0-1-2")]
    [InlineData("D7", "0-5-6")]
    [InlineData("A", "1-2-4")]
    public void FindsMerlinShapesOnItsDiatonicFrets(string chord, string tab) =>
        Assert.Equal(tab, Instrument.Merlin.Notation(ChordShapeFinder.Find(chord, Instrument.Merlin)!));

    [Fact]
    public void NamesTheMerlinsSixAndAHalfFret()
    {
        // Fret 7 as laid out is the 6+ fret, a semitone below the octave: G# on the A string, for E7.
        var shape = ChordShapeFinder.Find("E7", Instrument.Merlin)!;
        Assert.Equal("7-6+-8", Instrument.Merlin.Notation(shape));
        Assert.Equal(11, Instrument.Merlin.Semitones(7));
    }

    [Fact]
    public void PlaysRootAndFifthWhereTheMerlinHasNoThird()
    {
        // No G# within reach for E major: E5, as Merlin players play it.
        Assert.Equal("1-1-1", Instrument.Merlin.Notation(ChordShapeFinder.Find("E", Instrument.Merlin)!));
        // No F or Bb anywhere on a D-A-D diatonic neck.
        Assert.Null(ChordShapeFinder.Find("F", Instrument.Merlin));
    }

    [Fact]
    public void SuggestsNoCapoOnADiatonicNeck() =>
        Assert.Equal([0], CapoSuggester.Suggest([("G", 2), ("D", 2)], Instrument.Merlin).Select(s => s.Capo));
}
