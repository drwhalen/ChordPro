using Avalonia.Threading;

namespace ChordPro.Viewer;

/// <summary>
/// Records errors nothing else handled, so a crash can be diagnosed afterwards (an app started from
/// Finder has nowhere to print them), and keeps an error on the UI thread from closing the app.
/// The log is ~/Library/Logs/ChordPro Viewer/errors.log on macOS, beside the settings elsewhere.
/// </summary>
public static class ErrorLog
{
    private const long MaxBytes = 1 << 20; // start over past 1 MB rather than grow forever

    // CHORDPRO_VIEWER_LOG overrides the file (useful for testing).
    public static string FilePath { get; } = Environment.GetEnvironmentVariable("CHORDPRO_VIEWER_LOG") ?? Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChordPro Viewer", "errors.log");

    /// <summary>Raised on the UI thread after an error there was caught, with a one-line description.</summary>
    public static event Action<string>? Recovered;

    /// <summary>Records errors from any thread; call first thing in Main.</summary>
    public static void Install()
    {
        // A failed background task nobody waited for.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("background task", e.Exception);
            e.SetObserved();
        };
        // Anything else is about to end the process; at least record why.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.IsTerminating ? "fatal" : "unhandled", e.ExceptionObject as Exception);
    }

    /// <summary>
    /// Keeps an error in UI code (including async event handlers) from closing the app: it is
    /// recorded and reported instead. Call once Avalonia is running; touching the dispatcher before
    /// the platform is set up breaks its main loop.
    /// </summary>
    public static void InstallForUIThread() =>
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            e.Handled = true;
            Recovered?.Invoke(e.Exception.Message);
        };

    public static void Write(string where, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Delete(FilePath);
            var version = typeof(ErrorLog).Assembly.GetName().Version;
            File.AppendAllText(FilePath, $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({where}, version {version})\n{exception}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nowhere to record it; don't make things worse
        }
    }
}
