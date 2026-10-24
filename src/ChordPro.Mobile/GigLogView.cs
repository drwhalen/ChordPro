using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>A finished gig in the log, with its file.</summary>
internal sealed record GigLogEntry(string Path, Gig Gig)
{
    /// <summary>"Oct 24 · 14 songs · 1 h 32 min".</summary>
    public string Summary
    {
        get
        {
            var played = Gig.Played.Count();
            var length = (Gig.Ended ?? Gig.Started) - Gig.Started;
            return $"{Gig.Started.ToLocalTime():MMM d, yyyy} · {played} {(played == 1 ? "song" : "songs")} · {SetLength.Minutes(length)}";
        }
    }
}

/// <summary>What was played at a gig, and when.</summary>
internal sealed class GigLogView : UserControl
{
    public GigLogView(GigLogEntry log, Func<string, SongInfo?> song, SongPalette palette, Action back)
    {
        Background = palette.Background;
        var gig = log.Gig;
        var backButton = new Button { Content = "‹ Setlists" };
        backButton.Click += (_, _) => back();
        var title = new StackPanel
        {
            Margin = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = gig.Name, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = palette.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = log.Summary, FontSize = 13, Foreground = palette.MutedText },
            },
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        top.Children.Add(backButton);
        Grid.SetColumn(title, 1);
        top.Children.Add(title);
        var header = new Border { Padding = new Thickness(12, 8), Background = palette.CommentBackground, Child = top };

        string Name(GigEntry entry) => song(entry.Song)?.Title ?? Path.GetFileNameWithoutExtension(entry.Song);
        var lines = new StackPanel { Margin = new Thickness(16, 8), Spacing = 6 };
        foreach (var entry in gig.Played)
        {
            var minutes = entry.Seconds >= 60 ? $" · {SetLength.Minutes(TimeSpan.FromSeconds(entry.Seconds))}" : "";
            var request = entry.Requested ? " · request" : "";
            lines.Children.Add(new TextBlock
            {
                Text = $"{entry.Started?.ToLocalTime():h:mm tt}   {Name(entry)}{minutes}{request}",
                FontSize = 17,
                Foreground = palette.Text,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        var notPlayed = gig.Entries.Where(e => !e.IsDivider && e.State != GigSongState.Played).ToList();
        if (notPlayed.Count > 0)
        {
            lines.Children.Add(new TextBlock { Text = "Not played", FontWeight = FontWeight.SemiBold, FontSize = 15, Foreground = palette.MutedText, Margin = new Thickness(0, 12, 0, 0) });
            foreach (var entry in notPlayed)
                lines.Children.Add(new TextBlock { Text = Name(entry) + (entry.State == GigSongState.Skipped ? " · skipped" : ""), FontSize = 17, Foreground = palette.MutedText });
        }

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = lines });
        Content = root;
    }
}
