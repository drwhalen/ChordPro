using ChordPro.Core;

namespace ChordPro.Core.Tests;

public sealed class SetlistTests
{
    private static Setlist Make(params string[] entries)
    {
        var setlist = new Setlist();
        setlist.Songs.AddRange(entries);
        return setlist;
    }

    [Fact]
    public void StepsOverSetDividersAndNamesTheSetReached()
    {
        var setlist = Make("--- Set 1 ---", "a.cho", "b.cho", "--- Set 2 ---", "c.cho");

        Assert.Equal((2, (string?)null), setlist.Step(1, +1));
        Assert.Equal((4, "Set 2"), setlist.Step(2, +1));
        Assert.Equal((2, "Set 2"), setlist.Step(4, -1));
        Assert.Null(setlist.Step(4, +1));
        Assert.Null(setlist.Step(1, -1));
    }

    [Fact]
    public void StartsFromEitherEndWhenNoSongIsOpen()
    {
        var setlist = Make("--- Set 1 ---", "a.cho", "b.cho", "--- Encore ---");

        Assert.Equal((1, "Set 1"), setlist.Step(-1, +1));
        Assert.Equal((2, "Encore"), setlist.Step(-1, -1));
        Assert.Null(Make().Step(-1, +1));
    }

    [Fact]
    public void PassesOverSongsThatCannotBePlayed()
    {
        var setlist = Make("a.cho", "--- Set 2 ---", "missing.cho", "c.cho", "missing.cho");
        bool Playable(string path) => path != "missing.cho";

        Assert.Equal((3, "Set 2"), setlist.Step(0, +1, Playable));
        Assert.Null(setlist.Step(3, +1, Playable));
        Assert.Equal((0, "Set 2"), setlist.Step(3, -1, Playable));
    }
}
