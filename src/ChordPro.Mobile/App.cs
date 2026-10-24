using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace ChordPro.Mobile;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        // On stage the screen must never dim or lock between songs.
        UIKit.UIApplication.SharedApplication.IdleTimerDisabled = true;
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = new StageView();
        base.OnFrameworkInitializationCompleted();
    }
}
