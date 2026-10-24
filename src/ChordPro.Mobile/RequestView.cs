using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>How a request joins the gig.</summary>
internal enum RequestPlace { Now, Next, AtEnd }

/// <summary>Finds a song for a request (or to play right away) and says where it goes in the gig.</summary>
internal sealed class RequestView : UserControl
{
    private readonly List<SongInfo> _songs;
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly TextBox _search = new() { Watermark = "Search songs and artists", Margin = new Thickness(12, 4, 12, 8) };

    public RequestView(List<SongInfo> songs, SongPalette palette, Action back, Action<SongInfo, RequestPlace> add)
    {
        _songs = songs;
        Background = palette.Background;
        var backButton = new Button { Content = "‹ Gig" };
        backButton.Click += (_, _) => back();
        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                backButton,
                new TextBlock { Text = "Request", FontSize = 20, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), Foreground = palette.Text },
            },
        };
        var header = new Border { Padding = new Thickness(12, 8), Background = palette.CommentBackground, Child = top };

        _list.ItemTemplate = new FuncDataTemplate<SongInfo>((song, _) => new StackPanel
        {
            Margin = new Thickness(0, 4),
            Background = Brushes.Transparent,
            Children =
            {
                new TextBlock { Text = song.Title, FontSize = 18, Foreground = palette.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = string.Join(" · ", new[] { song.Artist, song.Key is null ? null : $"Key {song.Key}" }.Where(s => !string.IsNullOrEmpty(s))),
                    FontSize = 14,
                    Foreground = palette.MutedText,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        });
        // A tapped song asks where it goes.
        _list.Tapped += (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is not SongInfo song || e.Source is not Control anchor)
                return;
            var choices = new StackPanel { Spacing = 6 };
            var flyout = new Flyout { Content = choices };
            foreach (var (label, place) in new[] { ("Play now", RequestPlace.Now), ("Play next", RequestPlace.Next), ("Add at the end", RequestPlace.AtEnd) })
            {
                var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 200 };
                button.Click += (_, _) =>
                {
                    flyout.Hide();
                    add(song, place);
                };
                choices.Children.Add(button);
            }
            flyout.ShowAt(anchor);
        };
        _search.TextChanged += (_, _) => _list.ItemsSource = SongLibrary.Filter(_songs, _search.Text).ToList();
        _list.ItemsSource = _songs;

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_search);
        root.Children.Add(_list);
        Content = root;
    }
}
