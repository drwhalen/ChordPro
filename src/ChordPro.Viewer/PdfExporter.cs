using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia.Helpers;
using Avalonia.VisualTree;
using ChordPro.Core;
using ChordPro.Core.Layout;
using SkiaSharp;

namespace ChordPro.Viewer;

/// <summary>One entry in a setlist booklet: a song as the player plays it, or a divider between sets.</summary>
public sealed record BookletEntry(string Title, string? Artist, string? Key, int Capo, double? Tempo, Song? Song, RenderOptions? Options)
{
    public bool IsDivider => Song is null;

    /// <summary>How long the song lasts (its <c>{duration}</c>), if it says.</summary>
    public TimeSpan? Length { get; init; }
}

/// <summary>
/// Writes songs to a vector PDF (US Letter or A4, following the user's region), ready to print: one
/// song, or a whole setlist as a booklet — a page listing the set, then each song from a new page.
/// </summary>
public static class PdfExporter
{
    private const float Margin = 48;          // points
    private const float FooterHeight = 18;    // points
    private const float PointsPerDip = 72f / 96f;
    private const double PrintFontSize = 14;  // DIPs, about 10.5pt

    private static (float Width, float Height) PageSize => RegionInfo.CurrentRegion.IsMetric ? (595f, 842f) : (612f, 792f);
    private static double ContentWidth => (PageSize.Width - 2 * Margin) / PointsPerDip;
    private static double ContentHeight => (PageSize.Height - 2 * Margin - FooterHeight) / PointsPerDip;

    public static async Task ExportAsync(Song song, RenderOptions options, string path)
    {
        var title = song.Title ?? Path.GetFileNameWithoutExtension(path);
        await using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = title, Creator = "ChordPro Viewer" });
        await WriteSongAsync(document, song, options, title);
        document.Close();
    }

    /// <summary>A setlist as one PDF: the list of songs with key, capo and tempo, then every song.</summary>
    public static async Task ExportSetlistAsync(string name, IReadOnlyList<BookletEntry> entries, string path)
    {
        await using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = name, Creator = "ChordPro Viewer" });

        var (cover, rows) = Cover(name, entries);
        await WriteAsync(document, cover, [.. rows.Select(r => new PageGroup([new LineExtent(TopOf(r, cover), TopOf(r, cover) + r.Bounds.Height)], false))], name);

        var number = 0;
        foreach (var entry in entries.Where(e => !e.IsDivider))
            await WriteSongAsync(document, entry.Song!, entry.Options!, $"{++number}. {entry.Title}");
        document.Close();
    }

    // Adds a song's pages, starting on a new page, with the footer "<footer>  ·  page / pages".
    private static async Task WriteSongAsync(SKDocument document, Song song, RenderOptions options, string footer)
    {
        var renderer = new SongRenderer(options with { Palette = SongPalette.Print, Columns = 1, FontSize = PrintFontSize, ChordTapped = null });
        var root = renderer.Render(song);
        root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, ContentWidth, root.DesiredSize.Height));
        await WriteAsync(document, root, PageGroups(renderer, root), footer);
    }

    // Draws root once, then places a slice of it on each page, breaking where the groups allow.
    private static async Task WriteAsync(SKDocument document, Control root, IReadOnlyList<PageGroup> groups, string footer)
    {
        if (root.Bounds.Width == 0)
        {
            root.Measure(new Size(ContentWidth, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, ContentWidth, root.DesiredSize.Height));
        }
        var totalHeight = root.Bounds.Height;
        var (pageWidth, pageHeight) = PageSize;

        using var recorder = new SKPictureRecorder();
        var recording = recorder.BeginRecording(new SKRect(0, 0, (float)ContentWidth, (float)totalHeight));
        await DrawingContextHelper.RenderAsync(recording, root, new Rect(root.Bounds.Size), new Vector(96, 96));
        using var picture = recorder.EndRecording();

        var starts = PageLayout.PageStarts([.. groups], ContentHeight);
        using var footerPaint = new SKPaint { Color = new SKColor(0x71, 0x71, 0x7A), TextSize = 8, IsAntialias = true };
        for (var page = 0; page < starts.Count; page++)
        {
            var top = starts[page];
            var bottom = page + 1 < starts.Count ? starts[page + 1] : totalHeight;
            var canvas = document.BeginPage(pageWidth, pageHeight);

            canvas.Save();
            canvas.Translate(Margin, Margin);
            canvas.Scale(PointsPerDip);
            canvas.ClipRect(new SKRect(0, 0, (float)ContentWidth, (float)(bottom - top)));
            canvas.Translate(0, (float)-top);
            canvas.DrawPicture(picture);
            canvas.Restore();

            if (starts.Count > 1)
            {
                var text = $"{footer}  ·  {page + 1} / {starts.Count}";
                canvas.DrawText(text, pageWidth - Margin - footerPaint.MeasureText(text), pageHeight - Margin + 4, footerPaint);
            }
            document.EndPage();
        }
    }

    // The booklet's first page: the set's name and date, then a row per song (and per divider).
    private static (Control Root, List<Control> Rows) Cover(string name, IReadOnlyList<BookletEntry> entries)
    {
        var palette = SongPalette.Print;
        TextBlock Cell(string text, double size = PrintFontSize, FontWeight weight = FontWeight.Normal, bool muted = false) => new()
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = muted ? palette.MutedText : palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid Row(params Control[] cells)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,56,56,72,56"), Margin = new Thickness(0, 3) };
            for (var i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                grid.Children.Add(cells[i]);
            }
            return grid;
        }

        var root = new StackPanel { Spacing = 2 };
        var rows = new List<Control>();
        void Add(Control row)
        {
            root.Children.Add(row);
            rows.Add(row);
        }
        Add(new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14),
            Children =
            {
                Cell(name, PrintFontSize * 1.9, FontWeight.Bold),
                Cell($"{SetLength.Of(entries.Where(e => !e.IsDivider).Select(e => e.Length)).ToString().Replace(" · ", "  ·  ")}  ·  {DateTime.Now:D}",
                    PrintFontSize * 0.95, muted: true),
            },
        });
        Add(Row(Cell("#", 11, muted: true), Cell("Song", 11, muted: true), Cell("Key", 11, muted: true), Cell("Capo", 11, muted: true),
            Cell("Tempo", 11, muted: true), Cell("Length", 11, muted: true)));
        var number = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsDivider)
            {
                // The set's name, and how long it runs: its songs up to the next divider.
                var set = SetLength.Of(entries.Skip(i + 1).TakeWhile(e => !e.IsDivider).Select(e => e.Length));
                Add(new DockPanel
                {
                    Margin = new Thickness(0, 10, 0, 2),
                    Children =
                    {
                        Docked(Cell(set.Songs > 0 ? set.ToString() : "", 11, muted: true), Dock.Right),
                        Cell(entry.Title, PrintFontSize * 1.05, FontWeight.Bold),
                    },
                });
                continue;
            }
            var song = new StackPanel { Children = { Cell(entry.Title, weight: FontWeight.SemiBold) } };
            if (entry.Artist is { } artist)
                song.Children.Add(Cell(artist, 11, muted: true));
            Add(Row(Cell($"{++number}.", muted: true), song, Cell(entry.Key ?? "–"), Cell(entry.Capo > 0 ? entry.Capo.ToString() : "–"),
                Cell(entry.Tempo is { } t ? $"{t:0} BPM" : "–"), Cell(entry.Length is { } l ? $"{(int)l.TotalMinutes}:{l.Seconds:00}" : "–")));
        }
        root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, ContentWidth, root.DesiredSize.Height));
        return (root, rows);
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>The renderer's groups with each line's position within <paramref name="root"/>.</summary>
    public static List<PageGroup> PageGroups(SongRenderer renderer, Visual root) =>
        [.. renderer.Groups.Select(g => new PageGroup(
            [.. g.Lines.Select(line => new LineExtent(TopOf(line, root), TopOf(line, root) + line.Bounds.Height))],
            g.KeepWithNext))];

    public static double TopOf(Visual visual, Visual root)
    {
        var y = 0.0;
        for (Visual? v = visual; v is not null && v != root; v = v.GetVisualParent())
            y += v.Bounds.Y;
        return y;
    }
}
