using ChordPro.Core;

namespace ChordPro.Core.Tests;

public class SetlistPlannerTests
{
    private static PlannedSong Song(string title, string? key, double? tempo = null, int capo = 0, string? artist = null, int plays = 0) =>
        new(new SongInfo($"/songs/{title}.txt", title, artist ?? title + " Band", key, capo), key, tempo, capo, plays);

    [Theory]
    [InlineData("G", "G", "same key")]
    [InlineData("C", "Am", "relative minor")]
    [InlineData("Em", "G", "relative major")]
    [InlineData("G", "D", "up a fifth")]
    [InlineData("D", "G", "up a fourth")]
    [InlineData("G", "A", "up a whole step, a lift")]
    [InlineData("G", "C#", "a fresh start")]
    public void ExplainsHowKeysLeadIntoEachOther(string from, string to, string expected) =>
        Assert.Contains(expected, SetlistPlanner.Step(Song("a", from), Song("b", to)).Key);

    [Fact]
    public void ScoresCloseKeysAndSteadyTemposAboveJumps()
    {
        var smooth = SetlistPlanner.Step(Song("a", "G", 100), Song("b", "D", 104));
        var jump = SetlistPlanner.Step(Song("a", "G", 100), Song("b", "C#", 150));
        var capoMove = SetlistPlanner.Step(Song("a", "G", 100, capo: 0), Song("b", "D", 104, capo: 3));

        Assert.True(smooth.Score > capoMove.Score && capoMove.Score > jump.Score);
        Assert.Equal("good", smooth.Rating);
        Assert.Equal("bump", jump.Rating);
        Assert.Equal("G → D: up a fifth, a close key · 100 → 104 BPM, steady", smooth.ToString());
        Assert.Equal("capo off → 3", capoMove.Capo);
    }

    [Fact]
    public void ThePlanKeepsThePickedSongsAndLeavesOutTheMisfits()
    {
        PlannedSong[] pool =
        [
            Song("One", "G", 96), Song("Two", "D", 100), Song("Three", "Em", 92), Song("Four", "C", 104),
            Song("Five", "A", 108), Song("Odd", "C#", 180), Song("Six", "Am", 88), Song("Seven", "G", 120),
        ];
        var picked = new[] { pool[0], pool[4] };

        var plan = SetlistPlanner.Plan(new SetlistRequest(pool, picked, Length: 6));

        Assert.Equal(6, plan.Songs.Count);
        Assert.Equal(6, plan.Songs.Select(s => s.Song.Path).Distinct().Count());
        Assert.Contains(pool[0], plan.Songs);
        Assert.Contains(pool[4], plan.Songs);
        Assert.DoesNotContain(plan.Songs, s => s.Song.Title == "Odd");   // far key and a tempo lurch
        Assert.Equal(5, plan.Steps.Count);
    }

    [Fact]
    public void KeepsTheSameArtistApart()
    {
        PlannedSong[] pool =
        [
            Song("A1", "G", 100, artist: "Oasis"), Song("A2", "G", 100, artist: "Oasis"),
            Song("B1", "G", 100, artist: "Blur"), Song("B2", "G", 100, artist: "Blur"),
        ];

        var plan = SetlistPlanner.Plan(new SetlistRequest(pool, [], Length: 4));

        Assert.All(plan.Songs.Zip(plan.Songs.Skip(1)), pair => Assert.NotEqual(pair.First.Song.Artist, pair.Second.Song.Artist));
    }

    [Fact]
    public void PrefersKeysNearThePickedSongs()
    {
        PlannedSong[] pool = [Song("Picked", "G", 100), Song("Near", "A", 100), Song("Far", "C#", 100), Song("Also far", "Eb", 100)];

        var plan = SetlistPlanner.Plan(new SetlistRequest(pool, [pool[0]], Length: 2));

        Assert.Equal(["Near", "Picked"], plan.Songs.Select(s => s.Song.Title).Order());
    }

    [Fact]
    public void AvoidsLongRunsInOneKey()
    {
        var pool = Enumerable.Range(0, 6).Select(i => Song($"C{i}", "C", 100)).Concat(Enumerable.Range(0, 6).Select(i => Song($"G{i}", "G", 100))).ToList();

        var keys = SetlistPlanner.Plan(new SetlistRequest(pool, [], Length: 8)).Songs.Select(s => s.Key).ToList();

        Assert.DoesNotContain(Enumerable.Range(2, keys.Count - 2), i => keys[i] == keys[i - 1] && keys[i - 1] == keys[i - 2]);
    }

    [Fact]
    public void PrefersSongsFromThePickedSongsCollections()
    {
        PlannedSong Booked(string title, string book) =>
            new(new SongInfo($"/songs/{title}.txt", title, title + " Band", "G", 0, [book]), "G", 100, 0);
        PlannedSong[] pool = [Booked("Picked", "Camp Fire"), Booked("Fire song", "Camp Fire"), Booked("Carol", "Christmas Songs")];

        var plan = SetlistPlanner.Plan(new SetlistRequest(pool, [pool[0]], Length: 2));

        Assert.Equal(["Fire song", "Picked"], plan.Songs.Select(s => s.Song.Title).Order());
    }

    [Fact]
    public void AnotherVariationGivesAnotherSetWithThePickedSongs()
    {
        var pool = Enumerable.Range(0, 30).Select(i => Song($"S{i}", new[] { "G", "D", "C", "Em", "A", "Am" }[i % 6], 80 + i * 3)).ToList();

        var first = SetlistPlanner.Plan(new SetlistRequest(pool, [pool[3]], Length: 8));
        var other = SetlistPlanner.Plan(new SetlistRequest(pool, [pool[3]], Length: 8, Variation: 7));

        Assert.Contains(pool[3], other.Songs);
        Assert.NotEqual(first.Songs.Select(s => s.Song.Title), other.Songs.Select(s => s.Song.Title));
    }

    [Fact]
    public void DescribesASetInTheOrderGiven()
    {
        var plan = SetlistPlanner.Describe([Song("One", "G", 90), Song("Two", "Em", 84), Song("Three", "Bb", 140)]);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Contains("relative minor", plan.Steps[0].Key);
        Assert.Equal("bump", plan.Steps[1].Rating);
    }
}
