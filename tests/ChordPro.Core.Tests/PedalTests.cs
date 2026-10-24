namespace ChordPro.Core.Tests;

public class PedalTests
{
    [Theory]
    [InlineData(PedalButton.Right, PedalLayout.PagesThenSongs, PedalMove.PageThenSong, 1)]
    [InlineData(PedalButton.PageUp, PedalLayout.PagesThenSongs, PedalMove.PageThenSong, -1)]
    [InlineData(PedalButton.Down, PedalLayout.Songs, PedalMove.Song, 1)]
    [InlineData(PedalButton.Left, PedalLayout.ArrowsChangeSongs, PedalMove.Song, -1)]
    [InlineData(PedalButton.Down, PedalLayout.ArrowsChangeSongs, PedalMove.Page, 1)]
    [InlineData(PedalButton.PageUp, PedalLayout.ArrowsChangeSongs, PedalMove.Page, -1)]
    public void APressDoesWhatTheLayoutSays(PedalButton button, PedalLayout layout, PedalMove move, int direction) =>
        Assert.Equal((move, direction), Pedal.Action(button, held: false, layout, PedalHold.ChangeSong));

    [Fact]
    public void HoldingAPedalDoesItsOwnThing()
    {
        Assert.Equal((PedalMove.Song, 1), Pedal.Action(PedalButton.Right, held: true, PedalLayout.PagesThenSongs, PedalHold.ChangeSong));
        Assert.Equal(PedalMove.ToggleScrolling, Pedal.Action(PedalButton.Left, held: true, PedalLayout.Songs, PedalHold.ToggleScrolling).Move);
        // With nothing set for holding, a hold is just a press.
        Assert.Equal((PedalMove.Page, -1), Pedal.Action(PedalButton.Up, held: true, PedalLayout.ArrowsChangeSongs, PedalHold.Nothing));
    }
}
