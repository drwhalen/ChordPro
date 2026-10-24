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

/// <summary>The gig being played, at the top of the setlists.</summary>
internal sealed record CurrentGig(Gig Gig);

/// <summary>A setlist in the library, with how long it runs.</summary>
internal sealed record SetlistSummary(string Path, string Name, Setlist Setlist, SetLength Length);

/// <summary>The song list and the setlists: search, grouping, and the library folder with how fresh its copy is.</summary>
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
    private readonly Button _songsTab = Tab("Songs");
    private readonly Button _setlistsTab = Tab("Setlists");
    private bool _showingSetlists;
    private List<SongInfo> _songs = [];
    private List<SetlistSummary> _setlists = [];
    private Gig? _currentGig;
    private List<GigLogEntry> _gigLog = [];
    private SongPalette _palette = SongPalette.Print;

    public LibraryView(MobileSettings settings, Func<Task> chooseLibrary, Action<SongInfo> open, Action<SetlistSummary> openSetlist,
        Action openGig, Action<GigLogEntry> openLog)
    {
        _settings = settings;
        _songsTab.Click += (_, _) => ShowTab(setlists: false);
        _setlistsTab.Click += (_, _) => ShowTab(setlists: true);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _songsTab, _setlistsTab } };
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
            ShowItems();
        };
        _search.TextChanged += (_, _) => ShowItems();

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
            SetlistSummary setlist => Row(setlist.Name, setlist.Length.ToString()),
            CurrentGig current => Row($"▶\uFE0E {current.Gig.Name}", $"Playing now · started {current.Gig.Started.ToLocalTime():h:mm tt}"),
            GigLogEntry log => Row(log.Gig.Name, log.Summary),
            _ => new Control(),
        });
        // A tap, not a selection: scrolling the list mustn't open songs.
        _list.Tapped += (_, e) =>
        {
            switch ((e.Source as StyledElement)?.DataContext)
            {
                case SongInfo song: open(song); break;
                case SetlistSummary setlist: openSetlist(setlist); break;
                case CurrentGig: openGig(); break;
                case GigLogEntry log: openLog(log); break;
            }
        };

        var root = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_header);
        root.Children.Add(_search);
        root.Children.Add(_list);
        Content = root;
    }

    /// <summary>The songs and setlists to list (a fresh scan of the copy).</summary>
    public void SetSongs(List<SongInfo> songs, List<SetlistSummary> setlists)
    {
        _songs = songs;
        _setlists = setlists;
        ShowItems();
    }

    /// <summary>The gig being played (or null) and finished gigs, newest first, shown with the setlists.</summary>
    public void SetGigs(Gig? current, List<GigLogEntry> log)
    {
        _currentGig = current;
        _gigLog = log;
        ShowItems();
    }

    /// <summary>The line under "Songs": the library and how up to date the copy is.</summary>
    public void SetStatus(string text) => _status.Text = text;

    public void ApplyPalette(SongPalette palette)
    {
        _palette = palette;
        Background = palette.Background;
        _header.Background = palette.CommentBackground;
        _status.Foreground = palette.MutedText;
        ShowItems();
    }

    // "Songs" and "Setlists" at the top, as large as a title.
    private static Button Tab(string text) => new()
    {
        Content = text,
        FontSize = 24,
        FontWeight = FontWeight.Bold,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 0),
    };

    private void ShowTab(bool setlists)
    {
        _showingSetlists = setlists;
        _grouping.IsVisible = !setlists;
        _search.Watermark = setlists ? "Search setlists" : "Search songs and artists";
        ShowItems();
    }

    private Control SongRow(SongInfo song) =>
        Row(song.Title, string.Join(" · ", new[] { song.Artist, song.Key is null ? null : $"Key {song.Key}", song.Capo > 0 ? $"Capo {song.Capo}" : null }
            .Where(s => !string.IsNullOrEmpty(s))));

    private Control Row(string title, string details)
    {
        return new StackPanel
        {
            Margin = new Thickness(0, 4),
            Background = Brushes.Transparent, // the whole row takes the tap
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, Foreground = _palette.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = details, FontSize = 14, Foreground = _palette.MutedText, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
    }

    private void ShowItems()
    {
        // The tab not shown is drawn dimmed.
        _songsTab.Foreground = _showingSetlists ? _palette.MutedText : _palette.Text;
        _setlistsTab.Foreground = _showingSetlists ? _palette.Text : _palette.MutedText;
        if (_showingSetlists)
        {
            bool Matches(string name) => string.IsNullOrWhiteSpace(_search.Text) ||
                name.Contains(_search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
            List<object> setlistItems = [];
            if (_currentGig is { } gig)
                setlistItems.Add(new CurrentGig(gig));
            setlistItems.AddRange(_setlists.Where(s => Matches(s.Name)));
            var log = _gigLog.Where(g => Matches(g.Gig.Name)).ToList();
            if (log.Count > 0)
            {
                setlistItems.Add(new GroupHeading("Gig log", log.Count));
                setlistItems.AddRange(log);
            }
            _list.ItemsSource = setlistItems;
            return;
        }
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
