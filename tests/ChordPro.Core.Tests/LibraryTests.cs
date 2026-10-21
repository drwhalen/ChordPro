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

        var groups = SongLibrary.GroupByArtist(songs);

        Assert.Equal(["The Beatles", "Cream", "Free", ArtistGroup.UnknownArtist], groups.Select(g => g.Artist));
        Assert.Equal(["Across the Universe", "Help"], groups[0].Songs.Select(s => s.Title));
        Assert.Equal(["Amazing Grace", "Bike"], groups[3].Songs.Select(s => s.Title));
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
    public void SetlistIgnoresCommentsAndBlankLines()
    {
        var file = Path.Combine(_folder, "list" + Setlist.FileExtension);
        File.WriteAllLines(file, ["# Friday gig", "", "one.cho"]);

        Assert.Equal([Path.Combine(_folder, "one.cho")], Setlist.Load(file).Songs);
    }
}
