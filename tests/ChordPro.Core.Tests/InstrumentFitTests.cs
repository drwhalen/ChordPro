using ChordPro.Core.Chords;

namespace ChordPro.Core.Tests;

public class InstrumentFitTests
{
    [Fact]
    public void ListsChordsTheInstrumentCantPlay()
    {
        var result = InstrumentFit.Check(["C", "Am", "F", "G", "G7", "E"], Instrument.DulcimerDAdd);

        Assert.Equal(["F"], result.Unplayable);
        Assert.Equal(["G7", "E"], result.Partial.Select(p => p.Chord));
        Assert.False(result.AllPlayable);
        Assert.True(InstrumentFit.Check(["C", "Am", "F", "G7"], Instrument.Guitar).Complete);
    }

    [Fact]
    public void SuggestsATranspositionThatFitsTheDulcimer()
    {
        // In C, F has no notes on a D-A-DD dulcimer; up a whole step (D, Em, G, A) everything fits.
        var suggestion = InstrumentFit.SuggestTransposition(["C", "Am", "F", "G"], Instrument.DulcimerDAdd, "C");

        Assert.NotNull(suggestion);
        Assert.Equal(2, suggestion.Semitones);
        Assert.True(suggestion.Result.Complete);
    }

    [Fact]
    public void SuggestsNothingWhenTheChordsAlreadyFit() =>
        Assert.Null(InstrumentFit.SuggestTransposition(["D", "G", "A", "Bm"], Instrument.DulcimerDAdd, "D"));

    [Theory]
    [InlineData("Drop D", "Guitar (Drop D)", 0)]
    [InlineData("DADGAD", "Guitar (DADGAD)", 0)]
    [InlineData("Half step down", null, -1)]
    [InlineData("Eb standard", null, -1)]
    [InlineData("Eb", null, -1)]
    [InlineData("Drop D, half step down", "Guitar (Drop D)", -1)]
    [InlineData("D standard", null, -2)]
    [InlineData("D-A-A", "Dulcimer (D-A-A)", 0)]
    [InlineData("D-A-D", "Dulcimer (D-A-D)", 0)]
    [InlineData("Merlin", "Dulcimer / Merlin (D-A-DD)", 0)]
    [InlineData("baritone", "Baritone ukulele", 0)]
    public void ReadsTuningsAsChartsWriteThem(string text, string? instrument, int offset)
    {
        var tuning = SongTuning.Parse(text)!;
        Assert.Equal(instrument, tuning.Instrument?.Name);
        Assert.Equal(offset, tuning.Offset);
    }

    [Fact]
    public void IgnoresStandardTuningAndNonsense()
    {
        Assert.Null(SongTuning.Parse("Standard"));
        Assert.Null(SongTuning.Parse(""));
        Assert.Null(SongTuning.Parse("dadgadx"));
    }

    [Fact]
    public void AppliesASongsTuningOnlyToTheSameKindOfInstrument()
    {
        var dropD = SongTuning.Parse("Drop D")!;
        Assert.Same(Instrument.GuitarDropD, dropD.Apply(Instrument.Guitar));
        Assert.Same(Instrument.GuitarDropD, dropD.Apply(Instrument.GuitarOpenG));
        Assert.Same(Instrument.DulcimerDAdd, dropD.Apply(Instrument.DulcimerDAdd)); // a Merlin player keeps the Merlin
        Assert.Same(Instrument.DulcimerDAA, SongTuning.Parse("D-A-A")!.Apply(Instrument.MerlinGDgg));
    }

    [Fact]
    public void RetunesTheStringsForTheTuner()
    {
        var down = Instrument.Guitar.Retuned(-1);
        Assert.Equal([39, 44, 49, 54, 58, 63], down.Tuning);
        Assert.Equal("Guitar, half step down", down.Name);
        Assert.Same(Instrument.Guitar, Instrument.Guitar.Retuned(0));
    }

    [Fact]
    public void ReadsTheTuningDirective()
    {
        var song = ChordProParser.Parse("{title: Run Like Hell}\n{x_tuning: Drop D}\n[D]Run");
        Assert.Equal("Drop D", song.Tuning);
    }
}
