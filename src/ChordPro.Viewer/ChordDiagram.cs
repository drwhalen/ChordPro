using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ChordPro.Core.Chords;

namespace ChordPro.Viewer;

/// <summary>Draws a fretboard diagram for one chord shape, with the chord name above it.</summary>
public sealed class ChordDiagram : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ChordDiagram, IBrush?>(nameof(Foreground), Brushes.Black);

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<ChordDiagram, IBrush?>(nameof(Accent), Brushes.DarkOrange);

    private const int MinFrets = 4;

    private readonly string _name;
    private readonly ChordShape _shape;
    private readonly Instrument _instrument;
    private readonly double _scale;

    static ChordDiagram() => AffectsRender<ChordDiagram>(ForegroundProperty, AccentProperty);

    /// <param name="instrument">Names the frets (the Merlin's "6+") and marks paired strings; guitar if not given.</param>
    public ChordDiagram(string name, ChordShape shape, double fontSize, Instrument? instrument = null)
    {
        _name = name;
        _shape = shape;
        _instrument = instrument ?? Instrument.Guitar;
        _scale = fontSize / 16;
        ToolTip.SetTip(this, $"{name}: {_instrument.Notation(shape)}");
    }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    private double StringSpacing => 12 * _scale;
    private double FretSpacing => 14 * _scale;
    private double NameHeight => 20 * _scale;
    private double MarkerHeight => 12 * _scale;
    private double SideMargin => 16 * _scale;
    // Room on the left for the starting fret's number ("12", "6+") clear of the dots.
    private double LeftMargin => (_shape.BaseFret == 1 ? 16 : 24) * _scale;

    private int FretCount
    {
        get
        {
            var fretted = _shape.Frets.Where(f => f > 0).ToList();
            return fretted.Count == 0 ? MinFrets : Math.Max(MinFrets, fretted.Max() - _shape.BaseFret + 1);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        LeftMargin + SideMargin + StringSpacing * (_shape.Frets.Count - 1),
        NameHeight + MarkerHeight + FretSpacing * FretCount + 4 * _scale);

    public override void Render(DrawingContext context)
    {
        var foreground = Foreground ?? Brushes.Black;
        var accent = Accent ?? foreground;
        var strings = _shape.Frets.Count;
        var left = LeftMargin;
        var top = NameHeight + MarkerHeight;
        var width = StringSpacing * (strings - 1);
        var height = FretSpacing * FretCount;
        var thin = new Pen(foreground, Math.Max(1, _scale * 0.8));
        var baseFret = _shape.BaseFret;

        var name = Format(_name, 14 * _scale, accent, FontWeight.Bold);
        context.DrawText(name, new Point(left + width / 2 - name.Width / 2, 0));

        // Nut (or the starting fret number when the shape is further up the neck).
        if (baseFret == 1)
            context.DrawLine(new Pen(foreground, 3 * _scale), new Point(left, top), new Point(left + width, top));
        else
        {
            var label = Format(_instrument.FretName(baseFret), 10 * _scale, foreground);
            context.DrawText(label, new Point(left - StringSpacing * 0.38 - label.Width - 3 * _scale, top + FretSpacing / 2 - label.Height / 2));
        }

        for (var fret = 0; fret <= FretCount; fret++)
            context.DrawLine(thin, new Point(left, top + fret * FretSpacing), new Point(left + width, top + fret * FretSpacing));
        for (var s = 0; s < strings; s++)
        {
            var x = left + s * StringSpacing;
            if (_instrument.DoubledStrings.Contains(s) && _instrument.StringCount == strings)
            {
                // A pair of strings played as one (the Merlin's melody strings): two close lines.
                var gap = Math.Max(1.5, StringSpacing * 0.12);
                context.DrawLine(thin, new Point(x - gap, top), new Point(x - gap, top + height));
                context.DrawLine(thin, new Point(x + gap, top), new Point(x + gap, top + height));
            }
            else
                context.DrawLine(thin, new Point(x, top), new Point(x, top + height));
        }

        var dotRadius = StringSpacing * 0.38;
        for (var s = 0; s < strings; s++)
        {
            var x = left + s * StringSpacing;
            var fret = _shape.Frets[s];
            if (fret <= 0)
            {
                var marker = Format(fret < 0 ? "×" : "○", 10 * _scale, foreground);
                context.DrawText(marker, new Point(x - marker.Width / 2, top - MarkerHeight - 1 * _scale));
                continue;
            }
            var y = top + (fret - baseFret + 0.5) * FretSpacing;
            context.DrawEllipse(foreground, null, new Point(x, y), dotRadius, dotRadius);
        }

        DrawBarre(context, foreground, left, top, baseFret, dotRadius);
    }

    // A bar across the strings held by one finger at the lowest fret, drawn only when the shape
    // needs more than four fingers without it (so D's xx0232 isn't shown as a barre).
    private void DrawBarre(DrawingContext context, IBrush brush, double left, double top, int baseFret, double radius)
    {
        var fretted = _shape.Frets.Where(f => f > 0).ToList();
        if (fretted.Count <= 4)
            return;
        var lowest = fretted.Min();
        var first = _shape.Frets.ToList().IndexOf(lowest);
        var last = _shape.Frets.ToList().LastIndexOf(lowest);
        if (first == last || _shape.Frets.Skip(first).Any(f => f < lowest))
            return;

        var y = top + (lowest - baseFret + 0.5) * FretSpacing;
        var rect = new Rect(left + first * StringSpacing - radius, y - radius,
            (last - first) * StringSpacing + radius * 2, radius * 2);
        context.DrawRectangle(brush, null, rect, radius, radius);
    }

    private static FormattedText Format(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
}
