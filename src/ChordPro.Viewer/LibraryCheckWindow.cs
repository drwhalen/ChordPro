using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ChordPro.Core;

namespace ChordPro.Viewer;

/// <summary>
/// Lists the problems <see cref="LibraryCheck"/> found, one file per row; choosing a row opens that
/// song or setlist so it can be fixed.
/// </summary>
public sealed class LibraryCheckWindow : Window
{
    private sealed record FileProblems(string Path, string Name, IReadOnlyList<string> Problems);

    public LibraryCheckWindow() : this([], SongPalette.FromTheme(Core.Themes.SongTheme.Day), _ => { }) { }

    public LibraryCheckWindow(IReadOnlyList<LibraryIssue> issues, SongPalette palette, Action<string> open)
    {
        Title = "Library check";
        Width = 560;
        Height = 620;
        Background = palette.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var files = issues
            .GroupBy(i => i.Path)
            .Select(g => new FileProblems(g.Key, g.First().Name, g.Select(i => i.Problem).ToList()))
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var summary = new TextBlock
        {
            Text = issues.Count == 0
                ? "No problems found."
                : $"{issues.Count} {(issues.Count == 1 ? "problem" : "problems")} in {files.Count} {(files.Count == 1 ? "file" : "files")}. Choose one to open it.",
            Foreground = palette.Text,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(summary, Dock.Top);

        var list = new ListBox
        {
            ItemsSource = files,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<FileProblems>((file, _) =>
            {
                var panel = new StackPanel { Margin = new Thickness(0, 2) };
                panel.Children.Add(new TextBlock { Text = file.Name, FontWeight = FontWeight.SemiBold, Foreground = palette.Text, TextTrimming = TextTrimming.CharacterEllipsis });
                foreach (var problem in file.Problems)
                    panel.Children.Add(new TextBlock { Text = "• " + problem, FontSize = 12, Foreground = palette.MutedText, TextWrapping = TextWrapping.Wrap });
                return panel;
            }),
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is FileProblems file)
                open(file.Path);
        };

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true,
            Children = { summary, list },
        };
    }
}
