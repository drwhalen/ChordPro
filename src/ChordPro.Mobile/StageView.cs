using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.Core.Themes;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>
/// The first stage screen: a bundled song with text size, transpose and theme, paged by taps or a pedal.
/// A spike to see the shared renderer on iPad and iPhone before the real app is built around it.
/// </summary>
public sealed class StageView : UserControl
{
    private const double TwoColumnMinWidth = 1000;

    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly ComboBox _songBox = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _transposeText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 28, TextAlignment = TextAlignment.Center };
    private readonly Border _bar = new() { Padding = new Thickness(8, 6) };
    private readonly List<(string Name, Song Song)> _songs = LoadSamples();

    private Song _song;
    private double _fontSize;
    private int _transpose;
    private int _theme = 1; // Night
    private int _columns = 1;

    public StageView()
    {
        _song = _songs[0].Song;
        _songBox.ItemsSource = _songs.Select(s => s.Name).ToList();
        _songBox.SelectedIndex = 0;
        _songBox.SelectionChanged += (_, _) =>
        {
            _song = _songs[Math.Max(0, _songBox.SelectedIndex)].Song;
            _transpose = 0;
            Render();
            _scroller.Offset = default;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("A−", () => _fontSize = Math.Max(12, _fontSize - 2)));
        buttons.Children.Add(Button("A+", () => _fontSize = Math.Min(48, _fontSize + 2)));
        buttons.Children.Add(Button("−", () => _transpose--));
        buttons.Children.Add(_transposeText);
        buttons.Children.Add(Button("+", () => _transpose++));
        buttons.Children.Add(Button("Theme", () => _theme = (_theme + 1) % SongTheme.BuiltIn.Count));
        var barContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        barContent.Children.Add(_songBox);
        Grid.SetColumn(buttons, 1);
        barContent.Children.Add(buttons);
        _bar.Child = barContent;

        var root = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        root.Children.Add(_scroller);
        Content = root;

        Focusable = true;
        // A tap on the lower part of the song turns the page forward, on the upper part back, as on the Mac.
        _scroller.AddHandler(TappedEvent, (_, e) => Page(e.GetPosition(_scroller).Y > _scroller.Bounds.Height / 3 ? +1 : -1));
    }

    private Button Button(string text, Action change)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 44,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) =>
        {
            change();
            Render();
        };
        return button;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A phone has room for about 40 characters a line at 17 points; an iPad takes stage-size text.
        _fontSize = UIKit.UIDevice.CurrentDevice.UserInterfaceIdiom == UIKit.UIUserInterfaceIdiom.Phone ? 17 : 22;
        // Keep clear of the notch, the Dynamic Island and the home indicator.
        if (TopLevel.GetTopLevel(this)?.InsetsManager is { } insets)
        {
            Padding = insets.SafeAreaPadding;
            insets.SafeAreaChanged += (_, args) => Padding = args.SafeAreaPadding;
        }
        Render();
        Focus();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Two columns on a wide screen (an iPad in landscape), one otherwise.
        if ((e.NewSize.Width >= TwoColumnMinWidth ? 2 : 1) != _columns)
            Render();
    }

    // Page turners send arrow keys, Page Up/Down or Space.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Right or Key.PageDown or Key.Space: Page(+1); break;
            case Key.Up or Key.Left or Key.PageUp: Page(-1); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void Page(int direction)
    {
        var step = _scroller.Viewport.Height * 0.85;
        var y = Math.Clamp(_scroller.Offset.Y + direction * step, 0, Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height));
        _scroller.Offset = new Vector(0, y);
    }

    private void Render()
    {
        var theme = SongTheme.BuiltIn[_theme];
        WindowTheme.Apply(theme);
        var palette = SongPalette.FromTheme(theme);
        Background = _scroller.Background = palette.Background;
        _bar.Background = palette.CommentBackground;
        _transposeText.Text = _transpose == 0 ? "0" : _transpose.ToString("+0;−0");
        _columns = Bounds.Width >= TwoColumnMinWidth ? 2 : 1;

        var options = new RenderOptions
        {
            FontSize = _fontSize,
            Transpose = _transpose,
            Columns = _columns,
            Palette = palette,
        };
        _scroller.Content = new Border { Padding = new Thickness(16, 12), Child = new SongRenderer(options).Render(_song) };
    }

    private static List<(string, Song)> LoadSamples()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return [.. assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("samples/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                var song = ChordProParser.Parse(reader.ReadToEnd());
                return (song.Title ?? Path.GetFileNameWithoutExtension(n), song);
            })];
    }
}
