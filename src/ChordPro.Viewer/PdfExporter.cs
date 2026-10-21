using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Skia.Helpers;
using Avalonia.VisualTree;
using ChordPro.Core;
using ChordPro.Core.Layout;
using SkiaSharp;

namespace ChordPro.Viewer;

/// <summary>Writes a song to a vector PDF (US Letter or A4, following the user's region), ready to print.</summary>
public static class PdfExporter
{
    private const float Margin = 48;          // points
    private const float FooterHeight = 18;    // points
    private const float PointsPerDip = 72f / 96f;
    private const double PrintFontSize = 14;  // DIPs, about 10.5pt

    public static async Task ExportAsync(Song song, RenderOptions options, string path)
    {
        var (pageWidth, pageHeight) = RegionInfo.CurrentRegion.IsMetric ? (595f, 842f) : (612f, 792f);
        var contentWidth = (pageWidth - 2 * Margin) / PointsPerDip;
        var contentHeight = (pageHeight - 2 * Margin - FooterHeight) / PointsPerDip;

        var renderer = new SongRenderer(options with { Palette = SongPalette.Print, Columns = 1, FontSize = PrintFontSize });
        var root = renderer.Render(song);
        root.Measure(new Size(contentWidth, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, contentWidth, root.DesiredSize.Height));
        var totalHeight = root.Bounds.Height;

        // Draw the whole song once, then place a slice of it on each page.
        using var recorder = new SKPictureRecorder();
        var recording = recorder.BeginRecording(new SKRect(0, 0, (float)contentWidth, (float)totalHeight));
        await DrawingContextHelper.RenderAsync(recording, root, new Rect(root.Bounds.Size), new Vector(96, 96));
        using var picture = recorder.EndRecording();

        var starts = PageLayout.PageStarts(PageGroups(renderer, root), contentHeight);
        var title = song.Title ?? Path.GetFileNameWithoutExtension(path);

        await using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = title, Creator = "ChordPro Viewer" });
        using var footerPaint = new SKPaint { Color = new SKColor(0x71, 0x71, 0x7A), TextSize = 8, IsAntialias = true };

        for (var page = 0; page < starts.Count; page++)
        {
            var top = starts[page];
            var bottom = page + 1 < starts.Count ? starts[page + 1] : totalHeight;
            var canvas = document.BeginPage(pageWidth, pageHeight);

            canvas.Save();
            canvas.Translate(Margin, Margin);
            canvas.Scale(PointsPerDip);
            canvas.ClipRect(new SKRect(0, 0, (float)contentWidth, (float)(bottom - top)));
            canvas.Translate(0, (float)-top);
            canvas.DrawPicture(picture);
            canvas.Restore();

            if (starts.Count > 1)
            {
                var footer = $"{title}  ·  {page + 1} / {starts.Count}";
                canvas.DrawText(footer, pageWidth - Margin - footerPaint.MeasureText(footer), pageHeight - Margin + 4, footerPaint);
            }

            document.EndPage();
        }

        document.Close();
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
