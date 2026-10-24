using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.UI;

namespace ChordPro.Mobile;

/// <summary>Writes the player's notes for a song, shown above it on stage (and on the Mac), never in the song file.</summary>
internal sealed class NotesView : UserControl
{
    public NotesView(string title, string? notes, SongPalette palette, Action<string?> done)
    {
        Background = palette.Background;
        var box = new TextBox
        {
            Text = notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 18,
            Watermark = "Drop the 2nd verse · Long intro · Watch the key change",
            Margin = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var doneButton = new Button { Content = "Done" };
        doneButton.Click += (_, _) => done(string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim());
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(new TextBlock
        {
            Text = $"Notes · {title}",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0),
            Foreground = palette.Text,
        });
        Grid.SetColumn(doneButton, 1);
        top.Children.Add(doneButton);
        var bar = new Border { Padding = new Thickness(8, 6), Background = palette.CommentBackground, Child = top };
        var hint = new TextBlock
        {
            Text = "Shown above the song here and on the Mac. The song file isn't changed.",
            FontSize = 13,
            Foreground = palette.MutedText,
            Margin = new Thickness(12, 0, 12, 12),
            TextWrapping = TextWrapping.Wrap,
        };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(hint);
        root.Children.Add(box);
        Content = root;
        AttachedToVisualTree += (_, _) => box.Focus();
    }
}
