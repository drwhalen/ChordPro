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
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "{title: Not a song}");

        var songs = SongLibrary.Scan(_folder);

        Assert.Equal(["Apple", "untitled", "Zebra"], songs.Select(s => s.Title));
        Assert.Equal("Someone", songs[2].Artist);
    }

    [Theory]
    [InlineData("zeb", true)]
    [InlineData("some zeb", true)]
    [InlineData("b.cho", true)]
    [InlineData("apple", false)]
    public void MatchesSearchAgainstTitleArtistAndFileName(string search, bool expected) =>
        Assert.Equal(expected, new SongInfo("/x/b.cho", "Zebra", "Someone").Matches(search));

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
