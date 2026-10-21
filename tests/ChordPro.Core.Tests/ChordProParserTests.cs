using ChordPro.Core;

namespace ChordPro.Core.Tests;

public class ChordProParserTests
{
    [Fact]
    public void ParsesMetadataIncludingShortAliases()
    {
        var song = ChordProParser.Parse("""
            {t: Amazing Grace}
            {st: Traditional}
            {artist: John Newton}
            {key: G}
            {capo 2}
            {meta: tempo 90}
            """);

        Assert.Equal("Amazing Grace", song.Title);
        Assert.Equal("Traditional", song.Subtitle);
        Assert.Equal("John Newton", song.Artist);
        Assert.Equal("G", song.Key);
        Assert.Equal("2", song.Capo);
        Assert.Equal("90", song.Tempo);
        Assert.Empty(song.Sections);
    }

    [Fact]
    public void SplitsLyricLineIntoChordSegments()
    {
        var line = ChordProParser.ParseLyricLine("A[G]mazing [G7]grace, how [C]sweet[G]");

        Assert.Equal(
            [
                new ChordSegment(null, "A"),
                new ChordSegment("G", "mazing "),
                new ChordSegment("G7", "grace, how "),
                new ChordSegment("C", "sweet"),
                new ChordSegment("G", ""),
            ],
            line.Segments);
        Assert.True(line.HasChords);
    }

    [Fact]
    public void LineWithoutChordsIsSingleSegment()
    {
        var line = ChordProParser.ParseLyricLine("No chords here");

        Assert.Equal([new ChordSegment(null, "No chords here")], line.Segments);
        Assert.False(line.HasChords);
    }

    [Fact]
    public void GroupsLinesIntoSections()
    {
        var song = ChordProParser.Parse("""
            {start_of_verse: Verse 1}
            [G]Line one
            {end_of_verse}

            {soc}
            [C]Chorus line
            {eoc}
            """);

        Assert.Collection(song.Sections,
            verse =>
            {
                Assert.Equal(SectionKind.Verse, verse.Kind);
                Assert.Equal("Verse 1", verse.Label);
                Assert.Single(verse.Lines);
            },
            chorus =>
            {
                Assert.Equal(SectionKind.Chorus, chorus.Kind);
                Assert.Null(chorus.Label);
                Assert.Single(chorus.Lines);
            });
    }

    [Fact]
    public void ParsesLabelAttribute()
    {
        var song = ChordProParser.Parse("""
            {start_of_bridge label="Middle 8"}
            la la
            {end_of_bridge}
            """);

        Assert.Equal("Middle 8", Assert.Single(song.Sections).Label);
    }

    [Fact]
    public void ChorusDirectiveRepeatsLastChorus()
    {
        var song = ChordProParser.Parse("""
            {soc}
            [C]Sing it
            {eoc}
            Verse
            {chorus}
            """);

        Assert.Equal(3, song.Sections.Count);
        Assert.Equal(SectionKind.Chorus, song.Sections[2].Kind);
        Assert.Equal(song.Sections[0].Lines, song.Sections[2].Lines);
    }

    [Fact]
    public void TabSectionsArePreformattedAndKeepBrackets()
    {
        var song = ChordProParser.Parse("""
            {sot}
            e|--[0]--3--|
            {eot}
            """);

        var section = Assert.Single(song.Sections);
        Assert.Equal(SectionKind.Tab, section.Kind);
        Assert.Equal(new PreformattedLine("e|--[0]--3--|"), Assert.Single(section.Lines));
    }

    [Fact]
    public void ParsesCommentsAndSkipsHashLines()
    {
        var song = ChordProParser.Parse("""
            # file comment, not shown
            {c: Intro}
            {ci: softly}
            {cb: Coda}
            {define: G base-fret 1 frets 3 2 0 0 0 3}
            """);

        Assert.Equal(
            [
                new CommentLine("Intro", CommentStyle.Normal),
                new CommentLine("softly", CommentStyle.Italic),
                new CommentLine("Coda", CommentStyle.Box),
            ],
            Assert.Single(song.Sections).Lines);
    }

    [Fact]
    public void TrimsLeadingAndTrailingBlankLinesButKeepsInnerOnes()
    {
        var song = ChordProParser.Parse("\r\n\r\nOne\r\n\r\nTwo\r\n\r\n");

        Assert.Equal(
            [ChordProParser.ParseLyricLine("One"), new BlankLine(), ChordProParser.ParseLyricLine("Two")],
            Assert.Single(song.Sections).Lines);
    }
}
