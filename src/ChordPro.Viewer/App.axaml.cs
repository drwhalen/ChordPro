using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace ChordPro.Viewer;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = _mainWindow = new MainWindow(desktop.Args?.FirstOrDefault());

        // macOS sends files opened from Finder (double-click, "Open With", dropping on the Dock icon)
        // as an activation event rather than on the command line.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is FileActivatedEventArgs { Files: [var file, ..] } && file.TryGetLocalPath() is { } path)
                    _mainWindow?.OpenPath(path);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnAboutClick(object? sender, EventArgs e)
    {
        if (_mainWindow is not null)
            new AboutWindow().ShowDialog(_mainWindow);
    }
}
