namespace ChordPro.Core.Tests;

public class GigTests
{
    private static readonly DateTime Eight = new(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);

    private static Gig TwoSets() => Gig.Start("Friday", "Friday.setlist",
        ["--- Set 1 ---", "a.cho", "b.cho", "c.cho", "--- Set 2 ---", "d.cho"], Eight);

    [Fact]
    public void ASongCountsAsPlayedOnlyOnceShownLongEnough()
    {
        var gig = TwoSets();
        Assert.Null(gig.Show(1, Eight));
        // Opened b by mistake for a few seconds: a isn't played, and b isn't either.
        Assert.Equal("a.cho", gig.Show(2, Eight.AddMinutes(3)));
        Assert.Null(gig.Show(3, Eight.AddMinutes(3).AddSeconds(5)));
        Assert.Null(gig.Update(Eight.AddMinutes(3).AddSeconds(20)));
        Assert.Equal("c.cho", gig.Update(Eight.AddMinutes(4)));
        Assert.Null(gig.Update(Eight.AddMinutes(5))); // counted once
        Assert.Equal(GigSongState.Upcoming, gig.Entries[2].State);
        Assert.Equal(["a.cho", "c.cho"], gig.Played.Select(e => e.Song));
    }

    [Fact]
    public void TimeAwayFromASongStillAddsUp()
    {
        var gig = TwoSets();
        gig.Show(1, Eight);
        Assert.Null(gig.Leave(Eight.AddSeconds(20))); // looking at the list
        Assert.Null(gig.Show(1, Eight.AddMinutes(1))); // back to it
        Assert.Equal("a.cho", gig.Update(Eight.AddMinutes(1).AddSeconds(15))); // 20 + 15 seconds
    }

    [Fact]
    public void SteppingPassesSkippedSongsAndDividers()
    {
        var gig = TwoSets();
        gig.ToggleSkip(2);
        Assert.Equal((3, null), gig.Step(1, +1));
        Assert.Equal((5, "Set 2"), gig.Step(3, +1));
        Assert.Equal((1, null), gig.Step(3, -1));
        gig.ToggleSkip(2);
        Assert.Equal((2, null), gig.Step(1, +1));
    }

    [Fact]
    public void ARequestComesNextOrAtTheEnd()
    {
        var gig = TwoSets();
        Assert.Equal(1, gig.Request("x.cho", atEnd: false)); // before the first song, nothing played yet
        gig.Show(2, Eight);
        Assert.Equal(3, gig.Request("y.cho", atEnd: false));
        Assert.Equal(8, gig.Request("encore.cho", atEnd: true));
        Assert.Equal(["--- Set 1 ---", "x.cho", "a.cho", "y.cho", "b.cho", "c.cho", "--- Set 2 ---", "d.cho", "encore.cho"], gig.Entries.Select(e => e.Song));
        Assert.True(gig.Entries[3].Requested);
        Assert.Equal(2, gig.Current);
    }

    [Fact]
    public void MovingKeepsTheCurrentSong()
    {
        var gig = TwoSets();
        gig.Show(2, Eight);
        gig.Move(3, -1);
        Assert.Equal(["a.cho", "c.cho", "b.cho"], gig.Entries.Skip(1).Take(3).Select(e => e.Song));
        Assert.Equal(3, gig.Current);
        gig.Move(0, -1); // nowhere to go
        Assert.Equal("--- Set 1 ---", gig.Entries[0].Song);
    }

    [Fact]
    public void TheSetClockProjectsTheEndFromWhatIsLeft()
    {
        var gig = TwoSets();
        gig.Request("req.cho", atEnd: false);
        TimeSpan? Length(string song) => song switch { "a.cho" => TimeSpan.FromMinutes(4), "b.cho" => TimeSpan.FromMinutes(3), "c.cho" => TimeSpan.FromMinutes(5), "req.cho" => TimeSpan.FromMinutes(2), _ => null };

        // Plan: a + b + c = 12 min (the request isn't in the plan). a ran long: 6 minutes, now 1 minute into the request.
        gig.Show(2, Eight.AddMinutes(1)); // a
        gig.Show(1, Eight.AddMinutes(7)); // req
        var clock = gig.Clock(Eight.AddMinutes(8), Length);
        Assert.Equal("Set 1", clock.Set);
        Assert.Equal(TimeSpan.FromMinutes(12), clock.Planned);
        Assert.Equal(TimeSpan.FromMinutes(7), clock.SetElapsed);
        // 7 so far + 1 left of the request + b 3 + c 5 = 16, so 4 over.
        Assert.Equal(TimeSpan.FromMinutes(16), clock.Projected);
        Assert.Equal(TimeSpan.FromMinutes(4), clock.Over);
        Assert.Equal(TimeSpan.FromMinutes(8), clock.GigElapsed);

        // A skipped song comes off what's left.
        gig.ToggleSkip(4);
        Assert.Equal(TimeSpan.FromMinutes(-1), gig.Clock(Eight.AddMinutes(8), Length).Over);
    }

    [Fact]
    public void SavesAndLoadsAGigInProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gig-{Guid.NewGuid():N}.json");
        try
        {
            var gig = TwoSets();
            gig.Show(1, Eight);
            gig.ToggleSkip(2);
            gig.Save(path);
            var loaded = Gig.Load(path)!;
            Assert.Equal(1, loaded.Current);
            Assert.Equal(Eight, loaded.CurrentSince);
            Assert.Equal(GigSongState.Skipped, loaded.Entries[2].State);
            Assert.Equal("Friday.setlist", loaded.Setlist);
            Assert.Contains("\"skipped\"", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
