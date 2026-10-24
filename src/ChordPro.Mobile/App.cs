using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace ChordPro.Mobile;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // Touch-sized buttons with their labels centered (Fluent puts them top left in a taller button).
        Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 44.0),
                new Setter(Layoutable.MinWidthProperty, 44.0),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // On stage the screen must never dim or lock between songs.
        UIKit.UIApplication.SharedApplication.IdleTimerDisabled = true;
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = new MainView();
        base.OnFrameworkInitializationCompleted();
    }
}
