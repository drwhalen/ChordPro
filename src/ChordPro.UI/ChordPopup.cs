using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core.Chords;

namespace ChordPro.UI;

/// <summary>A chord's diagram with arrows to page through alternate fingerings.</summary>
public sealed class ChordPopup : UserControl
{
    private const double DiagramScale = 1.6;

    private readonly string _name;
    private readonly IReadOnlyList<(ChordShape Shape, string? Source)> _shapes;
    private readonly double _fontSize;
    private readonly SongPalette _palette;
    private readonly Instrument _instrument;
    private readonly Border _diagramHost = new();
    private readonly TextBlock _caption = new() { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _frets = new() { HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _counter = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _previous = new() { Content = "◀" };
    private readonly Button _next = new() { Content = "▶" };
    private readonly Button _use = new() { Content = "Use this fingering", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _forget = new() { Content = "Use the usual fingering", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Action<ChordShape>? _useShape;
    private int _index;

    /// <param name="shapes">Fingerings to show, best first; <c>Source</c> labels where one came from (e.g. "From the song").</param>
    /// <param name="use">Saves the fingering shown as the song's own; null when it can't be (see <paramref name="cantUse"/>).</param>
    /// <param name="forget">Drops the song's own fingering, going back to the usual one; null when it has none.</param>
    /// <param name="cantUse">Why a fingering can't be saved, shown on the disabled button.</param>
    public ChordPopup(string name, IReadOnlyList<(ChordShape Shape, string? Source)> shapes, double fontSize, SongPalette palette,
        Instrument? instrument = null, Action<ChordShape>? use = null, Action? forget = null, string? cantUse = null)
    {
        _instrument = instrument ?? Instrument.Guitar;
        _name = name;
        _shapes = shapes;
        _fontSize = fontSize;
        _palette = palette;

        _useShape = use;
        _use.Click += (_, _) => _useShape?.Invoke(_shapes[_index].Shape);
        ToolTip.SetTip(_use, cantUse ?? "Save it in the song, so it's the one shown next time (and in other ChordPro apps)");
        _forget.IsVisible = forget is not null;
        _forget.Click += (_, _) => forget?.Invoke();
        ToolTip.SetTip(_forget, "Take the song's own fingering out, and show the usual one");

        ToolTip.SetTip(_previous, "Previous fingering");
        ToolTip.SetTip(_next, "Next fingering");
        _previous.Click += (_, _) => Show(_index - 1);
        _next.Click += (_, _) => Show(_index + 1);

        var pager = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        pager.Children.Add(_previous);
        Grid.SetColumn(_counter, 1);
        pager.Children.Add(_counter);
        Grid.SetColumn(_next, 2);
        pager.Children.Add(_next);

        _diagramHost.Background = palette.Background;
        _diagramHost.CornerRadius = new CornerRadius(6);
        _diagramHost.Padding = new Thickness(12, 8, 12, 4);
        _diagramHost.HorizontalAlignment = HorizontalAlignment.Center;

        Content = shapes.Count == 0
            ? new TextBlock
            {
                Text = _instrument.IsDiatonic
                    ? $"{name} isn't on the {_instrument.Name}'s frets."
                    : $"No fingering found for {name}.",
                Margin = new Thickness(4),
            }
            : new StackPanel { MinWidth = 190, Spacing = 4, Children = { _diagramHost, _caption, _frets, pager, _use, _forget } };

        if (shapes.Count > 0)
            Show(0);
    }

    private void Show(int index)
    {
        _index = (index + _shapes.Count) % _shapes.Count;
        var (shape, source) = _shapes[_index];

        _diagramHost.Child = new ChordDiagram(_name, shape, _fontSize * DiagramScale, _instrument)
        {
            Foreground = _palette.Text,
            Accent = _palette.Chord,
        };
        _caption.Text = source ?? Describe(shape);
        _frets.Text = _instrument.Notation(shape);
        _counter.Text = $"{_index + 1} of {_shapes.Count}";
        _previous.IsEnabled = _next.IsEnabled = _shapes.Count > 1;
        // The song's own fingering is already the one used.
        _use.IsEnabled = _useShape is not null && source != "From the song";
    }

    private string Describe(ChordShape shape)
    {
        var position = shape.BaseFret == 1
            ? (shape.Frets.Contains(0) ? "Open position" : "First position")
            : $"Fret {_instrument.FretName(shape.BaseFret)}";
        return ChordDifficulty.NeedsBarre(shape) ? $"{position} · barre" : position;
    }
}
