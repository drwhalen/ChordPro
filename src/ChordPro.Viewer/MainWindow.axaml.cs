using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ChordPro.Core;

namespace ChordPro.Viewer;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ChordProFiles = new("ChordPro songs")
    {
        Patterns = ["*.cho", "*.chopro", "*.chordpro", "*.crd", "*.pro", "*.txt"],
    };

    private const double MinFontSize = 10, MaxFontSize = 40;

    private Song? _song;
    private double _fontSize = 16;
    private int _transpose;

    public MainWindow() : this(null) { }

    public MainWindow(string? initialPath)
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);

        if (initialPath is not null)
            LoadFile(initialPath);
        else
            ShowMessage("Open a ChordPro file or drop one onto this window.");
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ChordPro song",
            AllowMultiple = false,
            FileTypeFilter = [ChordProFiles, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            LoadFile(path);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path)
            LoadFile(path);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Cmd on macOS, Ctrl elsewhere.
        var command = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(command))
        {
            switch (e.Key)
            {
                case Key.O: OnOpenClick(this, e); e.Handled = true; break;
                case Key.OemPlus or Key.Add: SetFontSize(_fontSize + 2); e.Handled = true; break;
                case Key.OemMinus or Key.Subtract: SetFontSize(_fontSize - 2); e.Handled = true; break;
            }
        }
        base.OnKeyDown(e);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => SetFontSize(_fontSize + 2);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => SetFontSize(_fontSize - 2);

    private void OnTransposeDownClick(object? sender, RoutedEventArgs e) => SetTranspose(_transpose - 1);

    private void OnTransposeUpClick(object? sender, RoutedEventArgs e) => SetTranspose(_transpose + 1);

    private void OnTransposeResetClick(object? sender, RoutedEventArgs e) => SetTranspose(0);

    private void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
        RenderSong();
    }

    private void SetTranspose(int semitones)
    {
        // Twelve semitones is the same key again, so wrap around within an octave.
        _transpose = semitones % 12;
        TransposeResetButton.Content = _transpose > 0 ? $"+{_transpose}" : _transpose < 0 ? $"−{-_transpose}" : "0";
        TransposeResetButton.IsEnabled = _transpose != 0;
        RenderSong();
    }

    private void RenderSong()
    {
        if (_song is not null)
            SongHost.Content = new SongRenderer(_fontSize, _transpose).Render(_song);
    }

    private void LoadFile(string path)
    {
        try
        {
            _song = ChordProParser.ParseFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Could not open {Path.GetFileName(path)}:\n{ex.Message}");
            return;
        }

        FileNameText.Text = Path.GetFileName(path);
        Title = _song.Title is { } title ? $"{title} — ChordPro Viewer" : "ChordPro Viewer";
        SetTranspose(0);
        SongScroller.Offset = Vector.Zero;
    }

    private void ShowMessage(string message)
    {
        _song = null;
        SongHost.Content = new TextBlock
        {
            Text = message,
            Opacity = 0.6,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 80, 0, 0),
        };
    }
}
