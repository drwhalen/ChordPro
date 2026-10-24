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
    public void ASongsChosenTuningWinsOverItsFiles()
    {
        Assert.Null(SongTuning.For(null, null));
        Assert.Equal(new SongTuning(Instrument.GuitarDropD, -1), SongTuning.For("Drop D, half step down", null));
        // Chosen on another device: back to standard pitch, in DADGAD.
        var saved = new SongSettings { Tuning = Instrument.GuitarDadgad.Name, TuningOffset = 0 };
        Assert.Equal(new SongTuning(Instrument.GuitarDadgad, 0), SongTuning.For("Drop D, half step down", saved));
    }

    [Fact]
    public void NotesATuningOnlyWhenItIsNotThePlayersUsualOne()
    {
        Assert.Null(SongTuning.Note(Instrument.Guitar, Instrument.Guitar, 0));
        Assert.Equal("Drop D, half step down", SongTuning.Note(Instrument.GuitarDropD, Instrument.Guitar, -1));
        Assert.Equal("whole step down", SongTuning.Note(Instrument.Guitar, Instrument.Guitar, -2));
        Assert.Equal("Mandolin", SongTuning.ShortName(Instrument.Mandolin));
    }

    [Theory]
    [InlineData("{meta: tuning Drop D}")]
    [InlineData("{x_tuning: Drop D}")]
    public void ReadsTheTuningFromMetadata(string directive)
    {
        var song = ChordProParser.Parse("{title: Run Like Hell}\n" + directive + "\n[D]Run");
        Assert.Equal("Drop D", song.Tuning);
    }

    [Fact]
    public void TuningDownLowersTheKeyYouHearLikeACapoRaisesIt()
    {
        // Patience: C shapes, tuned a half step down, sounds in B; with a capo on 1 as well, in C.
        Assert.Equal("B", ChordTransposer.SoundingKey("C", 0, keyIncludesCapo: false, tuningOffset: -1));
        Assert.Equal("C", ChordTransposer.SoundingKey("C", 1, keyIncludesCapo: false, tuningOffset: -1));
        // Across the Universe: D shapes tuned down sound in Db.
        Assert.Equal("Db", ChordTransposer.SoundingKey("D", 0, keyIncludesCapo: false, tuningOffset: -1));
        // A key written as heard already includes the tuning.
        Assert.Equal("Db", ChordTransposer.SoundingKey("Db", 0, keyIncludesCapo: true, tuningOffset: -1));
        Assert.Equal("D", ChordTransposer.WrittenKey("Db", 0, keyIncludesCapo: true, tuningOffset: -1));
    }

    [Fact]
    public void LibrarySongsKnowTheirTuning()
    {
        var song = new SongInfo("/x.cho", "Patience", "Guns N' Roses", "C") { Tuning = "Half step down" };
        Assert.Equal(-1, song.TuningOffset);
        Assert.Equal(["B"], SongLibrary.Keys([song]).Select(k => k.Key));
    }
}
