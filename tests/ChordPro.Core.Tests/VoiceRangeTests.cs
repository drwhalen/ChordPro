using ChordPro.Core;

namespace ChordPro.Core.Tests;

public class VoiceRangeTests
{
    [Fact]
    public void KeysNearTheComfortableOnesFitBest()
    {
        var voice = new VoiceRange(["G", "Am"]);

        Assert.Equal(1, voice.Fit("G"), 3);
        Assert.Equal(1, voice.Fit("Gm"), 3);              // the tonic counts, not major or minor
        Assert.True(voice.Fit("A") > voice.Fit("C#"));    // A is a comfortable tonic; C# is far from both
        Assert.Equal(0.6, new VoiceRange([]).Fit("C#"));  // nothing to go on
    }

    [Theory]
    [InlineData("G", null)]      // comfortable already
    [InlineData("Ab", null)]     // a semitone off is close enough
    [InlineData("C", -3)]        // down three to A (G would be five away)
    [InlineData("E", 3)]         // up three to G (A would be five away)
    public void SuggestsTheSmallestMoveToAComfortableKey(string key, int? expected)
    {
        var voice = new VoiceRange(["G", "A"]);
        var shift = voice.SuggestShift(key);
        if (expected is null)
        {
            Assert.Null(shift);
            return;
        }
        Assert.NotNull(shift);
        var moved = ChordTransposer.Shift(key, shift!.Value)!;
        Assert.True(new VoiceRange(["G", "A"]).Fit(moved) > 0.8, $"{key} {shift:+0;-0} → {moved}");
        Assert.True(Math.Abs(shift.Value) <= Math.Abs(expected.Value));
    }

    [Fact]
    public void ThePlannerPrefersKeysNearTheSingersVoice()
    {
        PlannedSong Song(string title, string key) => new(new SongInfo($"/{title}", title, title + " Band", key), key, 100, 0);
        PlannedSong[] pool = [Song("In my key", "D"), Song("Far off", "Ab"), Song("Also far", "Bb")];

        var plan = SetlistPlanner.Plan(new SetlistRequest(pool, [], Length: 1, Voice: new VoiceRange(["D"])));

        Assert.Equal("In my key", plan.Songs.Single().Song.Title);
    }
}
