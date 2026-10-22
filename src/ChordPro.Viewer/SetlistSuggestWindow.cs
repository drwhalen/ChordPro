using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;

namespace ChordPro.Viewer;

/// <summary>
/// Suggests a set built around the songs in the setlist (the ones the player likes singing), and
/// shows how each song leads into the next. Before suggesting, it shows the flow of the setlist as
/// it is. A suggestion can be used as the setlist, or saved as one.
/// </summary>
public sealed class SetlistSuggestWindow : Window
{
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#2E9E4F"));
    private static readonly IBrush Fine = new SolidColorBrush(Color.Parse("#C99A1A"));
    private static readonly IBrush Bump = new SolidColorBrush(Color.Parse("#D0453A"));

    private readonly IReadOnlyList<SongInfo> _library;
    private readonly IReadOnlyList<PlannedSong> _picked;
    private readonly Func<SongInfo, PlannedSong> _toPlanned;
    private readonly Action<IReadOnlyList<string>> _use;
    private readonly Func<IReadOnlyList<string>, Task> _saveAs;
    private readonly SongPalette _palette;

    private readonly ComboBox _length = new() { ItemsSource = new[] { 6, 8, 10, 12, 15, 20 }, SelectedItem = 12, MinWidth = 70 };
    private readonly ComboBox _from = new() { MinWidth = 180 };
    private readonly CheckBox _nearKeys = new() { Content = "Stay near my songs' keys", IsChecked = true };
    private readonly CheckBox _favourPlayed = new() { Content = "Favour songs I play often" };
    private readonly Button _suggest = new() { Content = "Suggest", Classes = { "accent" } };
    private readonly TextBlock _heading = new() { FontWeight = FontWeight.SemiBold, FontSize = 15 };
    private readonly TextBlock _summary = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _rows = new() { Spacing = 2, Margin = new Thickness(0, 0, 16, 0) }; // clear of the scroll bar
    private readonly Button _useButton = new() { Content = "Use this setlist", IsEnabled = false };
    private readonly Button _saveButton = new() { Content = "Save as setlist…", IsEnabled = false };
    private SetlistPlan? _plan;
    private int _variation;

    public SetlistSuggestWindow() : this([], [], s => new PlannedSong(s, s.Key, s.Tempo, s.Capo), SongPalette.FromTheme(Core.Themes.SongTheme.Day), _ => { }, _ => Task.CompletedTask) { }

    /// <param name="setlist">The songs in the setlist now: kept in every suggestion.</param>
    /// <param name="toPlanned">How the player plays a song: the key heard (with their transpose), tempo, capo, plays.</param>
    public SetlistSuggestWindow(IReadOnlyList<SongInfo> library, IReadOnlyList<SongInfo> setlist, Func<SongInfo, PlannedSong> toPlanned,
        SongPalette palette, Action<IReadOnlyList<string>> use, Func<IReadOnlyList<string>, Task> saveAs)
    {
        _library = library;
        _toPlanned = toPlanned;
        _picked = [.. setlist.Select(toPlanned)];
        _use = use;
        _saveAs = saveAs;
        _palette = palette;

        Title = "Suggest a setlist";
        Width = 620;
        Height = 720;
        Background = palette.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _from.ItemsSource = new[] { "the whole library" }
            .Concat(library.SelectMany(s => s.Books).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        _from.SelectedIndex = 0;
        _nearKeys.IsEnabled = _picked.Count > 0;
        _suggest.Click += (_, _) => Suggest();
        _useButton.Click += (_, _) =>
        {
            if (_plan is not null)
                _use([.. _plan.Songs.Select(s => s.Song.Path)]);
            Close();
        };
        _saveButton.Click += async (_, _) =>
        {
            if (_plan is not null)
                await _saveAs([.. _plan.Songs.Select(s => s.Song.Path)]);
        };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();

        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Text = _picked.Count == 0
                ? "Add a few songs you like singing to the setlist first, and the set is built around them. Without any, it's built from the whole library."
                : $"Built around the {_picked.Count} {(_picked.Count == 1 ? "song" : "songs")} in your setlist: {string.Join(", ", _picked.Select(p => p.Song.Title))}.",
        };
        var options = new WrapPanel { Children = { Label("Songs"), _length, Label("from"), _from } };
        foreach (var child in options.Children)
            child.Margin = new Thickness(0, 0, 8, 6);
        var checks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _nearKeys, _favourPlayed } };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { intro, options, checks, _suggest, _heading, _summary } };
        DockPanel.SetDock(top, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { _useButton, _saveButton, close } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel
        {
            Margin = new Thickness(18, 14),
            Children = { top, buttons, new ScrollViewer { Content = _rows } },
        };

        // Before anything is suggested, show how the setlist flows as it is.
        if (_picked.Count >= 2)
            Show(SetlistPlanner.Describe(_picked), "Your setlist as it is");
        else
            _heading.Text = "";
    }

    private void Suggest()
    {
        var book = _from.SelectedIndex > 0 ? _from.SelectedItem as string : null;
        var pool = _library
            .Where(s => book is null || s.Books.Contains(book, StringComparer.CurrentCultureIgnoreCase))
            .Select(_toPlanned)
            .ToList();
        var plan = SetlistPlanner.Plan(new SetlistRequest(pool, _picked, (int)(_length.SelectedItem ?? 12),
            NearMustIncludeKeys: _nearKeys.IsChecked == true, FavourPlayed: _favourPlayed.IsChecked == true, Variation: _variation++));
        _suggest.Content = "Try another";
        Show(plan, "Suggested set");
        _useButton.IsEnabled = _saveButton.IsEnabled = plan.Songs.Count > 0;
    }

    private void Show(SetlistPlan plan, string heading)
    {
        _plan = plan;
        _heading.Text = heading;
        var counts = plan.Steps.GroupBy(s => s.Rating).ToDictionary(g => g.Key, g => g.Count());
        var unknown = plan.Songs.Count(s => s.Tempo is null);
        _summary.Text = $"{plan.Songs.Count} songs. Steps: {counts.GetValueOrDefault("good")} good, {counts.GetValueOrDefault("fine")} fine, {counts.GetValueOrDefault("bump")} bumps."
            + (unknown > 0 ? $" {unknown} without a tempo (tap one in to include it)." : "");

        _rows.Children.Clear();
        for (var i = 0; i < plan.Songs.Count; i++)
        {
            var song = plan.Songs[i];
            var picked = _picked.Any(p => p.Song.Path == song.Song.Path);
            var details = string.Join("   ", new[] { song.Key, song.Tempo is { } t ? $"{t:0} BPM" : null, song.Capo > 0 ? $"capo {song.Capo}" : null }.OfType<string>());
            _rows.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("28,*,Auto"),
                Margin = new Thickness(0, 4, 0, 0),
                Children =
                {
                    Text($"{i + 1}.", muted: true),
                    At(1, new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = song.Song.Title + (picked ? "  ★" : ""), FontWeight = FontWeight.SemiBold, Foreground = _palette.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                            Text(song.Song.Artist ?? "", muted: true, size: 12),
                        },
                    }),
                    At(2, Text(details, muted: true, size: 12)),
                },
            });
            if (i < plan.Steps.Count)
            {
                var step = plan.Steps[i];
                var colour = step.Rating switch { "good" => Good, "fine" => Fine, _ => Bump };
                _rows.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(28, 2, 0, 0),
                    Children =
                    {
                        new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = colour, VerticalAlignment = VerticalAlignment.Center },
                        Text(step.ToString(), muted: true, size: 12),
                    },
                });
            }
        }
    }

    private TextBlock Text(string text, bool muted = false, double size = 14) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = muted ? _palette.MutedText : _palette.Text,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = _palette.Text };

    private static Control At(int column, Control control)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
