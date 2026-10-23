using ChordPro.Core.Chords;

namespace ChordPro.Core.Tests;

public class KeyCompanionTests
{
    [Fact]
    public void ListsTheChordsOfAMajorKey()
    {
        var g = KeyChords.For("G")!;
        Assert.Equal(["G", "Am", "Bm", "C", "D", "Em", "F#dim"], g.Triads.Select(c => c.Chord));
        Assert.Equal(["I", "ii", "iii", "IV", "V", "vi", "vii°"], g.Triads.Select(c => c.Numeral));
        Assert.Equal(["Gmaj7", "Am7", "Bm7", "Cmaj7", "D7", "Em7", "F#m7b5"], g.Sevenths.Select(c => c.Chord));
        Assert.Equal(["F", "Cm", "Eb", "A", "B"], g.Borrowed.Select(c => c.Chord));
    }

    [Fact]
    public void ListsTheChordsOfAMinorKeyWithFlatsWhereTheKeyHasThem()
    {
        var dm = KeyChords.For("Dm")!;
        Assert.Equal(["Dm", "Edim", "F", "Gm", "Am", "Bb", "C"], dm.Triads.Select(c => c.Chord));
        Assert.Equal(["A", "G", "Eb"], dm.Borrowed.Select(c => c.Chord));
        Assert.Equal("Bb", KeyChords.For("F")!.Triads[3].Chord);
        Assert.Null(KeyChords.For("H"));
    }

    [Fact]
    public void FindsTheKeyInForceAtTheCursor()
    {
        var text = "{title: T}\n{key: G}\n[G]Verse\n{key: A}\n[A]Lift\n";
        Assert.Equal("G", EditorInsert.KeyAt(text, 0));                       // in the header
        Assert.Equal("G", EditorInsert.KeyAt(text, text.IndexOf("[G]Verse")));
        Assert.Equal("A", EditorInsert.KeyAt(text, text.IndexOf("[A]Lift")));
        Assert.Null(EditorInsert.KeyAt("[G]No key", 3));
    }

    [Fact]
    public void ASecondKeyIsAKeyChangeWhereItStands()
    {
        var song = ChordProParser.Parse("{key: G}\n[G]Verse\n{key: A}\n[A]Lift\n");
        Assert.Equal("G", song.Key);
        Assert.Contains(song.Sections.SelectMany(s => s.Lines), l => l is KeyChangeLine { Key: "A" });
    }

    [Fact]
    public void WritesAChosenFingeringIntoTheSong()
    {
        var text = "{title: T}\n{key: C}\n\n[C]La\n";
        var withC = SongFingerings.Set(text, "C", ChordShape.Parse("x35553"), Instrument.Guitar);
        Assert.Equal("{title: T}\n{key: C}\n\n{define: C base-fret 1 frets x 3 5 5 5 3}\n\n[C]La\n", withC);

        // a second chord goes with the first; choosing again replaces it
        var withF = SongFingerings.Set(withC, "F", ChordShape.Parse("133211"), Instrument.Guitar);
        var again = SongFingerings.Set(withF, "C", ChordShape.Parse("x32010"), Instrument.Guitar);
        Assert.Equal("{title: T}\n{key: C}\n\n{define: C base-fret 1 frets x 3 2 0 1 0}\n{define: F base-fret 1 frets 1 3 3 2 1 1}\n\n[C]La\n", again);

        Assert.Equal(withF.Replace("{define: C base-fret 1 frets x 3 5 5 5 3}\n", ""), SongFingerings.Remove(withF, "C", Instrument.Guitar));
    }

    [Fact]
    public void AFingeringForAnotherInstrumentIsTaggedWithIt()
    {
        var text = SongFingerings.Set("{title: T}\n\n[C]La\n", "C", ChordShape.Parse("0003"), Instrument.Ukulele);
        Assert.Contains("{define-ukulele: C base-fret 1 frets 0 0 0 3}", text);

        var song = ChordProParser.Parse(text);
        Assert.Empty(song.ChordDefinitions);   // not the guitar's
        Assert.Equal([0, 0, 0, 3], song.InstrumentDefinitions["ukulele"]["C"].Frets);
        Assert.Equal("dulcimermerlindadd", Instrument.DulcimerDAdd.Selector);
        Assert.Null(Instrument.Guitar.Selector);
    }
}
