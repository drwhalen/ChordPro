using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.Core.Themes;

namespace ChordPro.Viewer;

/// <summary>Creates or edits a custom theme, with a live preview and contrast checks.</summary>
public partial class ThemeEditorWindow : Window
{
    private const string PreviewSong = """
        {title: Preview Song}
        {subtitle: How your theme looks}
        {key: G}
        {c: Intro}
        [G] [C] [D]
        {start_of_verse: Verse}
        [G]Lyrics sit be[C]low the chords
        A [Em]second line of [D]words
        {end_of_verse}
        {start_of_chorus}
        [C]This is the [G]chorus
        [D]Marked by a bar
        {end_of_chorus}
        {cb: Boxed comment}
        """;

    // The editable colours, their labels, and what each must stay readable against.
    private sealed record ColorRole(string Label, Func<SongTheme, string> Get, Action<SongTheme, string> Set,
        Func<SongTheme, string>? Against = null);

    private static readonly ColorRole[] Roles =
    [
        new("Background", t => t.Background, (t, c) => t.Background = c),
        new("Lyrics", t => t.Text, (t, c) => t.Text = c, t => t.Background),
        new("Chords", t => t.Chord, (t, c) => t.Chord = c, t => t.Background),
        new("Labels and details", t => t.MutedText, (t, c) => t.MutedText = c, t => t.Background),
        new("Chorus bar", t => t.ChorusBar, (t, c) => t.ChorusBar = c),
        new("Comment highlight", t => t.CommentBackground, (t, c) => t.CommentBackground = c),
        new("Comment box border", t => t.CommentBox, (t, c) => t.CommentBox = c),
    ];

    private readonly SongTheme _theme;
    private readonly string? _originalName;
    private readonly IReadOnlySet<string> _takenNames;
    private readonly Song _previewSong = ChordProParser.Parse(PreviewSong);
    private readonly List<Action> _refreshContrast = [];

    public ThemeEditorWindow() : this(SongTheme.Day.Clone("Preview"), isNew: true, new HashSet<string>()) { }

    /// <param name="theme">The theme to edit; a copy is edited, so cancelling leaves it unchanged.</param>
    /// <param name="isNew">Whether this is a new theme (which can't be deleted yet).</param>
    /// <param name="takenNames">Names already used by other themes.</param>
    public ThemeEditorWindow(SongTheme theme, bool isNew, IReadOnlySet<string> takenNames)
    {
        InitializeComponent();
        _theme = theme.Clone();
        _originalName = isNew ? null : theme.Name;
        _takenNames = takenNames;

        Title = isNew ? "New theme" : $"Edit theme: {theme.Name}";
        NameBox.Text = _theme.Name;
        LightRadio.IsChecked = !_theme.Dark;
        DarkRadio.IsChecked = _theme.Dark;
        DeleteButton.IsVisible = !isNew;

        foreach (var role in Roles)
            ColorRows.Children.Add(ColorRow(role));

        // Comments put lyrics-coloured text on the highlight colour, so check that pair too.
        ColorRows.Children.Add(ContrastNote("Lyrics on comment highlight", t => t.Text, t => t.CommentBackground));

        UpdatePreview();
    }

    /// <summary>The saved theme, if the user pressed Save.</summary>
    public SongTheme? SavedTheme { get; private set; }

    /// <summary>True if the user deleted the theme.</summary>
    public bool Deleted { get; private set; }

    private Control ColorRow(ColorRole role)
    {
        var picker = new ColorPicker
        {
            Color = Color.Parse(role.Get(_theme)),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        picker.ColorChanged += (_, e) =>
        {
            role.Set(_theme, $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}");
            UpdatePreview();
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,Auto,*"), Margin = new Thickness(0, 2) };
        grid.Children.Add(new TextBlock { Text = role.Label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(picker, 1);
        grid.Children.Add(picker);
        if (role.Against is not null)
        {
            var badge = ContrastBadge(role.Get, role.Against);
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }
        return grid;
    }

    private Control ContrastNote(string label, Func<SongTheme, string> foreground, Func<SongTheme, string> background)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(0, 6, 0, 0) };
        grid.Children.Add(new TextBlock { Text = label, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var badge = ContrastBadge(foreground, background);
        badge.Margin = default;
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        return grid;
    }

    // Shows the WCAG contrast ratio: 7:1 is high contrast (AAA), 4.5:1 the minimum for text (AA).
    private TextBlock ContrastBadge(Func<SongTheme, string> foreground, Func<SongTheme, string> background)
    {
        var badge = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 13 };
        ToolTip.SetTip(badge, "Contrast against the background. 7:1 or more is high contrast (WCAG AAA); below 4.5:1 is hard to read.");
        _refreshContrast.Add(() =>
        {
            var ratio = SongTheme.ContrastRatio(foreground(_theme), background(_theme));
            (badge.Text, badge.Foreground) = ratio switch
            {
                >= 7 => ($"{ratio:0.0}:1  High contrast", new SolidColorBrush(Color.Parse("#2E9E4F"))),
                >= 4.5 => ($"{ratio:0.0}:1  Readable", new SolidColorBrush(Color.Parse("#C98A00"))),
                _ => ($"{ratio:0.0}:1  Too low", new SolidColorBrush(Color.Parse("#D12E2E"))),
            };
        });
        return badge;
    }

    private void UpdatePreview()
    {
        var palette = SongPalette.FromTheme(_theme);
        PreviewBorder.Background = palette.Background;
        PreviewHost.Content = new SongRenderer(new RenderOptions { Palette = palette, ShowDiagrams = true, FontSize = 16 }).Render(_previewSong);
        foreach (var refresh in _refreshContrast)
            refresh();
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e) => ErrorText.IsVisible = false;

    private void OnStyleChanged(object? sender, RoutedEventArgs e) => _theme.Dark = DarkRadio.IsChecked == true;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? "";
        var error = name.Length == 0 ? "Give the theme a name."
            : name == SongTheme.SystemThemeName || SongTheme.BuiltIn.Any(t => t.Name == name) ? $"“{name}” is a built-in theme; choose another name."
            : name != _originalName && _takenNames.Contains(name) ? $"There is already a theme called “{name}”."
            : null;
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        _theme.Name = name;
        SavedTheme = _theme;
        Close();
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        Deleted = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
