using Avalonia;

namespace ChordPro.Viewer;

internal static class Program
{
    // Usage: ChordPro.Viewer [path/to/song.cho]
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
