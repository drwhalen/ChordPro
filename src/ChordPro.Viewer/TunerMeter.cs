using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ChordPro.Viewer;

/// <summary>A needle on a −50…+50 cent scale, with an in-tune zone in the middle.</summary>
public sealed class TunerMeter : Control
{
    public static readonly StyledProperty<double?> CentsProperty =
        AvaloniaProperty.Register<TunerMeter, double?>(nameof(Cents));

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<TunerMeter, IBrush>(nameof(Foreground), Brushes.Black);

    public static readonly StyledProperty<IBrush> NeedleProperty =
        AvaloniaProperty.Register<TunerMeter, IBrush>(nameof(Needle), Brushes.DarkOrange);

    public static readonly StyledProperty<IBrush> InTuneProperty =
        AvaloniaProperty.Register<TunerMeter, IBrush>(nameof(InTune), Brushes.Green);

    /// <summary>Cents within which a note counts as in tune.</summary>
    public const double InTuneCents = 5;

    static TunerMeter() => AffectsRender<TunerMeter>(CentsProperty, ForegroundProperty, NeedleProperty, InTuneProperty);

    /// <summary>How far off the note is, or null to hide the needle.</summary>
    public double? Cents { get => GetValue(CentsProperty); set => SetValue(CentsProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush Needle { get => GetValue(NeedleProperty); set => SetValue(NeedleProperty, value); }
    public IBrush InTune { get => GetValue(InTuneProperty); set => SetValue(InTuneProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width, 70);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        const double margin = 16, scaleY = 40;
        double X(double cents) => margin + (cents + 50) / 100 * (width - 2 * margin);

        var zone = new Rect(new Point(X(-InTuneCents), scaleY - 18), new Point(X(InTuneCents), scaleY + 6));
        context.DrawRectangle(InTune, null, zone, 3, 3);

        var line = new Pen(Foreground, 1);
        context.DrawLine(line, new Point(X(-50), scaleY), new Point(X(50), scaleY));
        for (var c = -50; c <= 50; c += 10)
        {
            var major = c % 50 == 0 || c == 0;
            context.DrawLine(line, new Point(X(c), scaleY - (major ? 12 : 7)), new Point(X(c), scaleY));
            if (c % 25 == 0 || c == 0)
            {
                var label = new FormattedText(c > 0 ? $"+{c}" : c.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, Foreground);
                context.DrawText(label, new Point(X(c) - label.Width / 2, scaleY + 8));
            }
        }

        if (Cents is { } cents)
        {
            var x = X(Math.Clamp(cents, -50, 50));
            var needle = Math.Abs(cents) <= InTuneCents ? InTune : Needle;
            context.DrawLine(new Pen(needle, 4, lineCap: PenLineCap.Round), new Point(x, scaleY - 30), new Point(x, scaleY + 2));
        }
    }
}
