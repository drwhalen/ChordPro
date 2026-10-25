using System.Reflection;
using ChordPro.Core;
using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace ChordPro.Mobile;

/// <summary>
/// The library folder the player chose in Files (iCloud Drive, or Dropbox and others through their apps),
/// and the device's copy of it. iOS grants access to a picked folder through a security-scoped URL; a
/// bookmark of it keeps that access across launches.
/// </summary>
internal sealed class LibraryAccess(MobileSettings settings)
{
    private NSUrl? _url;

#if DEBUG
    // For checks in the simulator, which can't be tapped from a script: a library folder on the Mac,
    // given with `SIMCTL_CHILD_CHORDPRO_LIVE_LIBRARY=… xcrun simctl launch …`.
    private static readonly string? TestLibrary = Environment.GetEnvironmentVariable("CHORDPRO_LIVE_LIBRARY");
#else
    private static readonly string? TestLibrary = null; // not const: a constant null makes the checks below warnings
#endif

    /// <summary>True once a library folder has been chosen (it may be out of reach right now).</summary>
    public bool HasLibrary => settings.LibraryBookmark is not null || TestLibrary is not null;

    /// <summary>The folder songs are read from: the copy of the library, or the samples before one is chosen.</summary>
    public string SongsFolder => HasLibrary ? AppFolders.LibraryCopy : AppFolders.Samples;

    /// <summary>Reopens the chosen folder from its bookmark. False when it's gone or can't be reached.</summary>
    public bool Reopen()
    {
        if (_url is not null)
            return true;
        if (settings.LibraryBookmark is not { } bookmark)
            return false;
        var url = NSUrl.FromBookmarkData(new NSData(bookmark, NSDataBase64DecodingOptions.None),
            NSUrlBookmarkResolutionOptions.WithoutUI, null, out var stale, out var error);
        if (url is null || error is not null || !url.StartAccessingSecurityScopedResource())
            return false;
        _url = url;
        if (stale)
            Remember(url); // the folder moved or was renamed; a fresh bookmark keeps finding it
        return true;
    }

    /// <summary>Shows the Files folder picker. True when a folder was chosen.</summary>
    public Task<bool> ChooseAsync()
    {
        var done = new TaskCompletionSource<bool>();
        var picker = new UIDocumentPickerViewController([UTTypes.Folder], asCopy: false);
        picker.DidPickDocumentAtUrls += (_, e) =>
        {
            if (e.Urls.FirstOrDefault() is not { } url || !url.StartAccessingSecurityScopedResource())
            {
                done.TrySetResult(false);
                return;
            }
            var changed = _url?.Path != url.Path;
            _url?.StopAccessingSecurityScopedResource();
            _url = url;
            Remember(url);
            if (changed)
            {
                // Another library: its copy starts from scratch (the old one's song settings mustn't merge in).
                try
                {
                    if (Directory.Exists(AppFolders.LibraryCopy))
                        Directory.Delete(AppFolders.LibraryCopy, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
                settings.LastSynced = null;
                settings.Save();
            }
            done.TrySetResult(true);
        };
        picker.WasCancelled += (_, _) => done.TrySetResult(false);
        if (TopController() is { } controller)
            controller.PresentViewController(picker, true, null);
        else
            done.TrySetResult(false);
        return done.Task;
    }

    /// <summary>
    /// Brings the copy up to date with the library and asks iCloud for files it hasn't downloaded. Null when
    /// the library can't be reached (offline, or the folder is gone); the copy is used as it is.
    /// </summary>
    public Task<MirrorResult?> RefreshAsync()
    {
        var folder = TestLibrary;
        if (folder is null && (!Reopen() || (folder = _url?.Path) is null))
            return Task.FromResult<MirrorResult?>(null);
        return Task.Run(() =>
        {
            if (!Directory.Exists(folder))
                return null;
            var result = LibraryMirror.Update(folder, AppFolders.LibraryCopy);
            foreach (var path in result.NotDownloaded)
                NSFileManager.DefaultManager.StartDownloadingUbiquitous(NSUrl.FromFilename(path), out _);
            return result;
        });
    }

    /// <summary>Pushes song settings changed on this device to the library (and takes any newer ones back).</summary>
    public void ShareSettings()
    {
        if ((TestLibrary ?? _url?.Path) is { } folder)
            Task.Run(() => LibrarySongSettings.MergeFiles(folder, AppFolders.LibraryCopy));
    }

    /// <summary>Writes the bundled sample songs out, for browsing before a library is chosen.</summary>
    public static void ExtractSamples()
    {
        Directory.CreateDirectory(AppFolders.Samples);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("samples/", StringComparison.Ordinal)))
        {
            var target = Path.Combine(AppFolders.Samples, name["samples/".Length..]);
            if (File.Exists(target))
                continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(target);
            stream.CopyTo(file);
        }
    }

    private void Remember(NSUrl url)
    {
        var data = url.CreateBookmarkData(0, null, null, out var error);
        if (data is null || error is not null)
            return;
        settings.LibraryBookmark = data.GetBase64EncodedString(NSDataBase64EncodingOptions.None);
        settings.LibraryName = url.LastPathComponent;
        settings.Save();
    }

    private static UIViewController? TopController()
    {
        var controller = UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>()
            .Select(s => s.KeyWindow).FirstOrDefault(w => w is not null)?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
            controller = presented;
        return controller;
    }
}
