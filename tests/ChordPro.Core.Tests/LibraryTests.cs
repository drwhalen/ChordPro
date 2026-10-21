using ChordPro.Core;
using ChordPro.Core.Chords;

namespace ChordPro.Core.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("chordpro-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ParsesChordDefinitionsRelativeToBaseFret()
    {
        var song = ChordProParser.Parse("""
            {define: Am base-fret 1 frets x 0 2 2 1 0 fingers 0 0 2 3 1 0}
            {define: Bb base-fret 6 frets 1 1 3 3 3 1}
            {define: broken}
            """);

        Assert.Equal("x02210", song.ChordDefinitions["Am"].ToString());
        Assert.Equal(new[] { 6, 6, 8, 8, 8, 6 }, song.ChordDefinitions["Bb"].Frets);
        Assert.Equal(2, song.ChordDefinitions.Count);
    }

    [Fact]
    public void ScansFolderRecursivelyAndSortsByTitle()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "b.cho"), "{title: Zebra}\n{artist: Someone}");
        File.WriteAllText(Path.Combine(_folder, "sub", "a.chordpro"), "{t: Apple}");
        File.WriteAllText(Path.Combine(_folder, "untitled.pro"), "[C]la");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "Bring a spare capo.\nTune down [maybe] for the encore.");

        var songs = SongLibrary.Scan(_folder);

        Assert.Equal(["Apple", "untitled", "Zebra"], songs.Select(s => s.Title));
        Assert.Equal("Someone", songs[2].Artist);
    }

    [Theory]
    [InlineData("{title: Text Song}\nla la", true)]
    [InlineData("  {soc}\n[G]la", true)]
    [InlineData("Some words [C]here and [F#m7]there", true)]
    [InlineData("Just a note\nwith {braces} mid-line and [brackets]", false)]
    [InlineData("", false)]
    public void IncludesTextFilesOnlyWhenTheyLookLikeChordPro(string text, bool expected)
    {
        var path = Path.Combine(_folder, "song.txt");
        File.WriteAllText(path, text);

        Assert.Equal(expected, SongLibrary.IsLibrarySong(path));
    }

    [Fact]
    public void ScanListsChordProTextFiles()
    {
        File.WriteAllText(Path.Combine(_folder, "song.TXT"), "{title: Text Song}\n[C]la");
        File.WriteAllText(Path.Combine(_folder, "readme.txt"), "Songs for Friday.");

        Assert.Equal(["Text Song"], SongLibrary.Scan(_folder).Select(s => s.Title));
    }

    [Theory]
    [InlineData("zeb", true)]
    [InlineData("some zeb", true)]
    [InlineData("b.cho", true)]
    [InlineData("apple", false)]
    public void MatchesSearchAgainstTitleArtistAndFileName(string search, bool expected) =>
        Assert.Equal(expected, new SongInfo("/x/b.cho", "Zebra", "Someone").Matches(search));

    [Fact]
    public void GroupsByArtistIgnoringCaseAndLeadingThe()
    {
        SongInfo[] songs =
        [
            new("/a.cho", "Across the Universe", "The Beatles"),
            new("/b.cho", "Amazing Grace", null),
            new("/c.cho", "Badge", "Cream"),
            new("/d.cho", "Bike", "  "),
            new("/e.cho", "Help", "the beatles"),
            new("/f.cho", "Alright Now", "Free"),
        ];

        var groups = SongLibrary.Group(songs, LibraryGrouping.Artist);

        Assert.Equal(["The Beatles", "Cream", "Free", SongGroup.UnknownArtist], groups.Select(g => g.Name));
        Assert.Equal(["Across the Universe", "Help"], groups[0].Songs.Select(s => s.Title));
        Assert.Equal(["Amazing Grace", "Bike"], groups[3].Songs.Select(s => s.Title));
    }

    [Fact]
    public void GroupsByBookListingASongUnderEachOfItsBooks()
    {
        SongInfo[] songs =
        [
            new("/a.cho", "Amazing Grace", null, Books: ["Christian Songs", "Camp Fire"]),
            new("/b.cho", "Badge", null),
            new("/c.cho", "Kumbaya", null, Books: ["camp fire"]),
        ];

        var groups = SongLibrary.Group(songs, LibraryGrouping.Book);

        Assert.Equal(["Camp Fire", "Christian Songs", SongGroup.NoBook], groups.Select(g => g.Name));
        Assert.Equal(["Amazing Grace", "Kumbaya"], groups[0].Songs.Select(s => s.Title));
        Assert.Equal(["Badge"], groups[2].Songs.Select(s => s.Title));
    }

    [Fact]
    public void FiltersByKeyAndCapo()
    {
        SongInfo[] songs =
        [
            new("/a.cho", "One", null, "G"),
            new("/b.cho", "Two", null, "G", Capo: 2),
            new("/c.cho", "Three", null, "Em"),
            new("/d.cho", "Four", null),
        ];

        Assert.Equal(["One", "Two"], SongLibrary.Filter(songs, "", key: "g").Select(s => s.Title));
        Assert.Equal(["One", "Three", "Four"], SongLibrary.Filter(songs, null, capo: 0).Select(s => s.Title));
        Assert.Equal(["Two"], SongLibrary.Filter(songs, "tw", "G", 2).Select(s => s.Title));
    }

    [Fact]
    public void ListsKeysInPitchOrderAndCapos()
    {
        SongInfo[] songs =
        [
            new("/a", "", null, "G"), new("/b", "", null, "Em"), new("/c", "", null, "C"),
            new("/d", "", null, "E", Capo: 3), new("/e", "", null, "Db"), new("/f", "", null, "g"),
        ];

        Assert.Equal(["C", "Db", "E", "Em", "G"], SongLibrary.Keys(songs));
        Assert.Equal([0, 3], SongLibrary.Capos(songs));
    }

    [Fact]
    public void ReadsKeyCapoAndBooksFromTheFile()
    {
        var path = Path.Combine(_folder, "song.cho");
        File.WriteAllText(path, "{title: T}\n{key: D}\n{capo: 2}\n{book: Camp Fire}");

        var info = SongLibrary.ReadInfo(path)!;

        Assert.Equal(("D", 2), (info.Key, info.Capo));
        Assert.Equal(["Camp Fire"], info.Books);
    }

    [Theory]
    [InlineData("The Beatles", "Beatles")]
    [InlineData("Theory of a Deadman", "Theory of a Deadman")]
    [InlineData("Neil Young", "Neil Young")]
    public void ArtistSortKeyDropsLeadingThe(string artist, string expected) =>
        Assert.Equal(expected, SongLibrary.ArtistSortKey(artist));

    [Fact]
    public void SetlistRoundTripsWithRelativePaths()
    {
        var song1 = Path.Combine(_folder, "songs", "one.cho");
        var song2 = Path.Combine(_folder, "two.cho");
        var file = Path.Combine(_folder, "gig" + Setlist.FileExtension);

        var setlist = new Setlist();
        setlist.Songs.AddRange([song1, song2]);
        setlist.Save(file);

        Assert.Equal([Path.Combine("songs", "one.cho"), "two.cho"], File.ReadAllLines(file));
        Assert.Equal([song1, song2], Setlist.Load(file).Songs);
    }

    [Fact]
    public void LoadsLivePrompterSetlistBySongName()
    {
        // LivePrompter keeps setlists in a Setlists folder inside the songs folder.
        var setlists = Directory.CreateDirectory(Path.Combine(_folder, "Setlists")).FullName;
        var amazing = Path.Combine(_folder, "Amazing Grace - John Newton (G).txt");
        var tambourine = Path.Combine(_folder, "Mr. Tambourine Man - Bob Dylan (D).txt");
        var elsewhere = Path.Combine(_folder, "other", "Badge - Cream (Am).cho");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        foreach (var song in new[] { amazing, tambourine, elsewhere })
            File.WriteAllText(song, "{title: x}");
        var file = Path.Combine(setlists, "Gig.txt");
        File.WriteAllLines(file, ["Amazing Grace - John Newton (G)", "Mr. Tambourine Man - Bob Dylan (D)",
            "Badge - Cream (Am)", "Gone - Nobody (C)"]);

        Assert.True(Setlist.IsLivePrompterSetlist(file));
        Assert.False(Setlist.IsLivePrompterSetlist(amazing));
        var setlist = Setlist.Load(file, [elsewhere]);

        Assert.Equal([amazing, tambourine, elsewhere, Path.Combine(_folder, "Gone - Nobody (C).txt")], setlist.Songs);

        // Saving keeps LivePrompter's format, including the song that wasn't found.
        setlist.Save(file);
        Assert.Equal(["Amazing Grace - John Newton (G)", "Mr. Tambourine Man - Bob Dylan (D)",
            "Badge - Cream (Am)", "Gone - Nobody (C)"], File.ReadAllLines(file));
    }

    [Fact]
    public void SetlistIgnoresCommentsAndBlankLines()
    {
        var file = Path.Combine(_folder, "list" + Setlist.FileExtension);
        File.WriteAllLines(file, ["# Friday gig", "", "one.cho"]);

        Assert.Equal([Path.Combine(_folder, "one.cho")], Setlist.Load(file).Songs);
    }
}
