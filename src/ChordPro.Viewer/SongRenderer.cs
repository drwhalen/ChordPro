using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.Core.Chords;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

/// <summary>The brushes a song is drawn with, made from a <see cref="SongTheme"/>.</summary>
public sealed record SongPalette(IBrush Background, IBrush Text, IBrush MutedText, IBrush Chord,
    IBrush ChorusBar, IBrush CommentBackground, IBrush CommentBox)
{
    /// <summary>Printing always uses the Day colours: dark text on white paper.</summary>
    public static SongPalette Print { get; } = FromTheme(SongTheme.Day);

    public static SongPalette FromTheme(SongTheme theme)
    {
        static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex)).ToImmutable();
        return new SongPalette(Brush(theme.Background), Brush(theme.Text), Brush(theme.MutedText), Brush(theme.Chord),
            Brush(theme.ChorusBar), Brush(theme.CommentBackground), Brush(theme.CommentBox));
    }
}

/// <summary>A chord the user tapped: its name as shown, and the song's own fingering for it if that applies.</summary>
public sealed record ChordTap(string Name, ChordShape? DefinedShape, Control Anchor);

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
    public SongPalette Palette { get; init; } = SongPalette.FromTheme(SongTheme.Day);
    /// <summary>Show easier forms of chords (Cmaj9 → C, Am7 → Am, G/B → G).</summary>
    public bool SimplifyChords { get; init; }
    /// <summary>The file's <c>{key}</c> is the key heard with its capo on (LivePrompter), not the key of the chords as written.</summary>
    public bool KeyIncludesCapo { get; init; }
    /// <summary>Called when a chord or diagram is tapped; null makes chords not tappable (e.g. printing).</summary>
    public Action<ChordTap>? ChordTapped { get; init; }
}

/// <summary>Builds a read-only visual tree for a <see cref="Song"/>.</summary>
public sealed partial class SongRenderer(RenderOptions options)
{
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    // A word plus the whitespace that follows it.
    [GeneratedRegex(@"\S*\s*")]
    private static partial Regex WordRegex();

    private double FontSize => options.FontSize;
    private SongPalette Palette => options.Palette;

    private int _shift;
    private bool? _preferFlats;
    private string? _writtenKey;
    private Song? _song;

    /// <summary>
    /// The rendered song as page-layout groups, in display order: the header, the chord diagrams,
    /// then one group per section holding its lines (each line is unbreakable, so a chord stays
    /// with its lyric). Used to choose page breaks and page turns.
    /// </summary>
    public List<(IReadOnlyList<Control> Lines, bool KeepWithNext)> Groups { get; } = [];

    /// <summary>The control showing each line, keyed by section and line index (hidden lines are absent).</summary>
    public Dictionary<(Section, int), Control> LineControls { get; } = [];

    /// <summary>The written capo from the file (0 if none).</summary>
    public static int FileCapo(Song song) => song.CapoFret;

    public Control Render(Song song)
    {
        // Chords are written as shapes for the file's capo; re-spell them for the player's capo and transposition.
        var fileCapo = FileCapo(song);
        _shift = options.Transpose + fileCapo - (options.Capo ?? fileCapo);
        _writtenKey = ChordTransposer.WrittenKey(song.Key, fileCapo, options.KeyIncludesCapo);
        _preferFlats = ChordTransposer.PrefersFlats(_writtenKey, _shift);
        _song = song;
        Groups.Clear();
        LineControls.Clear();

        // The title and the chord diagrams stay on the same page as the start of the song.
        var root = new StackPanel { Spacing = FontSize };
        var header = RenderHeader(song, fileCapo);
        root.Children.Add(header);
        Groups.Add(([header], true));

        if (options.ShowChords && options.ShowDiagrams && RenderDiagrams(song) is { } diagrams)
        {
            root.Children.Add(diagrams);
            Groups.Add(([diagrams], true));
        }

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
            header.Children.Add(Text(subtitle, FontSize * 1.1, muted: true, wrap: true));
        if (song.Artist is { } artist)
            header.Children.Add(Text(artist, FontSize, muted: true, wrap: true));

        var capo = options.Capo ?? fileCapo;
        var details = new List<string>();
        if (song.Key is { } key)
        {
            // Show what the audience hears: the written key sounds a capo's worth higher. A key that
            // already includes the capo keeps the file's own spelling unless the song is transposed.
            details.Add($"Key: {ChordTransposer.Shift(key, options.Transpose + (options.KeyIncludesCapo ? 0 : fileCapo))}");
        }
        if (capo > 0 || song.Capo is not null)
            details.Add(capo > 0 && _writtenKey is { } k ? $"Capo: {capo} (play {TransposeChord(k)} shapes)" : $"Capo: {capo}");
        if (song.Tempo is { } tempo)
            details.Add($"Tempo: {tempo}");
        if (song.Time is { } time)
            details.Add($"Time: {time}");
        if (details.Count > 0)
            header.Children.Add(Text(string.Join("   ·   ", details), FontSize * 0.9, muted: true, wrap: true));

        return header;
    }

    private Control? RenderDiagrams(Song song)
    {
        // One diagram per chord as shown (after simplifying, Am7 and Am are the same chord).
        var chords = song.ChordUsages()
            .Select(written => (Written: written, Name: DisplayChord(written)))
            .DistinctBy(c => c.Name)
            .ToList();

        var panel = new WrapPanel();
        foreach (var (written, name) in chords)
        {
            var defined = DefinedShape(song, written, name);
            var shape = defined ?? ChordShapeFinder.Find(name, options.Instrument);
            if (shape is null)
                continue;

            var diagram = new ChordDiagram(name, shape, FontSize)
            {
                Margin = new Thickness(0, 0, FontSize * 0.5, FontSize * 0.5),
                Foreground = Palette.Text,
                Accent = Palette.Chord,
            };
            MakeTappable(diagram, name, defined);
            panel.Children.Add(diagram);
        }

        return panel.Children.Count == 0 ? null : panel;
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
            : Text(label, FontSize * 0.85, FontWeight.SemiBold, muted: true, margin: new Thickness(0, 0, 0, 4));

        var shown = section.Lines
            .Select((line, index) => (Line: line, Index: index))
            .Where(l => options.ShowChords || l.Line is not LyricLine { HasChords: true } line || !IsChordsOnly(line))
            .ToList();
        var lines = shown.Select(l => RenderLine(l.Line, mono)).ToList();

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
        for (var i = 0; i < shown.Count; i++)
            LineControls[(section, shown[i].Index)] = lines[i];

        foreach (var line in lines)
            body.Children.Add(line);
        // A section of only comments (like "Solo" before an instrumental line) leads into the next one.
        var leadIn = section.Lines.All(l => l is CommentLine or BlankLine);
        if (lines.Count > 0)
            Groups.Add((lines, leadIn));

        if (section.Kind != SectionKind.Chorus)
            return body;

        var chorus = new Border
        {
            Child = body,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(FontSize * 0.75, 0, 0, 0),
            BorderBrush = Palette.ChorusBar,
        };
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
        var name = chord is null ? "" : DisplayChord(chord);
        var text = Text(name, FontSize * 0.9, FontWeight.Bold);
        if (chord is not null)
        {
            text.Margin = new Thickness(0, 0, FontSize * 0.4, 0);
            text.Foreground = Palette.Chord;
            if (name.Length > 0)
                MakeTappable(text, name, DefinedShape(_song!, chord, name));
        }
        return text;
    }

    private string TransposeChord(string chord) => ChordTransposer.Transpose(chord, _shift, _preferFlats);

    // The chord as it should be shown: simplified if asked, then moved for transposition and capo.
    private string DisplayChord(string written) =>
        TransposeChord(options.SimplifyChords ? ChordSimplifier.Simplify(written) : written);

    // A {define} applies to the chord exactly as written, on a six-string guitar.
    private ChordShape? DefinedShape(Song song, string written, string shown) =>
        shown == written && options.Instrument.StringCount == 6 &&
        song.ChordDefinitions.TryGetValue(written, out var defined) && defined.Frets.Count == 6
            ? defined
            : null;

    private void MakeTappable(Control control, string name, ChordShape? defined)
    {
        if (options.ChordTapped is not { } tapped)
            return;
        // The "chord" class lets page-turn taps ignore taps on chords.
        control.Classes.Add("chord");
        control.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(control, $"Show {name} fingerings");
        control.Tapped += (_, e) =>
        {
            tapped(new ChordTap(name, defined, control));
            e.Handled = true;
        };
    }

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
            border.BorderBrush = Palette.CommentBox;
        }
        else if (comment.Style == CommentStyle.Normal)
        {
            border.Background = Palette.CommentBackground;
        }
        return border;
    }

    private TextBlock Text(string text, double size, FontWeight? weight = null, bool muted = false,
        Thickness? margin = null, bool wrap = false, FontFamily? font = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = muted ? Palette.MutedText : Palette.Text,
            Margin = margin ?? default,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (font is not null)
            block.FontFamily = font;
        return block;
    }
}
