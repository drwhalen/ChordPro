using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChordPro.Viewer;

/// <summary>
/// A question with a few answers, e.g. "Save changes to Hey Jude?" — Save / Don't Save / Cancel.
/// <see cref="AskAsync"/> returns the answer chosen, or the last one (cancel) if the window is closed.
/// </summary>
public sealed class ChoiceDialog : Window
{
    private string _answer;

    public ChoiceDialog() : this("", "", ["OK"]) { }

    /// <param name="answers">Button labels, the default first and the cancel answer last.</param>
    public ChoiceDialog(string question, string detail, IReadOnlyList<string> answers)
    {
        _answer = answers[^1];
        Title = "ChordPro Viewer";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var answer in answers)
        {
            var button = new Button { Content = answer, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (answer == answers[0])
                button.Classes.Add("accent");
            button.Click += (_, _) =>
            {
                _answer = answer;
                Close();
            };
            buttons.Children.Add(button);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 10,
            MaxWidth = 420,
            Children =
            {
                new TextBlock { Text = question, FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, IsVisible = detail.Length > 0 },
                new Border { Height = 6 },
                buttons,
            },
        };

        // Return answers the first choice, Escape the last.
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                _answer = e.Key == Key.Enter ? answers[0] : answers[^1];
                Close();
                e.Handled = true;
            }
        };
    }

    public static async Task<string> AskAsync(Window owner, string question, string detail, params string[] answers)
    {
        var dialog = new ChoiceDialog(question, detail, answers);
        await dialog.ShowDialog(owner);
        return dialog._answer;
    }
}
