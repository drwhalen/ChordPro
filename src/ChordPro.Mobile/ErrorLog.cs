using Avalonia.Threading;
using ObjCRuntime;

namespace ChordPro.Mobile;

/// <summary>
/// Records errors nothing else handled, so a crash can be diagnosed afterwards (iOS's crash report shows only
/// that a .NET exception escaped, not which), and keeps an error on the UI thread from closing the app.
/// The log is errors.log in the app's Application Support folder.
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 1 << 20; // start over past 1 MB rather than grow forever

    public static string FilePath => Path.Combine(AppFolders.Support, "errors.log");

    /// <summary>Records errors from any thread; call first thing in Main.</summary>
    public static void Install()
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("background task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.IsTerminating ? "fatal" : "unhandled", e.ExceptionObject as Exception);
        // An exception escaping a touch or other UIKit callback ends the app before AppDomain hears of it.
        Runtime.MarshalManagedException += (_, e) => Write("UIKit callback", e.Exception);
    }

    /// <summary>
    /// Keeps an error in UI code run by the dispatcher (timers, posted work, async handlers) from closing the app.
    /// Call once Avalonia is running.
    /// </summary>
    public static void InstallForUIThread() =>
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            e.Handled = true;
        };

    public static void Write(string where, Exception? exception)
    {
        Console.Error.WriteLine($"ChordPro Live error ({where}): {exception}");
        try
        {
            Directory.CreateDirectory(AppFolders.Support);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Delete(FilePath);
            File.AppendAllText(FilePath, $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({where})\n{exception}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nowhere to record it; don't make things worse
        }
    }
}
