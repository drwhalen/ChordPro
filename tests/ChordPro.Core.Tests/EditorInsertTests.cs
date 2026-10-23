namespace ChordPro.Core.Tests;

public class EditorInsertTests
{
    [Fact]
    public void WrapsTheSelectedLinesInASection()
    {
        var text = "{title: T}\n[G]One\n[C]Two\n[D]Three\n";
        var from = text.IndexOf("One");           // mid-line selections take whole lines
        var to = text.IndexOf("Two") + 2;
        var (result, caret) = EditorInsert.Section(text, from, to, "verse", "Verse 1");

        Assert.Equal("{title: T}\n{start_of_verse: label=\"Verse 1\"}\n[G]One\n[C]Two\n{end_of_verse}\n[D]Three\n", result);
        Assert.Equal(result.IndexOf("{end_of_verse}") + "{end_of_verse}".Length, caret);
    }

    [Fact]
    public void ASelectionEndingAtALineStartLeavesThatLineOut()
    {
        var text = "[G]One\n[C]Two\n";
        var (result, _) = EditorInsert.Section(text, 0, text.IndexOf("[C]"), "chorus", null);
        Assert.Equal("{start_of_chorus}\n[G]One\n{end_of_chorus}\n[C]Two\n", result);
    }

    [Fact]
    public void WithNothingSelectedAddsAnEmptySectionWithTheCursorInside()
    {
        var text = "[G]One\n";
        var (result, caret) = EditorInsert.Section(text, text.Length, text.Length, "bridge", "Bridge");
        Assert.Equal("[G]One\n{start_of_bridge: label=\"Bridge\"}\n\n{end_of_bridge}\n", result);
        Assert.Equal('\n', result[caret]);
        Assert.Equal('}', result[caret - 2]);
    }

    [Fact]
    public void PutsADirectiveOnALineOfItsOwn()
    {
        var text = "[G]One\n\n[C]Two";
        // on an empty line: there
        var (onEmpty, _) = EditorInsert.Line(text, text.IndexOf("\n\n") + 1, "{chorus}");
        Assert.Equal("[G]One\n{chorus}\n[C]Two", onEmpty);
        // on a line with text: above it, with the cursor where asked
        var (above, caret) = EditorInsert.Line(text, text.IndexOf("Two"), "{comment: }", "{comment: ".Length);
        Assert.Equal("[G]One\n\n{comment: }\n[C]Two", above);
        Assert.Equal(above.IndexOf("{comment: }") + "{comment: ".Length, caret);
    }

    [Fact]
    public void NumbersTheNextVerse()
    {
        Assert.Equal("Verse 1", EditorInsert.NextVerseLabel("{title: T}"));
        Assert.Equal("Verse 3", EditorInsert.NextVerseLabel("{start_of_verse: label=\"Verse 1\"}\n{start_of_verse: label=\"Verse 2\"}"));
    }
}
