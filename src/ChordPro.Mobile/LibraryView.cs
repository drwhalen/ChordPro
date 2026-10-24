using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>A heading in the song list when it's grouped ("Pink Floyd · 6").</summary>
internal sealed record GroupHeading(string Name, int Count);

/// <summary>The song list: search, grouping, and the library folder with how fresh its copy is.</summary>
internal sealed class LibraryView : UserControl
{
    private static readonly LibraryGrouping[] Groupings = [LibraryGrouping.None, LibraryGrouping.Artist, LibraryGrouping.Book, LibraryGrouping.Recent, LibraryGrouping.Decade];
    private static readonly string[] GroupingNames = ["A–Z", "Artist", "Book", "Recently added", "Decade"];

    private readonly MobileSettings _settings;
    private readonly TextBlock _status = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _search = new() { Watermark = "Search songs and artists", Margin = new Thickness(12, 4, 12, 8) };
    private readonly ComboBox _grouping = new() { ItemsSource = GroupingNames, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly Border _header = new() { Padding = new Thickness(12, 8) };
    private List<SongInfo> _songs = [];
    private SongPalette _palette = SongPalette.Print;

    public LibraryView(MobileSettings settings, Func<Task> chooseLibrary, Action<SongInfo> open)
    {
        _settings = settings;
        var title = new TextBlock { Text = "Songs", FontSize = 28, FontWeight = FontWeight.Bold };
        var choose = new Button { Content = "Library…", VerticalAlignment = VerticalAlignment.Center };
        choose.Click += async (_, _) => await chooseLibrary();
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        top.Children.Add(new StackPanel { Children = { title, _status } });
        Grid.SetColumn(_grouping, 1);
        top.Children.Add(_grouping);
        Grid.SetColumn(choose, 2);
        top.Children.Add(choose);
        _header.Child = top;

        _grouping.SelectedIndex = Math.Max(0, Array.IndexOf(Groupings, Enum.TryParse<LibraryGrouping>(settings.Grouping, out var g) ? g : LibraryGrouping.None));
        _grouping.SelectionChanged += (_, _) =>
        {
            _settings.Grouping = Groupings[Math.Max(0, _grouping.SelectedIndex)].ToString();
            _settings.Save();
            ShowSongs();
        };
        _search.TextChanged += (_, _) => ShowSongs();

        _list.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            GroupHeading heading => new TextBlock
            {
                Text = $"{heading.Name} · {heading.Count}",
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                Foreground = _palette.MutedText,
                Margin = new Thickness(0, 10, 0, 2),
            },
            SongInfo song => SongRow(song),
            _ => new Control(),
        });
        // A tap, not a selection: scrolling the list mustn't open songs.
        _list.Tapped += (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is SongInfo song)
                open(song);
        };

        var root = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_header);
        root.Children.Add(_search);
        root.Children.Add(_list);
        Content = root;
    }

    /// <summary>The songs to list (a fresh scan of the copy).</summary>
    public void SetSongs(List<SongInfo> songs)
    {
        _songs = songs;
        ShowSongs();
    }

    /// <summary>The line under "Songs": the library and how up to date the copy is.</summary>
    public void SetStatus(string text) => _status.Text = text;

    public void ApplyPalette(SongPalette palette)
    {
        _palette = palette;
        Background = palette.Background;
        _header.Background = palette.CommentBackground;
        _status.Foreground = palette.MutedText;
        ShowSongs();
    }

    private Control SongRow(SongInfo song)
    {
        var details = string.Join(" · ", new[] { song.Artist, song.Key is null ? null : $"Key {song.Key}", song.Capo > 0 ? $"Capo {song.Capo}" : null }
            .Where(s => !string.IsNullOrEmpty(s)));
        return new StackPanel
        {
            Margin = new Thickness(0, 4),
            Background = Brushes.Transparent, // the whole row takes the tap
            Children =
            {
                new TextBlock { Text = song.Title, FontSize = 18, Foreground = _palette.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = details, FontSize = 14, Foreground = _palette.MutedText, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
    }

    private void ShowSongs()
    {
        var songs = SongLibrary.Filter(_songs, _search.Text);
        var grouping = Groupings[Math.Max(0, _grouping.SelectedIndex)];
        List<object> items = [];
        if (grouping == LibraryGrouping.None)
            items.AddRange(songs);
        else
        {
            foreach (var group in SongLibrary.Group(songs, grouping))
            {
                items.Add(new GroupHeading(group.Name, group.Songs.Count));
                items.AddRange(group.Songs);
            }
        }
        _list.ItemsSource = items;
    }
}
