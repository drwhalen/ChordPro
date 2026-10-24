namespace ChordPro.Core.Tests;

public class NewSongTests
{
    [Fact]
    public void NamesTheFileTheWayTheLibraryDoes()
    {
        Assert.Equal("Sunset Pamlico - Black Sheeps.cho", NewSong.FileName("Sunset Pamlico", "Black Sheeps"));
        Assert.Equal("AC-DC Song - Me.cho", NewSong.FileName("AC/DC Song", "Me"));
        Assert.Equal("Untitled.cho", NewSong.FileName("Untitled", ""));
    }

    [Fact]
    public void StartsWithAVerseAndAChorusAndTheCursorInTheVerse()
    {
        var (text, caret) = NewSong.Template(new NewSongDetails("Sunset Pamlico", "Black Sheeps", "D", 96));
        Assert.StartsWith("{title: Sunset Pamlico}\n{artist: Black Sheeps}\n{key: D}\n{tempo: 96}\n\n{start_of_verse: label=\"Verse 1\"}\n\n{end_of_verse}", text);
        Assert.Contains("{start_of_chorus}\n\n{end_of_chorus}", text);
        Assert.Equal('\n', text[caret]);
        Assert.Equal("{start_of_verse: label=\"Verse 1\"}", text[..caret].Split('\n')[^2]);
        // the parser reads it as a song in D
        Assert.Equal("D", ChordProParser.Parse(text).Key);
    }

    [Fact]
    public void ACopyInTheSameKeyOnlyChangesTheHeader()
    {
        var text = "{title: Song}\n{artist: Me}\n{key: G}\n{define: C base-fret 1 frets x 3 5 5 5 3}\n\n[G]La [C]la\n";
        var copy = NewSong.Copy(text, new NewSongDetails("Song (slow)", "Me", "G"));
        Assert.Equal(text.Replace("{title: Song}", "{title: Song (slow)}"), copy);
    }

    [Fact]
    public void ACopyInAnotherKeyMovesItsChordsAndLeavesOutItsFingerings()
    {
        var text = "{title: Song}\n{artist: Me}\n{key: G}\n{capo: 2}\n{define: C base-fret 1 frets x 3 5 5 5 3}\n\n[G]La [C]la [D7]la\n{key: A}\n[A]Lift\n";
        var copy = NewSong.Copy(text, new NewSongDetails("Song", "Her", "Bb", 100));

        Assert.Equal("{title: Song}\n{artist: Her}\n{key: Bb}\n{tempo: 100}\n\n[Bb]La [Eb]la [F7]la\n{key: C}\n[C]Lift\n", copy);
    }

    [Fact]
    public void DroppingTheArtistDoesntMoveTheSongsKeyTwice()
    {
        var copy = NewSong.Copy("{title: Song}\n{artist: Me}\n{key: G}\n\n[G]La\n", new NewSongDetails("Song", "", "A"));
        Assert.Equal("{title: Song}\n{key: A}\n\n[A]La\n", copy);
    }
}
