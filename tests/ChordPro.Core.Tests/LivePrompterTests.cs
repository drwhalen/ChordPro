using ChordPro.Core;
using ChordPro.Core.Layout;
using ChordPro.Core.Scrolling;

namespace ChordPro.Core.Tests;

public sealed class LivePrompterTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("chordpro-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void MeasuresInArialEm() =>
        // Arial: "W" is 0.944 em, "i" 0.222, a space 0.278.
        Assert.Equal(1.444, LivePrompterWidth.Em("W i"), 3);

    [Theory]
    [InlineData("[G]Short [C]line", null)]
    [InlineData("{title: A very long title that LivePrompter shows in its own header anyway}", null)]
    [InlineData("Intro:", null)]
    public void LinesThatFitAreNotCut(string line, string? expected) =>
        Assert.Equal(expected, LivePrompterWidth.CutOff(line));

    [Fact]
    public void LongLyricIsCutWhereTheBudgetRunsOut()
    {
        // 40 "m"s are 33.3 em; 33 of them (27.5 em) fit in 28 em.
        var line = "[C]" + new string('m', 40);

        Assert.Equal(new string('m', 33), LivePrompterWidth.CutOff(line));
    }

    [Fact]
    public void MeasuresChordLinesAndCommentsAsShown()
    {
        Assert.Equal("Am  G  F", LivePrompterWidth.ShownText("[Am]  [G]  [F]"));
        Assert.Equal("Words here", LivePrompterWidth.ShownText("[Am]Words [G]here"));
        Assert.Equal("Repeat and fade", LivePrompterWidth.ShownText("{comment: Repeat and fade}"));
        Assert.Null(LivePrompterWidth.ShownText("{key: G}"));
    }

    [Fact]
    public void CheckReportsLinesLivePrompterWouldCutOff()
    {
        var text = "{title: T}\n[C]Fine\n{comment: " + new string('w', 50) + "}\n";

        var problem = Assert.Single(LibraryCheck.CheckLineWidths(text));
        Assert.StartsWith("Line 3 is too wide for LivePrompter", problem);
    }

    [Fact]
    public void TapTempoAveragesTheBeatAndStartsAgainAfterAPause()
    {
        var taps = new TapTempo();
        Assert.Null(taps.Tap(TimeSpan.FromSeconds(10)));
        Assert.Equal(120, taps.Tap(TimeSpan.FromSeconds(10.5))!.Value, 3);
        Assert.Equal(100, taps.Tap(TimeSpan.FromSeconds(11.2))!.Value, 3); // 1.2 s over two beats

        // A long pause: this tap starts a new count.
        Assert.Null(taps.Tap(TimeSpan.FromSeconds(20)));
        Assert.Equal(1, taps.Count);
    }

    [Fact]
    public void TapTempoKeepsOnlyTheRecentTaps()
    {
        var taps = new TapTempo();
        double? tempo = null;
        // Eight slow taps, then eight at 120 BPM: only the recent ones count.
        for (var i = 0; i < 8; i++)
            taps.Tap(TimeSpan.FromSeconds(i * 1.0));
        for (var i = 1; i <= 8; i++)
            tempo = taps.Tap(TimeSpan.FromSeconds(7 + i * 0.5));

        Assert.Equal(120, tempo!.Value, 3);
    }

    [Fact]
    public void GuessesWhetherKeysIncludeTheCapo()
    {
        // Played as C shapes with capo 3 (sounds Eb), starting and ending on C.
        var heard = Write("heard.txt", "{title: A}\n{key: Eb}\n{capo: 3}\n[C]la [F]la [G]la [C]la");
        var written = Write("written.cho", "{title: B}\n{key: C}\n{capo: 3}\n[C]la [F]la [G]la [C]la");
        var noCapo = Write("plain.cho", "{title: C}\n{key: G}\n[G]la");

        Assert.True(SongLibrary.GuessKeyIncludesCapo([SongLibrary.ReadInfo(heard)!, SongLibrary.ReadInfo(noCapo)!]));
        Assert.False(SongLibrary.GuessKeyIncludesCapo([SongLibrary.ReadInfo(written)!]));
        Assert.Null(SongLibrary.GuessKeyIncludesCapo([SongLibrary.ReadInfo(noCapo)!]));
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
