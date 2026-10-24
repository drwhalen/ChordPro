namespace ChordPro.Core;

/// <summary>The keys page-turn pedals send (they connect as keyboards).</summary>
public enum PedalButton { Left, Right, Up, Down, PageUp, PageDown }

/// <summary>What a pedal's presses do.</summary>
public enum PedalLayout
{
    /// <summary>Turn pages; at either end of a song, move to the next or previous song (for two-button pedals).</summary>
    PagesThenSongs,
    /// <summary>Every press changes the song.</summary>
    Songs,
    /// <summary>← → change songs; ↑ ↓ and Page Up/Down turn pages (for four-button pedals).</summary>
    ArrowsChangeSongs,
}

/// <summary>What holding a pedal down does.</summary>
public enum PedalHold { Nothing, ChangeSong, ToggleScrolling }

/// <summary>What a pedal press does to the stage.</summary>
public enum PedalMove { Page, PageThenSong, Song, ToggleScrolling }

/// <summary>Maps pedal presses to stage actions, as the player has set them up.</summary>
public static class Pedal
{
    /// <summary>How long a press must last to count as holding the pedal.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(0.6);

    /// <summary>+1 for the pedal's forward buttons (→ ↓ Page Down), −1 for back.</summary>
    public static int Direction(PedalButton button) =>
        button is PedalButton.Right or PedalButton.Down or PedalButton.PageDown ? +1 : -1;

    /// <summary>What a press (or a hold) of a pedal button does, and in which direction.</summary>
    public static (PedalMove Move, int Direction) Action(PedalButton button, bool held, PedalLayout layout, PedalHold hold)
    {
        var direction = Direction(button);
        if (held && hold == PedalHold.ChangeSong)
            return (PedalMove.Song, direction);
        if (held && hold == PedalHold.ToggleScrolling)
            return (PedalMove.ToggleScrolling, direction);
        return layout switch
        {
            PedalLayout.Songs => (PedalMove.Song, direction),
            PedalLayout.ArrowsChangeSongs when button is PedalButton.Left or PedalButton.Right => (PedalMove.Song, direction),
            PedalLayout.ArrowsChangeSongs => (PedalMove.Page, direction),
            _ => (PedalMove.PageThenSong, direction),
        };
    }
}
