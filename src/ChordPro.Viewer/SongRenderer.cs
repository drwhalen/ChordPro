using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;

namespace ChordPro.Viewer;

/// <summary>Builds a read-only visual tree for a <see cref="Song"/>, transposed by <paramref name="transpose"/> semitones.</summary>
public sealed partial class SongRenderer(double fontSize, int transpose = 0)
{
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    // A word plus the whitespace that follows it.
    [GeneratedRegex(@"\S*\s*")]
    private static partial Regex WordRegex();

    private bool? _preferFlats;

    public Control Render(Song song)
    {
        _preferFlats = ChordTransposer.PrefersFlats(song.Key, transpose);

        var root = new StackPanel { Spacing = fontSize };
        root.Children.Add(RenderHeader(song));

        foreach (var section in song.Sections)
            root.Children.Add(RenderSection(section));

        return root;
    }

    private Control RenderHeader(Song song)
    {
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, fontSize * 0.5) };

        if (song.Title is { } title)
            header.Children.Add(Text(title, fontSize * 1.75, FontWeight.Bold, wrap: true));
        if (song.Subtitle is { } subtitle)
            header.Children.Add(Text(subtitle, fontSize * 1.1, opacity: 0.8, wrap: true));
        if (song.Artist is { } artist)
            header.Children.Add(Text(artist, fontSize, opacity: 0.8, wrap: true));

        var details = new[] { ("Key", song.Key is { } key ? TransposeChord(key) : null),("Capo", song.Capo), ("Tempo", song.Tempo), ("Time", song.Time) }
            .Where(d => d.Item2 is not null)
            .Select(d => $"{d.Item1}: {d.Item2}")
            .ToList();
        if (details.Count > 0)
            header.Children.Add(Text(string.Join("   ·   ", details), fontSize * 0.9, opacity: 0.6, wrap: true));

        return header;
    }

    private Control RenderSection(Section section)
    {
        var mono = section.Kind is SectionKind.Tab or SectionKind.Grid;
        var body = new StackPanel();

        var label = section.Label ?? (section.Kind == SectionKind.Chorus ? "Chorus" : null);
        if (label is not null)
            body.Children.Add(Text(label, fontSize * 0.85, FontWeight.SemiBold, opacity: 0.6, margin: new Thickness(0, 0, 0, 4)));

        foreach (var line in section.Lines)
            body.Children.Add(RenderLine(line, mono));

        if (section.Kind != SectionKind.Chorus)
            return body;

        var chorus = new Border
        {
            Child = body,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(fontSize * 0.75, 0, 0, 0),
        };
        Bind(chorus, Border.BorderBrushProperty, "ChorusBarBrush");
        return chorus;
    }

    private Control RenderLine(SongLine line, bool mono) => line switch
    {
        LyricLine { HasChords: true } lyrics => RenderChordedLine(lyrics),
        LyricLine lyrics => Text(string.Concat(lyrics.Segments.Select(s => s.Lyric)), fontSize, wrap: true),
        PreformattedLine pre => Text(pre.Text, fontSize * 0.9, font: MonoFont),
        CommentLine comment => RenderComment(comment),
        BlankLine => new Control { Height = fontSize * 0.75 },
        _ => throw new ArgumentOutOfRangeException(nameof(line), line, null),
    };

    private Control RenderChordedLine(LyricLine line)
    {
        // One cell per word; the first word of each segment carries the chord above it.
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, fontSize * 0.25) };
        var chordsOnly = line.Segments.All(s => string.IsNullOrWhiteSpace(s.Lyric));

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
        var lyricText = Text(lyric.Length == 0 ? " " : lyric.Replace(' ', ' '), fontSize);

        return new StackPanel { Children = { ChordText(chord), lyricText } };
    }

    private TextBlock ChordText(string? chord)
    {
        var text = Text(chord is null ? "" : TransposeChord(chord), fontSize * 0.9, FontWeight.Bold);
        if (chord is not null)
        {
            text.Margin = new Thickness(0, 0, fontSize * 0.4, 0);
            Bind(text, TextBlock.ForegroundProperty, "ChordBrush");
        }
        return text;
    }

    private string TransposeChord(string chord) => ChordTransposer.Transpose(chord, transpose, _preferFlats);

    private Control RenderComment(CommentLine comment)
    {
        var text = Text(comment.Text, fontSize * 0.95,
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
            Bind(border, Border.BorderBrushProperty, "CommentBoxBrush");
        }
        else if (comment.Style == CommentStyle.Normal)
        {
            Bind(border, Border.BackgroundProperty, "CommentBackgroundBrush");
        }
        return border;
    }

    private static TextBlock Text(string text, double size, FontWeight? weight = null, double opacity = 1,
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
        return block;
    }

    // Bind to a theme resource so colours follow light/dark mode changes.
    private static void Bind(Control control, AvaloniaProperty property, string resourceKey) =>
        control.Bind(property, control.GetResourceObservable(resourceKey));
}
