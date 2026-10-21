using ChordPro.Core;
using ChordPro.Core.Scrolling;

namespace ChordPro.Core.Tests;

public class SongTimelineTests
{
    [Fact]
    public void ParsesTimingDirectivesOntoTheNextNonBlankLine()
    {
        var song = ChordProParser.Parse("""
            {x_bars: 8}

            {x_time: 1:05}
            [G] [C]
            plain line
            """);

        var lines = song.Sections.Single().Lines;
        Assert.Equal(8, lines[0].Bars);
        Assert.Equal(TimeSpan.FromSeconds(65), lines[0].Time);
        Assert.Null(lines[1].Bars);
        Assert.Null(lines[1].Time);
    }

    [Fact]
    public void ParsesSectionAttributes()
    {
        var song = ChordProParser.Parse("""
            {start_of_verse: label="Verse 1" bars="16"}
            la
            {end_of_verse}
            {start_of_chorus bars=8}
            la
            {end_of_chorus}
            """);

        Assert.Equal(("Verse 1", 16.0), (song.Sections[0].Label, song.Sections[0].Bars));
        Assert.Equal((null, 8.0), (song.Sections[1].Label, song.Sections[1].Bars));
    }

    [Fact]
    public void PlainSectionValueIsStillALabel() =>
        Assert.Equal("Verse 1", ChordProParser.Parse("{sov: Verse 1}\nla\n{eov}").Sections[0].Label);

    [Fact]
    public void DefaultsToOneBarPerChordTwoPerLyricLineAndNoneForComments()
    {
        var timeline = MusicalTimeline.Build(ChordProParser.Parse("""
            {tempo: 120}
            {c: Intro}
            [G] [C] [G] [D]
            [G]Line with [C]two chords
            No chords here

            """));

        Assert.Equal([0.0, 4, 2, 2], timeline.Lines.Select(l => l.Length));
        Assert.Equal([0.0, 0, 4, 6], timeline.Lines.Select(l => l.Start));
        Assert.Equal(8, timeline.TotalBars);
    }

    [Fact]
    public void ConvertsBarsToSecondsWithTempoAndTimeSignature()
    {
        var timeline = MusicalTimeline.Build(ChordProParser.Parse("{tempo: 90}\n{time: 3/4}\n{x_bars: 10}\nla"));

        Assert.Equal(3, timeline.BeatsPerBar);
        Assert.Equal(2, timeline.SecondsPerBar, precision: 6);      // 3 beats at 90 BPM
        Assert.Equal(20, timeline.TotalSeconds, precision: 6);
        Assert.True(timeline.TempoFromSong);
    }

    [Fact]
    public void AssumesDefaultTempoWhenMissing()
    {
        var timeline = MusicalTimeline.Build(ChordProParser.Parse("[G]la"));

        Assert.False(timeline.TempoFromSong);
        Assert.Equal(MusicalTimeline.DefaultTempo, timeline.Tempo);
    }

    [Fact]
    public void SharesSectionBarsAcrossLinesInProportion()
    {
        var timeline = MusicalTimeline.Build(ChordProParser.Parse("""
            {start_of_verse: bars="16"}
            [G]one [C]two [D]three
            [G]one
            {x_bars: 4}
            [Em]fixed
            {end_of_verse}
            """));

        // 4 bars are fixed; the remaining 12 split 3:1 by chord count.
        Assert.Equal([9.0, 3, 4], timeline.Lines.Select(l => l.Length));
    }

    [Fact]
    public void GivesATabBlockFourBarsOnItsFirstLine()
    {
        var timeline = MusicalTimeline.Build(ChordProParser.Parse("{sot}\ne|--0--|\nB|--1--|\n{eot}"));

        Assert.Equal([4.0, 0], timeline.Lines.Select(l => l.Length));
    }

    [Fact]
    public void FindsTheLineAtABar()
    {
        var timeline = MusicalTimeline.Build(ChordProParser.Parse("{c: Intro}\n[G] [C]\n[D]la [G]la"));

        Assert.Equal(1, timeline.LineAt(0)!.LineIndex);
        Assert.Equal(1, timeline.LineAt(1.9)!.LineIndex);
        Assert.Equal(2, timeline.LineAt(2)!.LineIndex);
    }

    [Fact]
    public void KeepsOnlyForwardMovingTimeMarkers()
    {
        var song = ChordProParser.Parse("""
            {x_time: 0:10}
            first
            {soc}
            {x_time: 0:30}
            [C]chorus
            {eoc}
            {x_time: 0:50}
            verse two
            {chorus}
            """);

        Assert.Equal([10.0, 30, 50], TimeMarkers.Find(song).Select(m => m.Time.TotalSeconds));
    }

    [Theory]
    [InlineData("3:45", 225)]
    [InlineData("1:02:30", 3750)]
    [InlineData("90", 90)]
    [InlineData("1:05.5", 65.5)]
    public void ParsesSongTimes(string text, double seconds) =>
        Assert.Equal(seconds, SongTime.Parse(text)!.Value.TotalSeconds, precision: 6);

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("1:2:3:4")]
    public void RejectsInvalidSongTimes(string text) => Assert.Null(SongTime.Parse(text));
}
