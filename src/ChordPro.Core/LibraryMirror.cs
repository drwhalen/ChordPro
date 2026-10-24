namespace ChordPro.Core;

/// <summary>What one <see cref="LibraryMirror.Update"/> did.</summary>
/// <param name="Copied">Files new or changed in the library, copied over.</param>
/// <param name="Removed">Copies of files no longer in the library, deleted.</param>
/// <param name="NotDownloaded">
/// Library files iCloud hasn't downloaded yet (full paths of the files as they'll be named), to ask for;
/// their old copies are kept meanwhile.
/// </param>
/// <param name="SettingsMerged">False when the song settings files couldn't be merged this time.</param>
public sealed record MirrorResult(int Copied, int Removed, IReadOnlyList<string> NotDownloaded, bool SettingsMerged)
{
    /// <summary>True when something in the copy changed.</summary>
    public bool Changed => Copied > 0 || Removed > 0;
}

/// <summary>
/// Keeps a device's own copy of a library folder (songs and setlists) up to date, so the iPad and iPhone
/// read songs from local storage on stage, whatever the connection. The copy keeps the library's folder
/// layout, so library-relative paths (setlists, <see cref="LibrarySongSettings"/>) mean the same in both,
/// and each file's dates, so "Recently added" still works.
/// </summary>
public static class LibraryMirror
{
    /// <summary>True for the files the copy holds: songs, <c>.txt</c> files (songs or LivePrompter setlists) and setlists.</summary>
    public static bool IsMirrored(string path) =>
        SongLibrary.IsSongFile(path) ||
        Path.GetExtension(path).Equals(SongLibrary.TextExtension, StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(Setlist.FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Brings <paramref name="copy"/> up to date with <paramref name="library"/>, and merges their song settings.</summary>
    public static MirrorResult Update(string library, string copy)
    {
        Directory.CreateDirectory(copy);
        // Hidden files included: iCloud's placeholders for files not yet downloaded are hidden.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        HashSet<string> present = new(StringComparer.Ordinal);
        List<string> notDownloaded = [];
        var copied = 0;

        foreach (var source in Directory.EnumerateFiles(library, "*", options))
        {
            var relative = Path.GetRelativePath(library, source);
            var name = Path.GetFileName(relative);
            if (PlaceholderFor(name) is { } real)
            {
                // ".Song.cho.icloud" stands for "Song.cho": keep the copy we have until it downloads.
                var realRelative = Path.Combine(Path.GetDirectoryName(relative) ?? "", real);
                if (IsMirrored(real) && !IsHidden(realRelative))
                {
                    present.Add(realRelative);
                    notDownloaded.Add(Path.Combine(library, realRelative));
                }
                continue;
            }
            if (IsHidden(relative) || !IsMirrored(name))
                continue;
            present.Add(relative);
            var target = Path.Combine(copy, relative);
            if (IsSame(source, target))
                continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temp = target + ".tmp";
                File.Copy(source, temp, overwrite: true);
                File.SetCreationTimeUtc(temp, File.GetCreationTimeUtc(source));
                File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
                File.Move(temp, target, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file mid-sync may not read; the next update picks it up.
            }
        }

        var removed = 0;
        foreach (var target in Directory.EnumerateFiles(copy, "*", options).ToList())
        {
            var relative = Path.GetRelativePath(copy, target);
            if (IsHidden(relative) || !IsMirrored(target) || present.Contains(relative))
                continue;
            try
            {
                File.Delete(target);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var merged = LibrarySongSettings.MergeFiles(library, copy);
        return new MirrorResult(copied, removed, notDownloaded, merged);
    }

    // The real name behind an iCloud placeholder (".Song.cho.icloud" → "Song.cho"), or null.
    private static string? PlaceholderFor(string name) =>
        name.Length > ".icloud".Length + 1 && name.StartsWith('.') && name.EndsWith(".icloud", StringComparison.Ordinal)
            ? name[1..^".icloud".Length]
            : null;

    // Dot files and anything in a dot folder: settings, temporary files, version-control folders.
    private static bool IsHidden(string relative) =>
        relative.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.'));

    // Unchanged since the last copy: the same size and modification time.
    private static bool IsSame(string source, string target)
    {
        var from = new FileInfo(source);
        var to = new FileInfo(target);
        return to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc;
    }
}
