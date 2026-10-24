using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChordPro.Core;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>What the gig screen asks of the app.</summary>
/// <param name="Play">Shows the entry at this index on stage.</param>
/// <param name="Changed">Saves the gig after a change made here.</param>
/// <param name="Request">Opens the song picker for a request.</param>
/// <param name="End">Ends the gig, into the gig log.</param>
/// <param name="Clock">The set clock, as shown on stage.</param>
internal sealed record GigActions(Action Back, Action<int> Play, Action Changed, Action Request, Action End, Func<string> Clock);

/// <summary>A set divider in a gig, with what the set runs to.</summary>
internal sealed record GigSet(string Name, SetLength Length);

/// <summary>A song in a gig: its place, how it stands, and the song (null when it isn't in the library).</summary>
internal sealed record GigSong(int Index, GigEntry Entry, SongInfo? Song, bool IsCurrent);

/// <summary>
/// A setlist being played: what's been played, skipped and requested, with the set clock. Songs can be skipped
/// and moved (Edit), and requests added, without changing the setlist file.
/// </summary>
internal sealed class GigView : UserControl
{
    private readonly Gig _gig;
    private readonly Func<string, SongInfo?> _song;
    private readonly SongPalette _palette;
    private readonly GigActions _actions;
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly TextBlock _clock = new() { FontSize = 13 };
    private readonly Button _edit = new() { Content = "Edit" };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _editing;

    public GigView(Gig gig, Func<string, SongInfo?> song, SongPalette palette, GigActions actions)
    {
        _gig = gig;
        _song = song;
        _palette = palette;
        _actions = actions;
        Background = palette.Background;
        _clock.Foreground = palette.MutedText;

        var back = new Button { Content = "‹ Setlists" };
        back.Click += (_, _) => actions.Back();
        var resume = new Button { Content = "Resume ›", IsVisible = gig.Current >= 0 };
        resume.Click += (_, _) => actions.Play(gig.Current);
        var title = new StackPanel
        {
            Margin = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = gig.Name, FontSize = 20, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = palette.Text },
                _clock,
            },
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        top.Children.Add(back);
        Grid.SetColumn(title, 1);
        top.Children.Add(title);
        Grid.SetColumn(resume, 2);
        top.Children.Add(resume);

        var request = new Button { Content = "Request…" };
        request.Click += (_, _) => actions.Request();
        _edit.Click += (_, _) =>
        {
            _editing = !_editing;
            _edit.Content = _editing ? "Done" : "Edit";
            ShowEntries();
        };
        var end = new Button { Content = "End gig" };
        end.Click += (_, _) => actions.End();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { request, _edit, end } };

        var header = new Border
        {
            Padding = new Thickness(12, 8),
            Background = palette.CommentBackground,
            Child = new StackPanel { Children = { top, tools } },
        };

        _list.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            GigSet set => new TextBlock
            {
                Text = set.Length.Songs > 0 ? $"{set.Name} · {set.Length}" : set.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                Foreground = palette.MutedText,
                Margin = new Thickness(0, 10, 0, 2),
            },
            GigSong row => SongRow(row),
            _ => new Control(),
        });
        _list.Tapped += (_, e) =>
        {
            if (!_editing && (e.Source as StyledElement)?.DataContext is GigSong { Song: not null } row)
                actions.Play(row.Index);
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_list);
        Content = root;

        _timer.Tick += (_, _) => _clock.Text = actions.Clock();
        ShowEntries();
    }

#if DEBUG
    /// <summary>For simulator checks: shows the Edit buttons.</summary>
    public void Edit()
    {
        _editing = true;
        _edit.Content = "Done";
        ShowEntries();
    }
#endif

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private Control SongRow(GigSong row)
    {
        var entry = row.Entry;
        var name = row.Song?.Title ?? $"{Path.GetFileNameWithoutExtension(entry.Song)} (not in the library)";
        var mark = row.IsCurrent ? "▶\uFE0E " : entry.State == GigSongState.Played ? "✓ " : "";
        var details = new List<string?>
        {
            entry.Requested ? "Request" : null,
            entry.State == GigSongState.Skipped ? "Skipped" : null,
            row.Song?.Artist,
            row.Song?.Key is { } key ? $"Key {key}" : null,
            row.Song?.Length is { } length ? SetLength.Minutes(length) : null,
        };
        var dim = entry.State != GigSongState.Upcoming && !row.IsCurrent || row.Song is null;
        var text = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = mark + name,
                    FontSize = 18,
                    FontWeight = row.IsCurrent ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = dim ? _palette.MutedText : _palette.Text,
                    TextDecorations = entry.State == GigSongState.Skipped ? TextDecorations.Strikethrough : null,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock
                {
                    Text = string.Join(" · ", details.Where(s => !string.IsNullOrEmpty(s))),
                    FontSize = 14,
                    Foreground = _palette.MutedText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4), Background = Brushes.Transparent };
        grid.Children.Add(text);
        if (_editing)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Change("↑", () => _gig.Move(row.Index, -1)));
            buttons.Children.Add(Change("↓", () => _gig.Move(row.Index, +1)));
            if (entry.State != GigSongState.Played && !row.IsCurrent)
                buttons.Children.Add(Change(entry.State == GigSongState.Skipped ? "Unskip" : "Skip", () => _gig.ToggleSkip(row.Index)));
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
        }
        return grid;
    }

    private Button Change(string text, Action change)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) =>
        {
            change();
            _actions.Changed();
            ShowEntries();
        };
        return button;
    }

    private void ShowEntries()
    {
        _clock.Text = _actions.Clock();
        List<object> rows = [];
        for (var i = 0; i < _gig.Entries.Count; i++)
        {
            var entry = _gig.Entries[i];
            if (entry.IsDivider)
            {
                // The set runs to the next divider; skipped songs don't count.
                var set = _gig.Entries.Skip(i + 1).TakeWhile(e => !e.IsDivider).Where(e => e.State != GigSongState.Skipped);
                rows.Add(new GigSet(Setlist.DividerName(entry.Song), SetLength.Of(set.Select(e => _song(e.Song)?.Length))));
            }
            else
            {
                rows.Add(new GigSong(i, entry, _song(entry.Song), i == _gig.Current));
            }
        }
        // Keep the place in the list while editing.
        var offset = _list.Scroll?.Offset;
        _list.ItemsSource = rows;
        if (offset is { } kept && _list.Scroll is { } scroll)
            Dispatcher.UIThread.Post(() => scroll.Offset = kept);
    }
}
