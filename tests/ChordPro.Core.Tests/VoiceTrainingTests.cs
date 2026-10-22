using ChordPro.Core;
using ChordPro.Core.Scrolling;

namespace ChordPro.Core.Tests;

public class VoiceTrainingTests
{
    private static readonly Song Grace = ChordProParser.ParseFile(Path.Combine(SamplesFolder(), "amazing-grace.cho"));

    private static readonly Song Repeats = ChordProParser.Parse("""
        {start_of_verse: Verse 1}
        Rain on the window and the road is long
        {end_of_verse}
        {start_of_chorus}
        Hold on to the morning light
        {end_of_chorus}
        {start_of_verse: Verse 2}
        Lamps in the harbor and the tide comes in
        {end_of_verse}
        {start_of_chorus}
        Hold on to the morning light
        {end_of_chorus}
        """);

    // The song sung at an even pace (two words a second), as recognized from the whole recording.
    private static List<TimedWord> SungEvenly(LyricAligner aligner, double start = 1) =>
        [.. aligner.LyricWords.Select((w, i) => new TimedWord(w.Word, start + i * 0.5))];

    [Fact]
    public void ReferenceFollowsTheWholeRecordingDespiteMistakes()
    {
        var aligner = new LyricAligner(Grace);
        var words = SungEvenly(aligner);
        words[1] = words[1] with { Text = "great" };           // "grace", misheard
        words.Insert(3, new TimedWord("um", words[3].Start));  // something extra

        var reference = VoiceTraining.Reference(aligner, words);

        Assert.True(reference.Coverage > 0.95);
        Assert.Equal(-1, reference.LineAt(0.5));
        Assert.Equal(0, reference.LineAt(1.2));
        var secondLine = aligner.LyricWords.ToList().FindIndex(w => w.Line == 1);
        Assert.Equal(1, reference.LineAt(1 + secondLine * 0.5 + 0.1));
    }

    [Fact]
    public void ReferencePlacesARepeatedChorusByWhatCameBefore()
    {
        var aligner = new LyricAligner(Repeats);
        var reference = VoiceTraining.Reference(aligner, SungEvenly(aligner));

        // The second "Hold on to the morning light" is the last line, not the first chorus.
        Assert.Equal(aligner.Lines.Count - 1, reference.LineAt(reference.End));
        Assert.Equal([0, 1, 2, 3], reference.Points.Select(p => p.Line).Distinct());
    }

    [Fact]
    public void AFollowerOnTheRightLineScoresFull()
    {
        var aligner = new LyricAligner(Grace);
        var reference = VoiceTraining.Reference(aligner, SungEvenly(aligner));
        var perfect = Enumerable.Range(0, (int)(reference.End * 4) + 20)
            .Select(i => (Time: i * 0.25, Line: reference.LineAt(i * 0.25)))
            .ToList();

        var score = VoiceTraining.Score(reference, perfect);

        Assert.Equal(1, score.Exact, 3);
        Assert.Equal(1, score.Near, 3);
        Assert.Equal(0, VoiceTraining.Score(reference, []).Near);
    }

    [Fact]
    public void SimulationSkipsQuietWindowsAndThoseBetweenIntervals()
    {
        var aligner = new LyricAligner(Grace);
        var first = string.Join(" ", aligner.LyricWords.Where(w => w.Line == 0).Select(w => w.Word));
        HeardWindow[] heard =
        [
            new(4, 0.002f, first),     // too quiet for the default gate
            new(4.5, 0.05f, first),    // between whole seconds
            new(5, 0.05f, first),
        ];

        Assert.Equal([(5.0, 0)], VoiceTraining.Simulate(Grace, VoiceTuning.Default, heard));
        Assert.Equal(3, VoiceTraining.Simulate(Grace, new VoiceTuning(MinLevel: 0.001, IntervalSeconds: 0.5), heard).Count);
    }

    [Fact]
    public void TrainingLowersTheGateForAQuietSinger()
    {
        var aligner = new LyricAligner(Grace);
        var sung = SungEvenly(aligner);
        var reference = VoiceTraining.Reference(aligner, sung);
        // What 4-second windows heard each second: the words sung in them, all quieter than the default gate.
        var heard = Enumerable.Range(4, (int)reference.End)
            .Select(end => new HeardWindow(end, 0.0025f,
                string.Join(" ", sung.Where(w => w.Start > end - 4 && w.Start <= end).Select(w => w.Text))))
            .ToList();
        var passes = new Dictionary<(double, bool), IReadOnlyList<HeardWindow>> { [(4, false)] = heard };

        var (best, bestScore, currentScore) = VoiceTraining.Choose(Grace, reference, passes, VoiceTuning.Default);

        Assert.Equal(0, currentScore!.Near);
        Assert.True(best.MinLevel <= 0.002);
        Assert.True(bestScore.Near > 0.8, $"near {bestScore.Near}");
    }

    [Fact]
    public void KeepsTheCurrentSettingsWithoutARealGain()
    {
        var aligner = new LyricAligner(Grace);
        var reference = VoiceTraining.Reference(aligner, SungEvenly(aligner));
        var passes = new Dictionary<(double, bool), IReadOnlyList<HeardWindow>> { [(4, false)] = [] };

        var (best, _, _) = VoiceTraining.Choose(Grace, reference, passes, VoiceTuning.Default);

        Assert.Same(VoiceTuning.Default, best);
    }

    [Fact]
    public void FewerWordsPlaceTheSingerSooner()
    {
        Assert.Null(new LyricAligner(Grace).Hear("amazing grace"));
        Assert.Equal(0, new LyricAligner(Grace, minWords: 2).Hear("amazing grace")?.LineIndex);
    }

    [Fact]
    public void PromptIsTheLyricsBeforeTheCurrentLine()
    {
        var aligner = new LyricAligner(Grace);
        Assert.Null(aligner.PromptText());
        aligner.Hear("I once was lost but now am found");

        var prompt = aligner.PromptText();

        Assert.NotNull(prompt);
        Assert.Contains("wretch", prompt);
        Assert.DoesNotContain("found", prompt);
    }

    private static string SamplesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "samples");
    }
}
