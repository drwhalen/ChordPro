using System.Text.RegularExpressions;

namespace ChordPro.Core.Layout;

/// <summary>
/// Whether a chart line fits LivePrompter's screen. LivePrompter never wraps: anything wider is
/// cut off. The budget is 28 em of Arial (a typographic width, not a character count), the same
/// measure the library's converter uses when it breaks long lines.
/// </summary>
public static partial class LivePrompterWidth
{
    public const double BudgetEm = 28;

    // Arial advance widths per 1000 em; anything else counts as 556 (a digit's width).
    private static readonly Dictionary<char, int> Widths = new()
    {
        [' '] = 278, ['!'] = 278, ['"'] = 355, ['#'] = 556, ['$'] = 556, ['%'] = 889,
        ['&'] = 667, ['\''] = 191, ['('] = 333, [')'] = 333, ['*'] = 389, ['+'] = 584,
        [','] = 278, ['-'] = 333, ['.'] = 278, ['/'] = 278, ['0'] = 556, ['1'] = 556,
        ['2'] = 556, ['3'] = 556, ['4'] = 556, ['5'] = 556, ['6'] = 556, ['7'] = 556,
        ['8'] = 556, ['9'] = 556, [':'] = 278, [';'] = 278, ['<'] = 584, ['='] = 584,
        ['>'] = 584, ['?'] = 556, ['@'] = 1015, ['A'] = 667, ['B'] = 667, ['C'] = 722,
        ['D'] = 722, ['E'] = 667, ['F'] = 611, ['G'] = 778, ['H'] = 722, ['I'] = 278,
        ['J'] = 500, ['K'] = 667, ['L'] = 556, ['M'] = 833, ['N'] = 722, ['O'] = 778,
        ['P'] = 667, ['Q'] = 778, ['R'] = 722, ['S'] = 667, ['T'] = 611, ['U'] = 722,
        ['V'] = 667, ['W'] = 944, ['X'] = 667, ['Y'] = 667, ['Z'] = 611, ['['] = 278,
        ['\\'] = 278, [']'] = 278, ['^'] = 469, ['_'] = 556, ['`'] = 333, ['a'] = 556,
        ['b'] = 556, ['c'] = 500, ['d'] = 556, ['e'] = 556, ['f'] = 278, ['g'] = 556,
        ['h'] = 556, ['i'] = 222, ['j'] = 222, ['k'] = 500, ['l'] = 222, ['m'] = 833,
        ['n'] = 556, ['o'] = 556, ['p'] = 556, ['q'] = 556, ['r'] = 333, ['s'] = 500,
        ['t'] = 278, ['u'] = 556, ['v'] = 500, ['w'] = 722, ['x'] = 500, ['y'] = 500,
        ['z'] = 500, ['{'] = 334, ['|'] = 260, ['}'] = 334, ['~'] = 584, ['–'] = 556,
        ['—'] = 1000, ['‘'] = 191, ['’'] = 191, ['“'] = 355, ['”'] = 355, ['…'] = 1000,
    };

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex ChordRegex();

    [GeneratedRegex(@"^\{\s*(?:c|comment|ci|comment_italic|cb|comment_box|highlight)\s*:\s*(?<text>.*?)\s*\}$")]
    private static partial Regex CommentRegex();

    /// <summary>The width of <paramref name="text"/> in em.</summary>
    public static double Em(string text) => text.Sum(c => Widths.GetValueOrDefault(c, 556)) / 1000.0;

    /// <summary>
    /// The text LivePrompter lays out for a chart line: the lyric with its chords removed, or for a
    /// line of only chords, the chords without their brackets; a comment's text. Null for lines it
    /// doesn't draw as text (other directives, blank lines, # comments).
    /// </summary>
    public static string? ShownText(string line)
    {
        var s = line.TrimEnd();
        if (s.TrimStart().StartsWith('#') || s.Length == 0)
            return null;
        if (s.TrimStart().StartsWith('{'))
            return CommentRegex().Match(s.Trim()) is { Success: true } comment ? comment.Groups["text"].Value : null;
        var lyric = ChordRegex().Replace(s, "");
        return lyric.Trim().Length > 0 ? lyric.TrimEnd() : s.Replace("[", "").Replace("]", "");
    }

    /// <summary>
    /// For a line too wide to show, the part LivePrompter shows before cutting it off; null when it fits.
    /// </summary>
    public static string? CutOff(string line)
    {
        if (ShownText(line) is not { } text || Em(text) <= BudgetEm)
            return null;
        var width = 0.0;
        var end = 0;
        while (end < text.Length && (width += Widths.GetValueOrDefault(text[end], 556) / 1000.0) <= BudgetEm)
            end++;
        return text[..end];
    }
}
