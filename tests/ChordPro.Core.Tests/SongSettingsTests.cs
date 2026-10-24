using ChordPro.Core;

namespace ChordPro.Core.Tests;

public sealed class SongSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("chordpro-tests").FullName;
    private DateTime _now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // A device opening the library, with a clock the test moves forward.
    private LibrarySongSettings OpenDevice() => LibrarySongSettings.Open(_folder, () => _now);

    private string SettingsFile => Path.Combine(_folder, LibrarySongSettings.FileName);

    [Fact]
    public void KeysSongsByPathWithinTheLibrary()
    {
        var library = OpenDevice();

        Assert.Equal("Rock/Song - Artist.cho", library.KeyFor(Path.Combine(_folder, "Rock", "Song - Artist.cho")));
        Assert.Equal(Path.Combine(_folder, "Rock", "Song - Artist.cho"), library.PathFor("Rock/Song - Artist.cho"));
        Assert.Null(library.KeyFor(Path.Combine(Path.GetTempPath(), "elsewhere.cho")));
        Assert.Null(library.KeyFor(_folder + "-other" + Path.DirectorySeparatorChar + "song.cho"));
    }

    [Fact]
    public void WritesOnlySongsWhoseSettingsChanged()
    {
        var library = OpenDevice();
        library.GetOrAdd("looked-at.cho");
        Assert.True(library.Save());
        Assert.False(File.Exists(SettingsFile)); // opening a song isn't worth a file

        library.GetOrAdd("song.cho").Transpose = 2;
        Assert.True(library.Save());

        var reopened = OpenDevice();
        Assert.Equal(2, reopened.Get("song.cho")?.Transpose);
        Assert.Equal(_now, reopened.Get("song.cho")?.Modified);
        Assert.Null(reopened.Get("looked-at.cho"));
    }

    [Fact]
    public void SavesFromTwoDevicesAreMergedSongBySong()
    {
        var mac = OpenDevice();
        var ipad = OpenDevice();

        mac.GetOrAdd("a.cho").Capo = 3;
        mac.Save();
        ipad.GetOrAdd("b.cho").Transpose = -2;
        ipad.Save();

        var later = OpenDevice();
        Assert.Equal(3, later.Get("a.cho")?.Capo);
        Assert.Equal(-2, later.Get("b.cho")?.Transpose);
        Assert.Equal(3, ipad.Get("a.cho")?.Capo); // the iPad picked up the Mac's song while saving
    }

    [Fact]
    public void StageNotesWrittenOnTheIPadReachTheMac()
    {
        var mac = OpenDevice();
        var ipad = OpenDevice();
        ipad.GetOrAdd("Rock/Song.cho").Notes = "Drop the 2nd verse\nLong intro";
        Assert.True(ipad.Save());

        _now = _now.AddMinutes(1);
        mac.Refresh();
        Assert.Equal("Drop the 2nd verse\nLong intro", mac.Get("Rock/Song.cho")?.Notes);
    }

    [Fact]
    public void TheNewerChangeToASongWins()
    {
        var mac = OpenDevice();
        var ipad = OpenDevice();

        // The iPad changed the song first but saved it last (it was offline, say).
        _now = _now.AddMinutes(1);
        ipad.GetOrAdd("song.cho").Transpose = 1;
        ipad.Save();
        _now = _now.AddMinutes(1);
        mac.GetOrAdd("song.cho").Transpose = 5;
        mac.Save();
        // Saved with an older time stamp than the file now has.
        var stale = OpenDevice();
        _now = _now.AddMinutes(-5);
        stale.GetOrAdd("song.cho").Transpose = 9;
        stale.Save();

        Assert.Equal(5, OpenDevice().Get("song.cho")?.Transpose);
        Assert.Equal(5, stale.Get("song.cho")?.Transpose);
    }

    [Fact]
    public void RefreshTakesOtherDevicesChangesButKeepsUnsavedOnes()
    {
        var mac = OpenDevice();
        var ipad = OpenDevice();
        var song = mac.GetOrAdd("song.cho");

        ipad.GetOrAdd("song.cho").Capo = 2;
        ipad.GetOrAdd("other.cho").Tempo = 120;
        ipad.Save();
        mac.GetOrAdd("other.cho").Tempo = 90; // changed here, not saved yet
        mac.Refresh();

        Assert.Same(song, mac.Get("song.cho")); // the same object, now with the iPad's capo
        Assert.Equal(2, song.Capo);
        Assert.Equal(90, mac.Get("other.cho")?.Tempo);
    }

    [Fact]
    public void ResettingASongReachesOtherDevices()
    {
        var mac = OpenDevice();
        mac.GetOrAdd("song.cho").Transpose = 2;
        mac.Save();
        var ipad = OpenDevice();

        _now = _now.AddMinutes(1);
        mac.GetOrAdd("song.cho").Transpose = 0;
        mac.Save();
        ipad.Refresh();

        Assert.Equal(0, ipad.Get("song.cho")?.Transpose);
    }

    [Fact]
    public void AdoptsSettingsKeptByFullPathForSongsInTheLibrary()
    {
        var inside = Path.Combine(_folder, "Sub", "song.cho");
        var outside = Path.Combine(Path.GetTempPath(), "loose.cho");
        var known = Path.Combine(_folder, "known.cho");
        var library = OpenDevice();
        library.GetOrAdd("known.cho").Capo = 1;
        library.Save();

        var adopted = library.Adopt(new Dictionary<string, SongSettings>
        {
            [inside] = new() { Transpose = 3, Plays = 7 },
            [outside] = new() { Transpose = 1 },
            [known] = new() { Capo = 4 },
        });
        library.Save();

        Assert.Equal([inside, known], adopted.Order(StringComparer.Ordinal));
        var reopened = OpenDevice();
        Assert.Equal(7, reopened.Get("Sub/song.cho")?.Plays);
        Assert.Equal(1, reopened.Get("known.cho")?.Capo); // the library's file already had it
        Assert.Equal(2, reopened.Songs.Count);
    }

    [Fact]
    public void DoesNotWriteOverAFileItCannotRead()
    {
        File.WriteAllText(SettingsFile, "{ not json");
        var library = OpenDevice();
        library.GetOrAdd("song.cho").Transpose = 1;

        Assert.False(library.Save());
        Assert.Equal("{ not json", File.ReadAllText(SettingsFile));

        File.Delete(SettingsFile);
        Assert.True(library.Save()); // the change waited for the next save
        Assert.Equal(1, OpenDevice().Get("song.cho")?.Transpose);
    }

    [Fact]
    public void DoesNotWriteWhileICloudHasEvictedTheFile()
    {
        File.WriteAllText(Path.Combine(_folder, "." + LibrarySongSettings.FileName + ".icloud"), "");
        var library = OpenDevice();
        library.GetOrAdd("song.cho").Transpose = 1;

        Assert.False(library.Save());
        Assert.False(File.Exists(SettingsFile));
    }
}
