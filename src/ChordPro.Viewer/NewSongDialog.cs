using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;
using ChordPro.Core.Chords;

namespace ChordPro.Viewer;

/// <summary>
/// Asks what a new song (or a copy of one) is called and starts in: title, artist, key, and
/// optionally tempo and capo. <see cref="AskAsync"/> returns the details, or null if cancelled.
/// </summary>
public sealed class NewSongDialog : Window
{
    private readonly TextBox _title = new() { Watermark = "Song title" };
    private readonly TextBox _artist = new() { Watermark = "Artist (or your band)" };
    private readonly ComboBox _key = new() { MinWidth = 90 };
    private readonly TextBox _tempo = new() { Watermark = "BPM", Width = 70 };
    private readonly ComboBox _capo = new() { MinWidth = 70, ItemsSource = new[] { "None", "1", "2", "3", "4", "5", "6", "7" }, SelectedIndex = 0 };
    private readonly Button _create;
    private NewSongDetails? _result;

    public NewSongDialog() : this("New song", "Create", new NewSongDetails("", "", "G")) { }

    /// <param name="heading">"New song", or "Save as new song".</param>
    /// <param name="action">The button's label: "Create", "Save Copy".</param>
    /// <param name="initial">What the fields start with.</param>
    /// <param name="note">A line under the heading, e.g. what a change of key does to a copy.</param>
    public NewSongDialog(string heading, string action, NewSongDetails initial, string? note = null)
    {
        Title = heading;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _title.Text = initial.Title;
        _artist.Text = initial.Artist;
        var keys = KeyChords.AllKeys.Contains(initial.Key) ? KeyChords.AllKeys : [initial.Key, .. KeyChords.AllKeys];
        _key.ItemsSource = keys;
        _key.SelectedItem = initial.Key;
        _tempo.Text = initial.Tempo?.ToString() ?? "";
        _capo.SelectedIndex = Math.Clamp(initial.Capo, 0, 7);

        _create = new Button { Content = action, Classes = { "accent" }, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        _create.Click += (_, _) => Finish();
        var cancel = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();
        _title.TextChanged += (_, _) => _create.IsEnabled = (_title.Text ?? "").Trim().Length > 0;
        _create.IsEnabled = initial.Title.Trim().Length > 0;

        static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto") };
        void Row(int row, string label, Control control)
        {
            var text = Label(label);
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 2);
            grid.Children.Add(text);
            grid.Children.Add(control);
        }
        Row(0, "Title", _title);
        Row(2, "Artist", _artist);
        Row(4, "Key", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _key, Label("  Tempo"), _tempo, Label("  Capo"), _capo },
        });

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 12,
            Width = 460,
            Children =
            {
                new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 15 },
                new TextBlock { Text = note ?? "", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, IsVisible = note is not null },
                grid,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _create, cancel } },
            },
        };

        // Return creates, Escape cancels.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _create.IsEnabled)
                Finish();
            else if (e.Key == Key.Escape)
                Close();
        };
        Opened += (_, _) => _title.Focus();
    }

    private void Finish()
    {
        var tempo = int.TryParse((_tempo.Text ?? "").Trim(), out var bpm) && bpm is > 20 and < 400 ? bpm : (int?)null;
        _result = new NewSongDetails((_title.Text ?? "").Trim(), (_artist.Text ?? "").Trim(),
            _key.SelectedItem as string ?? "G", tempo, Math.Max(0, _capo.SelectedIndex));
        Close();
    }

    /// <summary>The details entered, or null if the dialog was cancelled.</summary>
    public static async Task<NewSongDetails?> AskAsync(Window owner, string heading, string action, NewSongDetails initial, string? note = null)
    {
        var dialog = new NewSongDialog(heading, action, initial, note);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    /// <summary>The details as entered so far (for testing).</summary>
    public NewSongDetails? Result => _result;
}
