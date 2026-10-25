using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChordPro.Mobile;

/// <summary>
/// The pieces the stage's menus are built from, laid out like iOS Settings: a title, then grouped sections of
/// rows (a label on the left, its control on the right), each with an optional heading and a note below.
/// Colors come from the theme's resources, so they follow a theme change while a menu is built only once.
/// </summary>
internal static class MenuParts
{
    public const double Width = 360;
    private const double RowHeight = 48;

    private static T Themed<T>(T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    private static TextBlock Muted(string text, double size = 13) =>
        Themed(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "SystemControlForegroundBaseMediumBrush");

    /// <summary>A whole menu: its title and sections, scrolling when taller than the screen.</summary>
    public static ScrollViewer Menu(string title, params Control[] sections)
    {
        var stack = new StackPanel { Spacing = 18, Width = Width };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(4, 0, 0, -6) });
        stack.Children.AddRange(sections);
        return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>A group of rows on a rounded card, with an optional heading above and note below.</summary>
    public static StackPanel Section(string? heading, string? note, params Control[] rows) =>
        Section(heading, note is null ? null : Muted(note), rows);

    /// <param name="note">A note that changes (such as how the song is being paced).</param>
    public static StackPanel Section(string? heading, TextBlock? note, params Control[] rows)
    {
        var card = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            // A hairline between rows, inset from the left as on iOS.
            if (i > 0)
                card.Children.Add(Themed(new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0), Opacity = 0.6 },
                    Border.BackgroundProperty, "SystemControlForegroundBaseLowBrush"));
            card.Children.Add(rows[i]);
        }
        var section = new StackPanel { Spacing = 6 };
        if (heading is not null)
            section.Children.Add(Muted(heading.ToUpperInvariant(), 12).With(t => t.Margin = new Thickness(14, 0)));
        section.Children.Add(Themed(new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = card },
            Border.BackgroundProperty, "SystemControlBackgroundBaseLowBrush"));
        if (note is not null)
        {
            note.FontSize = 13;
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(14, 0);
            section.Children.Add(Themed(note, TextBlock.ForegroundProperty, "SystemControlForegroundBaseMediumBrush"));
        }
        return section;
    }

    /// <summary>A row: the label on the left and its control on the right.</summary>
    public static Grid Row(string label, Control? control = null) => Row(new TextBlock { Text = label }, control);

    /// <param name="label">A label that changes (such as Speed or Tempo).</param>
    public static Grid Row(TextBlock label, Control? control = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = RowHeight, Margin = new Thickness(14, 0, 8, 0) };
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(label);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
        }
        return row;
    }

    /// <summary>An on/off row.</summary>
    public static Grid SwitchRow(string label, ToggleSwitch toggle)
    {
        toggle.OnContent = toggle.OffContent = null;
        toggle.MinWidth = 0;
        toggle.Margin = new Thickness(0, 0, -6, 0);
        return Row(label, toggle);
    }

    /// <summary>A row with a value and − / + buttons beside it.</summary>
    public static Grid StepperRow(string label, TextBlock value, Action down, Action up) =>
        StepperRow(new TextBlock { Text = label }, value, down, up);

    /// <param name="label">A label that changes (such as Speed or Tempo).</param>
    public static Grid StepperRow(TextBlock label, TextBlock value, Action down, Action up)
    {
        value.VerticalAlignment = VerticalAlignment.Center;
        value.MinWidth = 64;
        value.TextAlignment = TextAlignment.Right;
        value.Margin = new Thickness(0, 0, 10, 0);
        var stepper = new StackPanel { Orientation = Orientation.Horizontal, Children = { value, StepButton("−", down), StepButton("+", up) } };
        return Row(label, stepper);
    }

    private static Button StepButton(string text, Action action)
    {
        var button = new Button { Content = text, Width = 48, MinHeight = 36, Height = 36, Padding = new Thickness(0), FontSize = 18, Margin = new Thickness(2, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A row with a choice from a list, shown as its value with a ⌄ on the right.</summary>
    public static Grid ChoiceRow(string label, ComboBox choices)
    {
        choices.MinWidth = 0;
        choices.MaxWidth = 210;
        choices.BorderThickness = new Thickness(0);
        choices.Background = Brushes.Transparent;
        choices.HorizontalContentAlignment = HorizontalAlignment.Right;
        return Row(label, choices);
    }

    /// <summary>A row that does something when tapped, such as opening the tuner.</summary>
    public static Button ActionRow(string label, Action action, string trailing = "›")
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var chevron = Muted(trailing, 20).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        return Tappable(content, action);
    }

    /// <summary>A whole-row button with no button look of its own (the card is its background).</summary>
    public static Button Tappable(Control content, Action action)
    {
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinHeight = RowHeight,
            Padding = new Thickness(14, 8),
        };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Choices side by side, one of them on, as iOS's segmented control.</summary>
    public static Border Segmented(IEnumerable<ToggleButton> buttons)
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1 };
        foreach (var button in buttons)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.MinHeight = 36;
            button.CornerRadius = new CornerRadius(8);
            button.Margin = new Thickness(2);
            button.BorderThickness = new Thickness(0);
            button.Background = Brushes.Transparent; // the checked one is filled by the theme
            grid.Children.Add(button);
        }
        return new Border { Padding = new Thickness(4), Child = grid };
    }

    private static T With<T>(this T control, Action<T> change)
    {
        change(control);
        return control;
    }
}
