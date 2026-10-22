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
    public void PlainLinesEndingInAColonStartLabeledSections()
    {
        var song = ChordProParser.Parse("""
            {title: Heads}
            Chords:
            {chord: Dm7 base-fret 1 frets x x 0 2 1 1}

            Intro:
            [G] [D]

            Verse 1:
            [G]Words and he said: [D]this
            And then he said:
            Chorus:
            {start_of_chorus}
            [C]Sing
            {end_of_chorus}
            """);

        // "Chords:" holds only a fingering, so it leaves no section behind. A short chordless line
        // ending in a colon is a heading, as in LivePrompter, and is kept even with nothing under it.
        Assert.Equal(["Intro", "Verse 1", "And then he said", "Chorus"], song.Sections.Select(s => s.Label));
        Assert.Empty(song.Sections[2].Lines);
        Assert.Equal(SectionKind.Chorus, song.Sections[3].Kind);
        // A colon mid-line, or at the end of a line with chords, is still a lyric.
        Assert.Single(song.Sections[1].Lines);
        Assert.Single(song.ChordDefinitions);
    }

    [Fact]
    public void ColonLinesInsideASectionStayLyrics()
    {
        var song = ChordProParser.Parse("""
            {start_of_verse}
            He said:
            Words
            {end_of_verse}
            """);

        var section = Assert.Single(song.Sections);
        Assert.Equal(2, section.Lines.Count);
    }

    [Fact]
    public void ReadsEveryBook()
    {
        var song = ChordProParser.Parse("{book: Camp Fire}\n{book: Christian Songs}\n{capo: 3}");

        Assert.Equal(["Camp Fire", "Christian Songs"], song.GetMetaValues("book"));
        Assert.Equal(3, song.CapoFret);
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
