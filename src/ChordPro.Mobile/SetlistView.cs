using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>A set divider in a setlist, with how long the set runs.</summary>
internal sealed record SetHeading(string Name, SetLength Length);

/// <summary>A song in a setlist: where it is in the list, and the song (null when it isn't in the library).</summary>
internal sealed record SetlistSong(int Index, string Path, SongInfo? Song);

/// <summary>One setlist's songs, in order with their sets; tapping a song starts playing from there.</summary>
internal sealed class SetlistView : UserControl
{
    public SetlistView(SetlistSummary setlist, IReadOnlyDictionary<string, SongInfo> songs, SongPalette palette,
        Action back, Action<int> play)
    {
        Background = palette.Background;
        var backButton = new Button { Content = "‹ Setlists" };
        backButton.Click += (_, _) => back();
        var start = new Button { Content = "Start ›" };
        start.Click += (_, _) =>
        {
            if (setlist.Setlist.Step(-1, +1) is var (first, _))
                play(first);
        };
        var title = new StackPanel
        {
            Margin = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = setlist.Name, FontSize = 20, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = setlist.Length.ToString(), FontSize = 13, Foreground = palette.MutedText },
            },
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        top.Children.Add(backButton);
        Grid.SetColumn(title, 1);
        top.Children.Add(title);
        Grid.SetColumn(start, 2);
        top.Children.Add(start);
        var header = new Border { Padding = new Thickness(12, 8), Background = palette.CommentBackground, Child = top };

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            ItemsSource = Entries(setlist.Setlist, songs),
            ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
            {
                SetHeading heading => new TextBlock
                {
                    Text = heading.Length.Songs > 0 ? $"{heading.Name} · {heading.Length}" : heading.Name,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 15,
                    Foreground = palette.MutedText,
                    Margin = new Thickness(0, 10, 0, 2),
                },
                SetlistSong { Song: { } song } => new StackPanel
                {
                    Margin = new Thickness(0, 4),
                    Background = Brushes.Transparent, // the whole row takes the tap
                    Children =
                    {
                        new TextBlock { Text = song.Title, FontSize = 18, Foreground = palette.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock
                        {
                            Text = string.Join(" · ", new[] { song.Artist, song.Key is null ? null : $"Key {song.Key}", song.Length is { } length ? SetLength.Minutes(length) : null }
                                .Where(s => !string.IsNullOrEmpty(s))),
                            FontSize = 14,
                            Foreground = palette.MutedText,
                        },
                    },
                },
                SetlistSong missing => new TextBlock
                {
                    Text = $"{System.IO.Path.GetFileNameWithoutExtension(missing.Path)} (not in the library)",
                    FontSize = 18,
                    Foreground = palette.MutedText,
                    Margin = new Thickness(0, 4),
                },
                _ => new Control(),
            }),
        };
        list.Tapped += (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is SetlistSong { Song: not null } entry)
                play(entry.Index);
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(list);
        Content = root;
    }

    private static List<object> Entries(Setlist setlist, IReadOnlyDictionary<string, SongInfo> songs)
    {
        List<object> entries = [];
        for (var i = 0; i < setlist.Songs.Count; i++)
        {
            var entry = setlist.Songs[i];
            if (Setlist.IsDivider(entry))
            {
                // The set runs to the next divider.
                var set = setlist.Songs.Skip(i + 1).TakeWhile(e => !Setlist.IsDivider(e));
                entries.Add(new SetHeading(Setlist.DividerName(entry), SetLength.Of(set.Select(p => songs.GetValueOrDefault(p)?.Length))));
            }
            else
            {
                entries.Add(new SetlistSong(i, entry, songs.GetValueOrDefault(entry)));
            }
        }
        return entries;
    }
}
