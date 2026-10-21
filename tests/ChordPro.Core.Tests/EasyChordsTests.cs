using ChordPro.Core;
using ChordPro.Core.Chords;

namespace ChordPro.Core.Tests;

public class EasyChordsTests
{
    [Theory]
    [InlineData("Cmaj9", "C")]
    [InlineData("Cmaj7", "C")]
    [InlineData("Am7", "Am")]
    [InlineData("Am9", "Am")]
    [InlineData("E9", "E7")]
    [InlineData("G13", "G7")]
    [InlineData("D7", "D7")]
    [InlineData("G/B", "G")]
    [InlineData("F#m7b5", "F#m")]
    [InlineData("Bbadd9", "Bb")]
    [InlineData("C6", "C")]
    [InlineData("Eaug", "E")]
    [InlineData("Dsus4", "Dsus4")]
    [InlineData("A7sus4", "Asus4")]
    [InlineData("Asus2", "Asus2")]
    [InlineData("Bdim7", "Bdim")]
    [InlineData("E5", "E5")]
    [InlineData("N.C.", "N.C.")]
    public void SimplifiesChords(string chord, string expected) =>
        Assert.Equal(expected, ChordSimplifier.Simplify(chord));

    [Theory]
    [InlineData("C")]
    [InlineData("G")]
    [InlineData("D")]
    [InlineData("Am")]
    [InlineData("Em")]
    [InlineData("E7")]
    public void OpenChordsAreEasy(string chord) =>
        Assert.True(ChordDifficulty.Of(chord, Instrument.Guitar) <= ChordDifficulty.EasyThreshold, chord);

    [Theory]
    [InlineData("F")]
    [InlineData("Bm")]
    [InlineData("Bb")]
    [InlineData("F#m")]
    [InlineData("Ab")]
    public void BarreChordsAreHarderThanOpenOnes(string chord)
    {
        Assert.True(ChordDifficulty.Of(chord, Instrument.Guitar) > ChordDifficulty.Of("G", Instrument.Guitar) + 2, chord);
        Assert.True(ChordDifficulty.NeedsBarre(ChordShapeFinder.Find(chord, Instrument.Guitar)!), chord);
    }

    [Fact]
    public void SuggestsCapoThreeForASongInBFlat()
    {
        // Bb Eb F Gm with capo 3 become G C D Em.
        var best = CapoSuggester.Suggest([("Bb", 8), ("Eb", 4), ("F", 4), ("Gm", 2)], Instrument.Guitar, "Bb")[0];

        Assert.Equal(3, best.Capo);
        Assert.Equal(["G", "C", "D", "Em"], best.Chords);
        Assert.Equal(0, best.BarreChords);
    }

    [Fact]
    public void KeepsNoCapoWhenTheChordsAreAlreadyOpen()
    {
        var suggestions = CapoSuggester.Suggest([("G", 4), ("C", 2), ("D", 2), ("Em", 1)], Instrument.Guitar, "G");

        Assert.Equal(0, suggestions[0].Capo);
        Assert.Equal(CapoSuggester.MaxCapo + 1, suggestions.Count);
    }

    [Fact]
    public void SpellsShapesForTheirKey()
    {
        // A song heard in Eb, capo 1: D-family shapes spelled with sharps where that key needs them.
        var capo1 = CapoSuggester.Suggest([("Eb", 1), ("Ab", 1), ("Bb", 1)], Instrument.Guitar, "Eb").Single(s => s.Capo == 1);

        Assert.Equal(["D", "G", "A"], capo1.Chords);
    }

    [Fact]
    public void AlternativesStartWithTheStandardShapeAndVaryThePosition()
    {
        var shapes = ChordShapeFinder.Alternatives("A", Instrument.Guitar);

        Assert.Equal("x02220", shapes[0].ToString());
        Assert.True(shapes.Count >= 3);
        Assert.Equal(shapes.Count, shapes.Select(s => s.BaseFret).Distinct().Count());
    }

    [Fact]
    public void AlternativesForNonChordsAreEmpty() =>
        Assert.Empty(ChordShapeFinder.Alternatives("N.C.", Instrument.Guitar));

    [Fact]
    public void ListsChordUsagesInOrderWithRepeats()
    {
        var song = ChordProParser.Parse("[G]a [C]b [G]c\n{sot}\n[x]tab\n{eot}\n[D]d");

        Assert.Equal(["G", "C", "G", "D"], song.ChordUsages());
    }
}
