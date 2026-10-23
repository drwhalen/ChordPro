using System.Text.RegularExpressions;

namespace ChordPro.Core;

/// <summary>
/// The text changes behind the editor's Insert menu: wrapping lines in a section, or putting a
/// directive on a line of its own, with where the cursor goes after.
/// </summary>
public static partial class EditorInsert
{
    [GeneratedRegex(@"\{\s*(?:start_of_verse|sov)\b[^}]*?Verse\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedVerseRegex();

    [GeneratedRegex(@"^\s*\{\s*key\s*:\s*([^}]+?)\s*\}\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex KeyRegex();

    /// <summary>
    /// The key in force at <paramref name="caret"/>: the last <c>{key}</c> before it, or the song's
    /// first one when the cursor is above them all (in the header). Null when the song has none.
    /// </summary>
    public static string? KeyAt(string text, int caret)
    {
        var keys = KeyRegex().Matches(text);
        if (keys.Count == 0)
            return null;
        var before = keys.LastOrDefault(m => m.Index <= Math.Clamp(caret, 0, text.Length));
        return (before ?? keys[0]).Groups[1].Value;
    }

    /// <summary>How many <c>{key}</c> lines the song has (more than one: it changes key).</summary>
    public static int KeyCount(string text) => KeyRegex().Count(text);

    /// <summary>"Verse 3" when the song already has verses 1 and 2.</summary>
    public static string NextVerseLabel(string text)
    {
        var numbers = NumberedVerseRegex().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        return $"Verse {(numbers.Count == 0 ? 1 : numbers.Max() + 1)}";
    }

    /// <summary>
    /// Wraps the lines the selection touches in <c>{start_of_<paramref name="name"/>: label="…"}</c>
    /// … <c>{end_of_<paramref name="name"/>}</c>; with nothing selected, adds an empty section at the
    /// cursor's line, with the cursor on the empty line inside it.
    /// </summary>
    public static (string Text, int Caret) Section(string text, int selectionStart, int selectionEnd, string name, string? label)
    {
        var (from, to) = (Math.Min(selectionStart, selectionEnd), Math.Max(selectionStart, selectionEnd));
        from = Math.Clamp(from, 0, text.Length);
        to = Math.Clamp(to, 0, text.Length);
        var start = LineStart(text, from);
        var open = label is null ? $"{{start_of_{name}}}" : $"{{start_of_{name}: label=\"{label.Replace("\"", "\\\"")}\"}}";
        var close = $"{{end_of_{name}}}";
        if (from == to)
        {
            // An empty section on the cursor's line (or above it, when the line has text).
            var block = $"{open}\n\n{close}\n";
            return (text.Insert(start, block), start + open.Length + 1);
        }
        // A selection ending at the very start of a line doesn't take that line.
        var lastLineEnd = to > from && to > 0 && text[to - 1] == '\n' ? to - 1 : LineEnd(text, to);
        var lines = text[start..lastLineEnd];
        var wrapped = $"{open}\n{lines}\n{close}";
        return (text[..start] + wrapped + text[lastLineEnd..], start + wrapped.Length);
    }

    /// <summary>
    /// Puts <paramref name="line"/> on a line of its own: on the cursor's line when it's empty,
    /// otherwise just above it. The cursor goes to <paramref name="caretInLine"/> characters into
    /// the new line (the end of it when null).
    /// </summary>
    public static (string Text, int Caret) Line(string text, int caret, string line, int? caretInLine = null)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = LineStart(text, caret);
        var end = LineEnd(text, caret);
        var offset = caretInLine ?? line.Length;
        if (text[start..end].Trim().Length == 0)
            return (text[..start] + line + text[end..], start + offset);
        return (text.Insert(start, line + "\n"), start + offset);
    }

    private static int LineStart(string text, int at) => at <= 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;

    private static int LineEnd(string text, int at)
    {
        var end = text.IndexOf('\n', Math.Min(at, text.Length));
        return end < 0 ? text.Length : end;
    }
}
