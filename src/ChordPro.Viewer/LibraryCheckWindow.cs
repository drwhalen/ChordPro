using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;

namespace ChordPro.Viewer;

/// <summary>
/// Lists the problems <see cref="LibraryCheck"/> found, one file per row; choosing a row opens that
/// song or setlist so it can be fixed. A problem that is deliberate can be marked "Looks fine",
/// which hides it until its file changes.
/// </summary>
public sealed class LibraryCheckWindow : Window
{
    private sealed record FileProblems(string Path, string Name, IReadOnlyList<LibraryIssue> Issues);

    private readonly IReadOnlyList<LibraryIssue> _issues;
    private readonly Func<LibraryIssue, bool> _isAccepted;
    private readonly Action<LibraryIssue> _accept;
    private readonly SongPalette _palette;
    private readonly TextBlock _summary;
    private readonly ListBox _list;
    private readonly CheckBox _showAccepted = new() { Content = "Show accepted", IsVisible = false };

    public LibraryCheckWindow() : this([], SongPalette.FromTheme(Core.Themes.SongTheme.Day), _ => { }, _ => false, _ => { }) { }

    /// <param name="isAccepted">Whether a problem was marked "Looks fine" (and its file hasn't changed since).</param>
    /// <param name="accept">Marks a problem "Looks fine".</param>
    public LibraryCheckWindow(IReadOnlyList<LibraryIssue> issues, SongPalette palette, Action<string> open,
        Func<LibraryIssue, bool> isAccepted, Action<LibraryIssue> accept)
    {
        _issues = issues;
        _isAccepted = isAccepted;
        _accept = accept;
        _palette = palette;

        Title = "Library check";
        Width = 600;
        Height = 620;
        Background = palette.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _summary = new TextBlock
        {
            Foreground = palette.Text,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_showAccepted, Dock.Right);
        top.Children.Add(_showAccepted);
        top.Children.Add(_summary);
        DockPanel.SetDock(top, Dock.Top);
        _showAccepted.IsCheckedChanged += (_, _) => Refresh();

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            // The list asks for an empty row while it swaps in a new set of files.
            ItemTemplate = new FuncDataTemplate<FileProblems?>((file, _) => file is null ? new Panel() : Row(file)),
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is FileProblems file)
                open(file.Path);
        };

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true,
            Children = { top, _list },
        };
        Refresh();
    }

    private void Refresh()
    {
        var showAccepted = _showAccepted.IsChecked == true;
        var accepted = _issues.Count(_isAccepted);
        var shown = _issues.Where(i => showAccepted || !_isAccepted(i)).ToList();
        var files = shown
            .GroupBy(i => i.Path)
            .Select(g => new FileProblems(g.Key, g.First().Name, g.ToList()))
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var open = _issues.Count - accepted;
        _summary.Text = open == 0
            ? accepted == 0 ? "No problems found." : "No problems left to look at."
            : $"{open} {(open == 1 ? "problem" : "problems")} in {files.Count(f => f.Issues.Any(i => !_isAccepted(i)))} {(files.Count == 1 ? "file" : "files")}. Choose one to open it.";
        if (accepted > 0)
            _summary.Text += $" ({accepted} marked “Looks fine”{(showAccepted ? "" : ", hidden")}.)";
        _showAccepted.IsVisible = accepted > 0;
        _list.ItemsSource = files;
    }

    private Control Row(FileProblems file)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2), Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = file.Name, FontWeight = FontWeight.SemiBold, Foreground = _palette.Text, TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var issue in file.Issues)
        {
            var accepted = _isAccepted(issue);
            var text = new TextBlock
            {
                Text = "• " + issue.Problem,
                FontSize = 12,
                Foreground = _palette.MutedText,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = accepted ? 0.6 : 1,
                TextDecorations = accepted ? TextDecorations.Strikethrough : null,
            };
            var row = new DockPanel();
            if (!accepted)
            {
                var looksFine = new Button { Content = "Looks fine", FontSize = 11, Padding = new Thickness(6, 1), Margin = new Thickness(8, 0, 0, 0) };
                ToolTip.SetTip(looksFine, "This is deliberate: hide it until the file changes");
                looksFine.Click += (_, e) =>
                {
                    e.Handled = true; // don't also open the song
                    _accept(issue);
                    Refresh();
                };
                DockPanel.SetDock(looksFine, Dock.Right);
                row.Children.Add(looksFine);
            }
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        return panel;
    }
}
