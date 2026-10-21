using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.Core.Chords;

namespace ChordPro.Viewer;

/// <summary>Fixed colours, for rendering outside the themed window (e.g. PDF export).</summary>
public sealed record SongPalette(IBrush Text, IBrush Chord, IBrush ChorusBar, IBrush CommentBackground, IBrush CommentBox)
{
    public static SongPalette Print { get; } = new(
        Brushes.Black,
        new SolidColorBrush(Color.Parse("#C2410C")),
        new SolidColorBrush(Color.Parse("#D4D4D8")),
        new SolidColorBrush(Color.Parse("#EDEDF0")),
        new SolidColorBrush(Color.Parse("#A1A1AA")));
}

public sealed record RenderOptions
{
    public double FontSize { get; init; } = 16;
    /// <summary>Semitones to move the song's pitch.</summary>
    public int Transpose { get; init; }
    /// <summary>The capo the player is using; null means the one given in the file.</summary>
    public int? Capo { get; init; }
    public bool ShowChords { get; init; } = true;
    public bool ShowDiagrams { get; init; }
    public Instrument Instrument { get; init; } = Instrument.Guitar;
    public int Columns { get; init; } = 1;
    /// <summary>Explicit colours; null uses the current theme.</summary>
    public SongPalette? Palette { get; init; }
}

/// <summary>Builds a read-only visual tree for a <see cref="Song"/>.</summary>
public sealed partial class SongRenderer(RenderOptions options)
{
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    // A word plus the whitespace that follows it.
    [GeneratedRegex(@"\S*\s*")]
    private static partial Regex WordRegex();

    private double FontSize => options.FontSize;

    private int _shift;
    private bool? _preferFlats;

    /// <summary>
    /// The smallest pieces the rendered song can be split between (header, lines, diagram rows), in
    /// display order. Used to choose page breaks when printing.
    /// </summary>
    public List<Control> Blocks { get; } = [];

    /// <summary>The written capo from the file (0 if none).</summary>
    public static int FileCapo(Song song) => int.TryParse(song.Capo, out var capo) ? Math.Clamp(capo, 0, 11) : 0;

    public Control Render(Song song)
    {
        // Chords are written as shapes for the file's capo; re-spell them for the player's capo and transposition.
        var fileCapo = FileCapo(song);
        _shift = options.Transpose + fileCapo - (options.Capo ?? fileCapo);
        _preferFlats = ChordTransposer.PrefersFlats(song.Key, _shift);
        Blocks.Clear();

        var root = new StackPanel { Spacing = FontSize };
        root.Children.Add(Block(RenderHeader(song, fileCapo)));

        if (options.ShowChords && options.ShowDiagrams && RenderDiagrams(song) is { } diagrams)
            root.Children.Add(diagrams);

        var sections = song.Sections.Where(s => options.ShowChords || !IsChordsOnly(s)).ToList();
        if (options.Columns <= 1 || sections.Count < 2)
        {
            foreach (var section in sections)
                root.Children.Add(RenderSection(section));
        }
        else
        {
            root.Children.Add(RenderColumns(sections));
        }

        return root;
    }

    private Control RenderHeader(Song song, int fileCapo)
    {
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, FontSize * 0.5) };

        if (song.Title is { } title)
            header.Children.Add(Text(title, FontSize * 1.75, FontWeight.Bold, wrap: true));
        if (song.Subtitle is { } subtitle)
            header.Children.Add(Text(subtitle, FontSize * 1.1, opacity: 0.8, wrap: true));
        if (song.Artist is { } artist)
            header.Children.Add(Text(artist, FontSize, opacity: 0.8, wrap: true));

        var capo = options.Capo ?? fileCapo;
        var details = new List<string>();
        if (song.Key is { } key)
        {
            // The written key sounds a capo's worth higher; show what the audience hears.
            details.Add($"Key: {ChordTransposer.Transpose(key, options.Transpose + fileCapo, ChordTransposer.PrefersFlats(key, options.Transpose + fileCapo))}");
        }
        if (capo > 0 || song.Capo is not null)
            details.Add(capo > 0 && song.Key is { } k ? $"Capo: {capo} (play {TransposeChord(k)} shapes)" : $"Capo: {capo}");
        if (song.Tempo is { } tempo)
            details.Add($"Tempo: {tempo}");
        if (song.Time is { } time)
            details.Add($"Time: {time}");
        if (details.Count > 0)
            header.Children.Add(Text(string.Join("   ·   ", details), FontSize * 0.9, opacity: 0.6, wrap: true));

        return header;
    }

    private Control? RenderDiagrams(Song song)
    {
        var names = song.Sections
            .Where(s => s.Kind is not (SectionKind.Tab or SectionKind.Grid))
            .SelectMany(s => s.Lines.OfType<LyricLine>())
            .SelectMany(l => l.Segments)
            .Select(s => s.Chord)
            .OfType<string>()
            .Where(c => c.Length > 0)
            .Distinct()
            .ToList();

        var panel = new WrapPanel();
        foreach (var written in names)
        {
            var name = TransposeChord(written);
            // A {define} applies to the chord as written, on a six-string guitar.
            var shape = _shift == 0 && options.Instrument.StringCount == 6 &&
                        song.ChordDefinitions.TryGetValue(written, out var defined) && defined.Frets.Count == 6
                ? defined
                : ChordShapeFinder.Find(name, options.Instrument);
            if (shape is null)
                continue;

            var diagram = new ChordDiagram(name, shape, FontSize) { Margin = new Thickness(0, 0, FontSize * 0.5, FontSize * 0.5) };
            Paint(diagram, ChordDiagram.ForegroundProperty, "TextBrush", options.Palette?.Text);
            Paint(diagram, ChordDiagram.AccentProperty, "ChordBrush", options.Palette?.Chord);
            panel.Children.Add(diagram);
        }

        return panel.Children.Count == 0 ? null : Block(panel);
    }

    // Splits sections across columns so each holds roughly the same number of lines.
    private Control RenderColumns(List<Section> sections)
    {
        var grid = new Grid();
        var weights = sections.Select(s => s.Lines.Count + 1).ToList();
        var perColumn = weights.Sum() / (double)options.Columns;
        var column = 0;
        var filled = 0.0;
        StackPanel? current = null;

        for (var i = 0; i < sections.Count; i++)
        {
            if (current is null || (filled + weights[i] / 2.0 > perColumn * (column + 1) && column < options.Columns - 1 && current.Children.Count > 0))
            {
                if (current is not null)
                    column++;
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                current = new StackPanel { Spacing = FontSize, Margin = new Thickness(column == 0 ? 0 : FontSize * 1.5, 0, 0, 0) };
                Grid.SetColumn(current, column);
                grid.Children.Add(current);
            }
            current.Children.Add(RenderSection(sections[i]));
            filled += weights[i];
        }

        return grid;
    }

    private Control RenderSection(Section section)
    {
        var mono = section.Kind is SectionKind.Tab or SectionKind.Grid;
        var body = new StackPanel();

        var label = section.Label ?? (section.Kind == SectionKind.Chorus ? "Chorus" : null);
        Control? labelBlock = label is null
            ? null
            : Text(label, FontSize * 0.85, FontWeight.SemiBold, opacity: 0.6, margin: new Thickness(0, 0, 0, 4));

        var lines = section.Lines
            .Where(l => options.ShowChords || l is not LyricLine { HasChords: true } line || !IsChordsOnly(line))
            .Select(l => RenderLine(l, mono))
            .ToList();

        // Keep a section label on the same page as the line after it.
        if (labelBlock is not null && lines.Count > 0)
        {
            var first = new StackPanel { Children = { labelBlock, lines[0] } };
            lines[0] = first;
        }
        else if (labelBlock is not null)
        {
            lines.Insert(0, labelBlock);
        }

        foreach (var line in lines)
            body.Children.Add(Block(line));

        if (section.Kind != SectionKind.Chorus)
            return body;

        var chorus = new Border
        {
            Child = body,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(FontSize * 0.75, 0, 0, 0),
        };
        Paint(chorus, Border.BorderBrushProperty, "ChorusBarBrush", options.Palette?.ChorusBar);
        return chorus;
    }

    private Control RenderLine(SongLine line, bool mono) => line switch
    {
        LyricLine { HasChords: true } lyrics when options.ShowChords => RenderChordedLine(lyrics),
        LyricLine lyrics => Text(string.Concat(lyrics.Segments.Select(s => s.Lyric)).Trim(), FontSize, wrap: true),
        PreformattedLine pre => Text(pre.Text, FontSize * 0.9, font: MonoFont),
        CommentLine comment => RenderComment(comment),
        BlankLine => new Control { Height = FontSize * 0.75 },
        _ => throw new ArgumentOutOfRangeException(nameof(line), line, null),
    };

    private static bool IsChordsOnly(LyricLine line) => line.Segments.All(s => string.IsNullOrWhiteSpace(s.Lyric));

    // A section with nothing to show once chords are hidden (e.g. an instrumental intro).
    private static bool IsChordsOnly(Section section) =>
        section.Kind is not (SectionKind.Tab or SectionKind.Grid) &&
        section.Lines.All(l => l is BlankLine || l is LyricLine line && IsChordsOnly(line));

    private Control RenderChordedLine(LyricLine line)
    {
        // One cell per word; the first word of each segment carries the chord above it.
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, FontSize * 0.25) };
        var chordsOnly = IsChordsOnly(line);

        foreach (var segment in line.Segments)
        {
            if (chordsOnly)
            {
                panel.Children.Add(ChordText(segment.Chord));
                continue;
            }

            var words = WordRegex().Matches(segment.Lyric)
                .Select(m => m.Value)
                .Where(w => w.Length > 0)
                .DefaultIfEmpty("")
                .ToList();

            for (var i = 0; i < words.Count; i++)
                panel.Children.Add(ChordCell(i == 0 ? segment.Chord : null, words[i]));
        }

        return panel;
    }

    private Control ChordCell(string? chord, string lyric)
    {
        // Use non-breaking spaces so trailing whitespace between words is measured and kept.
        var lyricText = Text(lyric.Length == 0 ? " " : lyric.Replace(' ', ' '), FontSize);

        return new StackPanel { Children = { ChordText(chord), lyricText } };
    }

    private TextBlock ChordText(string? chord)
    {
        var text = Text(chord is null ? "" : TransposeChord(chord), FontSize * 0.9, FontWeight.Bold);
        if (chord is not null)
        {
            text.Margin = new Thickness(0, 0, FontSize * 0.4, 0);
            Paint(text, TextBlock.ForegroundProperty, "ChordBrush", options.Palette?.Chord);
        }
        return text;
    }

    private string TransposeChord(string chord) => ChordTransposer.Transpose(chord, _shift, _preferFlats);

    private Control RenderComment(CommentLine comment)
    {
        var text = Text(comment.Text, FontSize * 0.95,
            comment.Style == CommentStyle.Normal ? FontWeight.SemiBold : FontWeight.Normal, wrap: true);
        if (comment.Style == CommentStyle.Italic)
            text.FontStyle = FontStyle.Italic;

        var border = new Border
        {
            Child = text,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 2, 0, 6),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (comment.Style == CommentStyle.Box)
        {
            border.BorderThickness = new Thickness(1);
            Paint(border, Border.BorderBrushProperty, "CommentBoxBrush", options.Palette?.CommentBox);
        }
        else if (comment.Style == CommentStyle.Normal)
        {
            Paint(border, Border.BackgroundProperty, "CommentBackgroundBrush", options.Palette?.CommentBackground);
        }
        return border;
    }

    private T Block<T>(T control) where T : Control
    {
        Blocks.Add(control);
        return control;
    }

    private TextBlock Text(string text, double size, FontWeight? weight = null, double opacity = 1,
        Thickness? margin = null, bool wrap = false, FontFamily? font = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Normal,
            Opacity = opacity,
            Margin = margin ?? default,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (font is not null)
            block.FontFamily = font;
        if (options.Palette is { } palette)
            block.Foreground = palette.Text;
        return block;
    }

    // Use the fixed palette colour if there is one; otherwise bind to the theme resource so colours
    // follow light/dark mode changes.
    private static void Paint(Control control, AvaloniaProperty property, string resourceKey, IBrush? fixedBrush)
    {
        if (fixedBrush is not null)
            control.SetValue(property, fixedBrush);
        else
            control.Bind(property, control.GetResourceObservable(resourceKey));
    }
}
