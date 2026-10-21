using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

/// <summary>
/// Applies a theme to the app's controls (toolbar, lists, dialogs). Day and Night use the standard
/// light and dark styles; other themes recolour the Fluent palette so the whole window matches,
/// which matters for high contrast and for keeping a night-vision stage fully red.
/// </summary>
public static class WindowTheme
{
    /// <summary>True for themes that recolour the controls (everything except System, Day and Night).</summary>
    public static bool RecolorsControls(SongTheme? theme) =>
        theme is not null && theme.Name != SongTheme.Day.Name && theme.Name != SongTheme.Night.Name;

    /// <param name="theme">The theme to apply, or null to follow the operating system.</param>
    public static void Apply(SongTheme? theme)
    {
        var app = Application.Current!;
        var custom = RecolorsControls(theme);

        // Fluent builds its brushes from the palette when it first loads them, so changing a palette
        // afterwards doesn't reach every control. Swapping in a new FluentTheme rebuilds them all.
        var index = app.Styles.IndexOf(app.Styles.OfType<FluentTheme>().First());
        var fluent = new FluentTheme();
        if (custom)
        {
            var palette = new ColorPaletteResources();
            Fill(palette, theme!);
            fluent.Palettes[theme!.Dark ? ThemeVariant.Dark : ThemeVariant.Light] = palette;
        }
        app.Styles[index] = fluent;

        app.RequestedThemeVariant = theme is null ? ThemeVariant.Default : theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void Fill(ColorPaletteResources palette, SongTheme theme)
    {
        Color Pick(Func<SongTheme, string> color) => Color.Parse(color(theme));

        var background = Pick(t => t.Background);
        var text = Pick(t => t.Text);
        var muted = Pick(t => t.MutedText);
        var surface = Pick(t => t.CommentBackground);
        var border = Pick(t => t.CommentBox);

        palette.RegionColor = background;
        palette.AltHigh = palette.AltMediumHigh = palette.AltMedium = palette.AltMediumLow = palette.AltLow = background;
        palette.BaseHigh = palette.BaseMediumHigh = text;
        palette.BaseMedium = palette.BaseMediumLow = muted;
        palette.BaseLow = surface;
        // Checked toggles and check boxes are drawn inverted (text colour fill, background colour
        // glyph), the usual high-contrast convention. List selection is restyled in MainWindow.
        palette.ChromeWhite = background;
        palette.ChromeAltLow = text;
        palette.ChromeHigh = palette.ChromeGray = palette.ChromeDisabledLow = muted;
        palette.ChromeMedium = palette.ChromeMediumLow = palette.ChromeLow = palette.ListLow = surface;
        palette.ChromeBlackHigh = palette.ChromeBlackMedium = palette.ChromeBlackMediumLow = palette.ChromeBlackLow = background;
        palette.ChromeDisabledHigh = palette.ListMedium = border;
        palette.ErrorText = Pick(t => t.Chord);
        palette.Accent = text;
    }
}
