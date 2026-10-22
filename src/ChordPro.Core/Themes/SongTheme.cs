using System.Globalization;
using System.Text.Json.Serialization;

namespace ChordPro.Core.Themes;

/// <summary>
/// Colors for displaying a song, as <c>#RRGGBB</c> strings so themes can be saved as JSON.
/// <see cref="Dark"/> picks the light or dark style for the rest of the window (toolbar, lists).
/// </summary>
public sealed class SongTheme
{
    public const string SystemThemeName = "System";

    public string Name { get; set; } = "Custom";
    public bool Dark { get; set; }
    public string Background { get; set; } = "#FFFFFF";
    public string Text { get; set; } = "#18181B";
    /// <summary>Section labels, subtitle and song details.</summary>
    public string MutedText { get; set; } = "#52525B";
    public string Chord { get; set; } = "#C2410C";
    public string ChorusBar { get; set; } = "#D4D4D8";
    public string CommentBackground { get; set; } = "#EDEDF0";
    public string CommentBox { get; set; } = "#A1A1AA";

    /// <summary>True for the themes that come with the app, which can't be edited or deleted.</summary>
    [JsonIgnore]
    public bool IsBuiltIn => BuiltIn.Any(t => t.Name == Name);

    public static SongTheme Day { get; } = new() { Name = "Day" };

    public static SongTheme Night { get; } = new()
    {
        Name = "Night", Dark = true,
        Background = "#121214", Text = "#E4E4E7", MutedText = "#A1A1AA", Chord = "#FB923C",
        ChorusBar = "#52525B", CommentBackground = "#2E2E33", CommentBox = "#71717A",
    };

    public static IReadOnlyList<SongTheme> BuiltIn { get; } =
    [
        Day,
        Night,
        new()
        {
            Name = "High contrast: black on white",
            Background = "#FFFFFF", Text = "#000000", MutedText = "#000000", Chord = "#0030B0",
            ChorusBar = "#000000", CommentBackground = "#FFF200", CommentBox = "#000000",
        },
        new()
        {
            Name = "High contrast: white on black", Dark = true,
            Background = "#000000", Text = "#FFFFFF", MutedText = "#FFFFFF", Chord = "#FFD400",
            ChorusBar = "#FFFFFF", CommentBackground = "#3A3A3A", CommentBox = "#FFFFFF",
        },
        new()
        {
            Name = "High contrast: yellow on black", Dark = true,
            Background = "#000000", Text = "#FFFF00", MutedText = "#FFFF00", Chord = "#00FFFF",
            ChorusBar = "#FFFF00", CommentBackground = "#333300", CommentBox = "#FFFF00",
        },
        new()
        {
            // All red, so a dark stage doesn't cost the player their night vision.
            Name = "Night vision (red)", Dark = true,
            Background = "#000000", Text = "#E0301E", MutedText = "#A82414", Chord = "#FF6A3D",
            ChorusBar = "#5C1308", CommentBackground = "#2A0905", CommentBox = "#A82414",
        },
    ];

    public SongTheme Clone(string? name = null)
    {
        var copy = (SongTheme)MemberwiseClone();
        if (name is not null)
            copy.Name = name;
        return copy;
    }

    /// <summary>
    /// WCAG contrast ratio between two <c>#RRGGBB</c> colors, from 1 (identical) to 21 (black on
    /// white). 4.5 is the usual minimum for text and 7 the high-contrast (AAA) level.
    /// </summary>
    public static double ContrastRatio(string foreground, string background)
    {
        var (a, b) = (RelativeLuminance(foreground), RelativeLuminance(background));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public static bool IsValidColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' &&
        int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    private static double RelativeLuminance(string hex)
    {
        if (!IsValidColor(hex))
            throw new FormatException($"Expected a color like #1A2B3C, got '{hex}'.");

        double Channel(int index)
        {
            var c = int.Parse(hex.AsSpan(1 + index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }
}
