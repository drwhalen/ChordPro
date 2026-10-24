using ChordPro.Core;

namespace ChordPro.Core.Tests;

public sealed class LibraryMirrorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("chordpro-tests").FullName;
    private string Library => Path.Combine(_root, "library");
    private string Copy => Path.Combine(_root, "copy");

    public LibraryMirrorTests() => Directory.CreateDirectory(Library);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string text = "{title: Song}")
    {
        var path = Path.Combine(Library, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void CopiesSongsAndSetlistsKeepingFoldersAndDates()
    {
        var song = Write("Rock/Bike - Pink Floyd.cho");
        Write("Setlists/Friday.txt", "Bike - Pink Floyd\n");
        Write("gig.setlist", "Rock/Bike - Pink Floyd.cho\n");
        Write("cover.jpg", "not a song");
        Write(".git/config", "[core]");
        var created = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(song, created);

        var result = LibraryMirror.Update(Library, Copy);

        Assert.Equal(3, result.Copied);
        var copied = Path.Combine(Copy, "Rock", "Bike - Pink Floyd.cho");
        Assert.Equal("{title: Song}", File.ReadAllText(copied));
        Assert.Equal(created, File.GetCreationTimeUtc(copied));
        Assert.Equal(File.GetLastWriteTimeUtc(song), File.GetLastWriteTimeUtc(copied));
        Assert.True(File.Exists(Path.Combine(Copy, "Setlists", "Friday.txt")));
        Assert.False(File.Exists(Path.Combine(Copy, "cover.jpg")));
        Assert.False(Directory.Exists(Path.Combine(Copy, ".git")));
    }

    [Fact]
    public void CopiesOnlyWhatChangedAndRemovesWhatWasDeleted()
    {
        var keep = Write("keep.cho");
        var change = Write("change.cho");
        var gone = Write("gone.cho");
        LibraryMirror.Update(Library, Copy);

        File.WriteAllText(change, "{title: Changed}");
        File.SetLastWriteTimeUtc(change, File.GetLastWriteTimeUtc(keep).AddMinutes(1));
        File.Delete(gone);
        var result = LibraryMirror.Update(Library, Copy);

        Assert.Equal(1, result.Copied);
        Assert.Equal(1, result.Removed);
        Assert.Equal("{title: Changed}", File.ReadAllText(Path.Combine(Copy, "change.cho")));
        Assert.False(File.Exists(Path.Combine(Copy, "gone.cho")));
        Assert.False(LibraryMirror.Update(Library, Copy).Changed);
    }

    [Fact]
    public void KeepsTheOldCopyOfASongICloudHasNotDownloaded()
    {
        var song = Write("Sub/song.cho");
        LibraryMirror.Update(Library, Copy);

        // iCloud evicted it: only ".song.cho.icloud" is left.
        File.Delete(song);
        Write("Sub/.song.cho.icloud", "placeholder");
        var result = LibraryMirror.Update(Library, Copy);

        Assert.Equal(0, result.Removed);
        Assert.True(File.Exists(Path.Combine(Copy, "Sub", "song.cho")));
        Assert.Equal([song], result.NotDownloaded);
    }

    [Fact]
    public void MergesSongSettingsBothWays()
    {
        var now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        var mac = LibrarySongSettings.Open(Library, () => now);
        mac.GetOrAdd("a.cho").Capo = 2;
        mac.Save();
        Directory.CreateDirectory(Copy);
        var ipad = LibrarySongSettings.Open(Copy, () => now.AddMinutes(1));
        ipad.GetOrAdd("b.cho").Transpose = 3;
        ipad.Save();

        var result = LibraryMirror.Update(Library, Copy);

        Assert.True(result.SettingsMerged);
        foreach (var folder in new[] { Library, Copy })
        {
            var settings = LibrarySongSettings.Open(folder);
            Assert.Equal(2, settings.Get("a.cho")?.Capo);
            Assert.Equal(3, settings.Get("b.cho")?.Transpose);
        }
    }

    [Fact]
    public void LeavesSettingsAloneWhileICloudHasNotDownloadedThem()
    {
        Write("." + LibrarySongSettings.FileName + ".icloud", "placeholder");
        Directory.CreateDirectory(Copy);
        var ipad = LibrarySongSettings.Open(Copy);
        ipad.GetOrAdd("b.cho").Transpose = 3;
        ipad.Save();

        var result = LibraryMirror.Update(Library, Copy);

        Assert.False(result.SettingsMerged);
        Assert.False(File.Exists(Path.Combine(Library, LibrarySongSettings.FileName)));
    }
}
