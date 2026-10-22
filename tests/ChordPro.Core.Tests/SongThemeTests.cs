using ChordPro.Core.Themes;

namespace ChordPro.Core.Tests;

public class SongThemeTests
{
    [Theory]
    [InlineData("#000000", "#FFFFFF", 21)]
    [InlineData("#FFFFFF", "#000000", 21)]
    [InlineData("#777777", "#777777", 1)]
    [InlineData("#767676", "#FFFFFF", 4.54)] // the classic "just passes AA" gray
    public void ComputesWcagContrastRatio(string foreground, string background, double expected) =>
        Assert.Equal(expected, SongTheme.ContrastRatio(foreground, background), precision: 2);

    public static TheoryData<string> HighContrastThemes =>
        [.. SongTheme.BuiltIn.Where(t => t.Name.StartsWith("High contrast")).Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(HighContrastThemes))]
    public void HighContrastThemesMeetAaaForLyricsChordsAndLabels(string name)
    {
        var theme = SongTheme.BuiltIn.Single(t => t.Name == name);

        Assert.True(SongTheme.ContrastRatio(theme.Text, theme.Background) >= 7);
        Assert.True(SongTheme.ContrastRatio(theme.MutedText, theme.Background) >= 7);
        Assert.True(SongTheme.ContrastRatio(theme.Chord, theme.Background) >= 7);
        Assert.True(SongTheme.ContrastRatio(theme.Text, theme.CommentBackground) >= 7);
    }

    [Fact]
    public void EveryBuiltInThemeKeepsLyricsAndChordsReadable()
    {
        foreach (var theme in SongTheme.BuiltIn)
        {
            Assert.True(SongTheme.ContrastRatio(theme.Text, theme.Background) >= 4.5, theme.Name);
            Assert.True(SongTheme.ContrastRatio(theme.Chord, theme.Background) >= 4.5, theme.Name);
        }
    }

    [Fact]
    public void BuiltInNamesAreUniqueAndDontClashWithSystem()
    {
        var names = SongTheme.BuiltIn.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.DoesNotContain(SongTheme.SystemThemeName, names);
    }

    [Fact]
    public void CloneIsIndependentCopy()
    {
        var copy = SongTheme.Night.Clone("Mine");
        copy.Chord = "#00FF00";

        Assert.Equal("Mine", copy.Name);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal("#FB923C", SongTheme.Night.Chord);
    }

    [Theory]
    [InlineData("#1A2B3C", true)]
    [InlineData("#1a2b3c", true)]
    [InlineData("1A2B3C", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData(null, false)]
    public void ValidatesHexColors(string? hex, bool valid) => Assert.Equal(valid, SongTheme.IsValidColor(hex));
}
