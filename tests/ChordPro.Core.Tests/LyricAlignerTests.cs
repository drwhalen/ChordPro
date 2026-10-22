using ChordPro.Core;
using ChordPro.Core.Scrolling;

namespace ChordPro.Core.Tests;

public class LyricAlignerTests
{
    private static readonly Song Grace = ChordProParser.ParseFile(Path.Combine(SamplesFolder(), "amazing-grace.cho"));

    [Fact]
    public void FollowsRecognisedSpeechThroughTheVerses()
    {
        // What Whisper (base.en) heard in overlapping 4-second windows of the two verses read aloud,
        // mistakes included: "now I'm found" for "now am found", "Twas great" for "Twas grace".
        string[] windows =
        [
            "Amazing grace, how sweet the sound, that saved a wretch like me.",
            "that saved a wretch like me. I once was lost",
            "I once was lost, but now I'm found was blind.",
            "But now I'm found was blind, but now I see. Twas great.",
            "But now I see. Twas grace that taught my heart to fear and grace.",
            "that taught my heart to fear and grace my fears relieved.",
            "\"How precious did that grace appear?\"",
            "How precious did that grace appear the hour I first believed?",
        ];
        var aligner = new LyricAligner(Grace);

        var heard = windows.Select(w => aligner.Hear(w) is { } line ? $"{line.Section.Label} {line.LineIndex + 1}" : "-").ToList();

        Assert.Equal(
            ["Verse 1 2", "Verse 1 3", "Verse 1 4", "Verse 1 4", "Verse 2 2", "Verse 2 2", "Verse 2 3", "Verse 2 4"],
            heard);
    }

    [Fact]
    public void TellsARepeatedChorusFromTheFirst()
    {
        var aligner = new LyricAligner(Grace);
        var choruses = Grace.Sections.Where(s => s.Kind == SectionKind.Chorus).ToList();
        Assert.Equal(2, choruses.Count); // the chorus, and its {chorus} recall after verse 2

        aligner.MoveTo(Grace.Sections.First(s => s.Label == "Verse 1"), 3);
        Assert.Same(choruses[0], aligner.Hear("praise god praise god praise god for grace so free")?.Section);

        aligner.MoveTo(Grace.Sections.First(s => s.Label == "Verse 2"), 3);
        Assert.Same(choruses[1], aligner.Hear("praise god praise god praise god for grace so free")?.Section);
    }

    [Fact]
    public void KeepsItsPlaceWhenWhatIsHeardDoesNotMatch()
    {
        var aligner = new LyricAligner(Grace);
        aligner.Hear("I once was lost but now am found");
        var place = aligner.CurrentLine;

        // An instrumental: recognition makes up words, or hears only a few.
        Assert.Null(aligner.Hear("Thank you for watching."));
        Assert.Null(aligner.Hear("grace"));
        Assert.Equal(place, aligner.CurrentLine);
    }

    [Fact]
    public void DoesNotJumpToMatchingWordsFarAhead()
    {
        var aligner = new LyricAligner(Grace);
        aligner.MoveTo(Grace.Sections.First(s => s.Label == "Verse 1"), 0);

        // "grace" and "how" are all over the song; the nearest good match ahead wins.
        Assert.Equal(("Verse 1", 0), aligner.Hear("amazing grace how sweet") is { } line ? (line.Section.Label, line.LineIndex) : default);
        // "sweet" is the fourth of the line's six words ("Amazing grace, how sweet the sound").
        Assert.Equal(4 / 6.0, aligner.CurrentFraction, 3);
    }

    // An original song whose chorus repeats one line four times, and comes round twice.
    private static readonly Song Repeats = ChordProParser.Parse("""
        {title: Hold On}
        {start_of_verse: Verse 1}
        [G]Rain on the window and the [C]road is long
        [G]Coffee gone cold but the [D]radio's on
        {end_of_verse}
        {start_of_chorus}
        [C]Hold on, hold on, hold on
        [G]Hold on, hold on, hold on
        [C]Hold on, hold on, hold on
        [G]Hold on, hold on, hold on
        [C]Hold on to the [D]morning light
        {end_of_chorus}
        {start_of_verse: Verse 2}
        [G]Lamps in the harbour and the [C]tide comes in
        [G]Nobody knows where the [D]night has been
        {end_of_verse}
        {chorus}
        """);

    private static string LineName(Song song, Section section, int index)
    {
        var choruses = song.Sections.Where(s => s.Kind == SectionKind.Chorus).ToList();
        return (section.Kind == SectionKind.Chorus ? $"Chorus {choruses.IndexOf(section) + 1}" : section.Label) + $" {index + 1}";
    }

    // Sings the song at a steady pace and asks the aligner every second, as Follow voice does, with
    // what was heard in the last four seconds (recognition drops every fourth word), with a pause
    // between sections. Returns, for each second, the line being sung and the line the aligner had placed.
    private static List<(string Sung, string Placed)> Sing(LyricAligner aligner, Song song, double wordsPerSecond, double sectionGap = 0)
    {
        var sung = new List<(double Time, string Word, string Line)>();
        var time = 2.0;
        foreach (var section in song.Sections)
        {
            time += sectionGap; // an instrumental between sections: the last words stay in the window
            for (var i = 0; i < section.Lines.Count; i++)
            {
                if (section.Lines[i] is not LyricLine line)
                    continue;
                foreach (var word in LyricAligner.Words(string.Concat(line.Segments.Select(s => s.Lyric))))
                    sung.Add((time += 1 / wordsPerSecond, word, LineName(song, section, i)));
                time += 0.8; // a breath between lines
            }
        }
        var result = new List<(string, string)>();
        var placed = "-";
        for (var now = Math.Ceiling(sung[0].Time) + 1; now <= sung[^1].Time + 1; now += 1)
        {
            var heard = string.Join(" ", sung.Select((w, n) => (w, n)).Where(x => x.w.Time > now - 4 && x.w.Time <= now && x.n % 4 != 3).Select(x => x.w.Word));
            if (aligner.Hear(heard, now) is { } at)
                placed = LineName(song, at.Section, at.LineIndex);
            result.Add((sung.Last(w => w.Time <= now).Line, placed));
        }
        return result;
    }

    [Theory]
    [InlineData(1.5, 0)]
    [InlineData(2.5, 0)]
    [InlineData(2, 3)]   // an instrumental must not be taken for very slow singing
    public void MovesOnThroughRepeatedLinesAtTheSingersPace(double wordsPerSecond, double sectionGap)
    {
        var steps = Sing(new LyricAligner(Repeats), Repeats, wordsPerSecond, sectionGap);
        var lines = Repeats.Sections.SelectMany(s => s.Lines.Select((l, i) => (l, i, s))).Where(x => x.l is LyricLine)
            .Select(x => (Name: LineName(Repeats, x.s, x.i), Text: string.Join(" ", LyricAligner.Words(string.Concat(((LyricLine)x.l).Segments.Select(g => g.Lyric))))))
            .ToList();
        // Identical lines ("hold on, hold on, hold on" four times) look the same on screen, so which
        // copy is placed doesn't matter; what does is reaching each different line, in order and in time.
        string Text(string name) => lines.Find(l => l.Name == name).Text ?? "";
        int Index(string name) => lines.FindIndex(l => l.Name == name);
        static IEnumerable<string> Collapse(IEnumerable<string> texts) => texts.Where((t, i) => i == 0 || t != texts.ElementAt(i - 1));

        Assert.Equal(Collapse(lines.Select(l => l.Text)), Collapse(steps.Where(s => s.Placed != "-").Select(s => Text(s.Placed))));
        Assert.DoesNotContain(steps, s => s.Placed.StartsWith("Chorus 2") && Index(s.Sung) < Index("Chorus 2 1"));
        var keepingUp = steps.Count(s => Text(s.Placed) == Text(s.Sung) || Index(s.Sung) - Index(s.Placed) == 1);
        Assert.True(keepingUp >= steps.Count * 0.85, $"kept up {keepingUp} of {steps.Count}:\n" + string.Join("\n", steps.Select(s => $"{s.Sung} -> {s.Placed}")));
    }

    [Fact]
    public void FollowsAChorusSungAgainWhenItIsWrittenOnce()
    {
        // Charts often write a chorus once and mean it twice ("x2"); the singer goes back to its start.
        var song = ChordProParser.Parse("""
            {start_of_verse: Verse 1}
            [G]Lamps in the harbour and the [C]tide comes in
            {end_of_verse}
            {start_of_chorus}
            [C]Carry me home on the evening train
            [G]Wake me up when we're there again
            {end_of_chorus}
            {start_of_verse: Verse 2}
            [G]Nobody knows where the [D]night has been
            {end_of_verse}
            """);
        var aligner = new LyricAligner(song);
        double time = 0;
        string? Place(string words) => aligner.Hear(words, time += 2) is { } at ? $"{at.Section.Label ?? "Chorus"} {at.LineIndex + 1}" : null;

        Assert.Equal("Verse 1 1", Place("lamps in the harbour and the tide"));
        Assert.Equal("Chorus 1", Place("carry me home on the evening"));
        Assert.Equal("Chorus 2", Place("wake me up when we're there again"));
        Assert.Equal("Chorus 1", Place("again carry me home on the evening train"));   // and again
        Assert.Equal("Chorus 2", Place("wake me up when we're there"));
        Assert.Equal("Verse 2 1", Place("nobody knows where the night"));
    }

    [Theory]
    [InlineData("'Twas grace, that taught", new[] { "twas", "grace", "that", "taught" })]
    [InlineData("Don’t  hallelu-jah!", new[] { "dont", "hallelujah" })]
    [InlineData("Café 5 o'clock", new[] { "cafe", "5", "oclock" })]
    public void SplitsTextIntoPlainWords(string text, string[] expected) =>
        Assert.Equal(expected, LyricAligner.Words(text));

    private static string SamplesFolder()
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "samples")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("samples");
        return Path.Combine(dir, "samples");
    }
}
