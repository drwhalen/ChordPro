using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
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
        // The stage's menus (MenuParts): roomier and rounder than Fluent's flyouts.
        Styles.Add(new Style(x => x.OfType<FlyoutPresenter>().Class("menu"))
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 16)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(16)),
                new Setter(Layoutable.MaxWidthProperty, 480.0),
            },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ErrorLog.InstallForUIThread();
        // On stage the screen must never dim or lock between songs.
        UIKit.UIApplication.SharedApplication.IdleTimerDisabled = true;
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = new MainView();
        base.OnFrameworkInitializationCompleted();
    }
}
