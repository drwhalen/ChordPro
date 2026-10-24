using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.Core.Themes;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>Where a song is in the setlist being played: the songs either side, and how to step to them.</summary>
/// <param name="Next">"Set 2 · Bike (A)", or null at the end of the setlist.</param>
/// <param name="HasPrevious">False on the first song.</param>
internal sealed record SetlistPlace(string? Next, bool HasPrevious, Action<int> Step);

/// <summary>
/// A song on stage, with text size, transpose and theme, paged by taps or a pedal. Transpose and text size
/// are the song's own settings, shared with the Mac through the library.
/// </summary>
internal sealed class StageView : UserControl
{
    private const double TwoColumnMinWidth = 1000;
    // The Mac's usual text size; a size saved there is shown here in proportion to this device's usual size.
    private const double MacFontSize = 16;

    private readonly Song _song;
    private readonly SongSettings _songSettings;
    private readonly MobileSettings _settings;
    private readonly Action _saved;
    private readonly bool _keyIncludesCapo;
    private readonly SetlistPlace? _place;
    private readonly Border _footer = new() { Padding = new Thickness(8, 6) };
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly TextBlock _transposeText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 28, TextAlignment = TextAlignment.Center };
    private readonly Border _bar = new() { Padding = new Thickness(8, 6) };
    private readonly double _deviceFontSize =
        UIKit.UIDevice.CurrentDevice.UserInterfaceIdiom == UIKit.UIUserInterfaceIdiom.Phone ? 17 : 22;
    private int _columns = 1;

    /// <param name="saved">Called after the song's settings change, to share them.</param>
    /// <param name="place">The song's place in the setlist being played; null for a song opened from the list.</param>
    public StageView(Song song, SongSettings songSettings, MobileSettings settings, bool keyIncludesCapo,
        string backLabel, Action back, Action saved, SetlistPlace? place = null)
    {
        _place = place;
        _song = song;
        _songSettings = songSettings;
        _settings = settings;
        _keyIncludesCapo = keyIncludesCapo;
        _saved = saved;

        var backButton = MakeButton($"‹ {backLabel}", back);
        var title = new TextBlock
        {
            Text = song.Title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(MakeButton("A−", () => ChangeFont(-2)));
        buttons.Children.Add(MakeButton("A+", () => ChangeFont(+2)));
        buttons.Children.Add(MakeButton("−", () => ChangeTranspose(-1)));
        buttons.Children.Add(_transposeText);
        buttons.Children.Add(MakeButton("+", () => ChangeTranspose(+1)));
        buttons.Children.Add(MakeButton("Theme", NextTheme));
        var barContent = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        barContent.Children.Add(backButton);
        Grid.SetColumn(title, 1);
        barContent.Children.Add(title);
        Grid.SetColumn(buttons, 2);
        barContent.Children.Add(buttons);
        _bar.Child = barContent;

        var root = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        if (place is not null)
        {
            // What's coming next, so the key and the next chart are no surprise; tapping it goes there.
            var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            if (place.HasPrevious)
                footer.Children.Add(MakeButton("‹ Previous", () => place.Step(-1)));
            if (place.Next is { } next)
            {
                var nextButton = MakeButton($"Next: {next}  ›", () => place.Step(+1));
                Grid.SetColumn(nextButton, 2);
                footer.Children.Add(nextButton);
            }
            _footer.Child = footer;
            DockPanel.SetDock(_footer, Dock.Bottom);
            root.Children.Add(_footer);
        }
        root.Children.Add(_scroller);
        Content = root;

        Focusable = true;
        // A tap on the lower part of the song turns the page forward, on the upper part back, as on the Mac.
        _scroller.AddHandler(TappedEvent, (_, e) => Page(e.GetPosition(_scroller).Y > _scroller.Bounds.Height / 3 ? +1 : -1));
    }

    private double SongFontSize => _songSettings.FontSize is { } saved ? saved / MacFontSize * _deviceFontSize : _deviceFontSize;

    private Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    private void ChangeFont(double step)
    {
        var size = Math.Clamp(SongFontSize + step, 12, 48);
        _songSettings.FontSize = Math.Abs(size - _deviceFontSize) < 0.5 ? null : size / _deviceFontSize * MacFontSize;
        _saved();
        Render();
    }

    private void ChangeTranspose(int step)
    {
        _songSettings.Transpose += step;
        _saved();
        Render();
    }

    private void NextTheme()
    {
        var themes = SongTheme.BuiltIn;
        var index = themes.ToList().FindIndex(t => t.Name == _settings.Theme);
        _settings.Theme = themes[(index + 1) % themes.Count].Name;
        _settings.Save();
        Render();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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

    // Page turners send arrow keys, Page Up/Down or Space. With a two-button pedal, forward and back are
    // all there is, so they also move through the setlist at either end of a song.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Right or Key.PageDown or Key.Space: Page(+1, orSong: true); break;
            case Key.Up or Key.Left or Key.PageUp: Page(-1, orSong: true); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void Page(int direction, bool orSong = false)
    {
        var atEnd = direction > 0
            ? _scroller.Offset.Y >= _scroller.Extent.Height - _scroller.Viewport.Height - 1
            : _scroller.Offset.Y <= 1;
        if (orSong && atEnd && _place is not null)
        {
            _place.Step(direction);
            return;
        }
        var step = _scroller.Viewport.Height * 0.85;
        var y = Math.Clamp(_scroller.Offset.Y + direction * step, 0, Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height));
        _scroller.Offset = new Vector(0, y);
    }

    private void Render()
    {
        var theme = MainView.CurrentTheme(_settings);
        WindowTheme.Apply(theme);
        var palette = SongPalette.FromTheme(theme);
        Background = _scroller.Background = palette.Background;
        _bar.Background = _footer.Background = palette.CommentBackground;
        var transpose = _songSettings.Transpose;
        _transposeText.Text = transpose == 0 ? "0" : transpose.ToString("+0;−0");
        _columns = Bounds.Width >= TwoColumnMinWidth ? 2 : 1;

        var options = new RenderOptions
        {
            FontSize = SongFontSize,
            Transpose = transpose,
            Capo = _songSettings.Capo,
            Columns = _columns,
            Palette = palette,
            KeyIncludesCapo = _keyIncludesCapo,
        };
        _scroller.Content = new Border { Padding = new Thickness(16, 12), Child = new SongRenderer(options).Render(_song) };
    }
}
