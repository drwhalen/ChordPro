using Avalonia.iOS;
using Foundation;
using UIKit;

namespace ChordPro.Mobile;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>;

/// <summary>
/// Apps built with the iOS 27 SDK must use the scene life cycle, which Avalonia 11.3 predates: its app
/// delegate makes a window with no scene. This puts that window in the app's scene (named in Info.plist).
/// </summary>
[Register("SceneDelegate")]
public sealed class SceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window { get; set; }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene || UIApplication.SharedApplication.Delegate.GetWindow() is not { } window)
            return;
        window.WindowScene = windowScene;
        Window = window;
        window.MakeKeyAndVisible();
    }
}
