using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ChordPro.Viewer;

/// <summary>The "About ChordPro Viewer" window: icon, name, version and project link.</summary>
public sealed class AboutWindow : Window
{
    public const string ProjectUrl = "https://github.com/drwhalen/ChordPro";

    public AboutWindow()
    {
        Title = "About ChordPro Viewer";
        Width = 340;
        Height = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var version = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        version = version.Split('+')[0]; // drop the source-control suffix

        var link = new Button { Content = "github.com/drwhalen/ChordPro", HorizontalAlignment = HorizontalAlignment.Center };
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });

        using var icon = AssetLoader.Open(new Uri("avares://ChordPro.Viewer/Assets/AppIcon.png"));
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Image { Source = new Bitmap(icon), Width = 128, Height = 128 },
                new TextBlock { Text = "ChordPro Viewer", FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = $"Version {version}", Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Read, play and perform ChordPro songs.", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) },
                link,
            },
        };
    }
}
