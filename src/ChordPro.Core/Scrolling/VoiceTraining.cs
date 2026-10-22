namespace ChordPro.Core.Scrolling;

/// <summary>A word recognized in a recording, and when it started (seconds from the start).</summary>
public sealed record TimedWord(string Text, double Start);

/// <summary>What recognition heard in one window of a recording, as Follow voice would have heard it live.</summary>
/// <param name="End">When the window ended, in seconds from the start of the recording.</param>
/// <param name="Level">The window's level (RMS, 0–1).</param>
public sealed record HeardWindow(double End, float Level, string Text);

/// <summary>How well Follow voice kept up with a recording: the share of the time it showed the line
/// being sung, and the share it was within a line of it.</summary>
public sealed record TrainingScore(double Exact, double Near)
{
    /// <summary>One number to compare by: being on the line matters most, being close still helps.</summary>
    public double Rank => 0.6 * Exact + 0.4 * Near;
}

/// <summary>
/// Which lyric line was being sung when, worked out from a whole recording at once. Recognizing the
/// whole recording (rather than a few seconds at a time, as Follow voice must) and aligning all of
/// it to the lyrics gives a timeline good enough to judge the live follower against.
/// </summary>
public sealed class ReferenceTimeline(IReadOnlyList<(double Time, int Line)> points, double coverage)
{
    /// <summary>When each matched word started, with its line (index in <see cref="LyricAligner.Lines"/>).</summary>
    public IReadOnlyList<(double Time, int Line)> Points { get; } = points;

    /// <summary>The share of the song's lyric words found in the recording.</summary>
    public double Coverage { get; } = coverage;

    public bool IsEmpty => Points.Count == 0;
    public double Start => IsEmpty ? 0 : Points[0].Time;
    public double End => IsEmpty ? 0 : Points[^1].Time;

    /// <summary>The line being sung at a time: the line of the last word started by then, or -1 before the first.</summary>
    public int LineAt(double time)
    {
        var line = -1;
        foreach (var point in Points)
        {
            if (point.Time > time)
                break;
            line = point.Line;
        }
        return line;
    }
}

/// <summary>
/// Tunes Follow voice to a singer from a recording of them singing a song: replays the recording as
/// the live follower would hear it under different settings, scores each against the
/// <see cref="ReferenceTimeline"/>, and picks the settings that kept up best. Recognition itself
/// happens elsewhere (it needs Whisper); this takes what was heard.
/// </summary>
public static class VoiceTraining
{
    // Alignment scores for the reference: a word heard right or nearly right, a wrong word, and a
    // word skipped on either side (recognition misses words and adds a few).
    private const double Mismatch = -1, Gap = -0.6;

    /// <summary>The settings tried, besides those that depend on how the recording was transcribed.</summary>
    public static readonly double[] Levels = [0.001, 0.002, 0.003, 0.006, 0.012];
    public static readonly int[] MinWordChoices = [2, 3, 4];
    public static readonly double[] Windows = [3, 4, 5];

    /// <summary>
    /// Aligns a whole recording's words to the song's lyrics, in order (a global alignment, so a
    /// repeated chorus is matched to the right copy by everything around it).
    /// </summary>
    public static ReferenceTimeline Reference(LyricAligner aligner, IReadOnlyList<TimedWord> recognized)
    {
        var heard = recognized
            .SelectMany(w => LyricAligner.Words(w.Text).Select(text => (Text: text, w.Start)))
            .ToList();
        var lyrics = aligner.LyricWords;
        int n = lyrics.Count, m = heard.Count;
        if (n == 0 || m == 0)
            return new([], 0);

        // Smith-Waterman over the whole song: start and end anywhere (the recording may begin with
        // talking or stop early), with traceback to recover which heard word matched which lyric.
        var score = new double[n + 1, m + 1];
        var from = new byte[n + 1, m + 1];   // 0 start, 1 diagonal, 2 lyric skipped, 3 heard word extra
        var best = (Score: 0.0, I: 0, J: 0);
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var similarity = LyricAligner.Similarity(heard[j - 1].Text, lyrics[i - 1].Word);
                var (cell, direction) = (0.0, (byte)0);
                var diagonal = score[i - 1, j - 1] + (similarity > 0 ? similarity : Mismatch);
                if (diagonal > cell)
                    (cell, direction) = (diagonal, (byte)1);
                if (score[i - 1, j] + Gap > cell)
                    (cell, direction) = (score[i - 1, j] + Gap, (byte)2);
                if (score[i, j - 1] + Gap > cell)
                    (cell, direction) = (score[i, j - 1] + Gap, (byte)3);
                (score[i, j], from[i, j]) = (cell, direction);
                if (cell > best.Score)
                    best = (cell, i, j);
            }
        }

        var points = new List<(double Time, int Line)>();
        for (int i = best.I, j = best.J; i > 0 && j > 0 && from[i, j] != 0;)
        {
            switch (from[i, j])
            {
                case 1:
                    if (LyricAligner.Similarity(heard[j - 1].Text, lyrics[i - 1].Word) > 0)
                        points.Add((heard[j - 1].Start, lyrics[i - 1].Line));
                    (i, j) = (i - 1, j - 1);
                    break;
                case 2: i--; break;
                default: j--; break;
            }
        }
        points.Reverse();
        return new(points, points.Count / (double)n);
    }

    /// <summary>
    /// Replays what was heard through a <see cref="LyricAligner"/> under the given settings, as Follow
    /// voice would live, and returns the line it showed after each window (index in its Lines).
    /// </summary>
    /// <param name="windows">Every window transcribed, at the finest interval tried; those the
    /// tuning's interval wouldn't have heard are skipped.</param>
    public static List<(double Time, int Line)> Simulate(Song song, VoiceTuning tuning, IEnumerable<HeardWindow> windows)
    {
        var aligner = new LyricAligner(song, tuning.MinWords);
        var followed = new List<(double Time, int Line)>();
        foreach (var window in windows.OrderBy(w => w.End))
        {
            var ticks = window.End / tuning.IntervalSeconds;
            if (Math.Abs(ticks - Math.Round(ticks)) > 1e-6 || window.Level < tuning.MinLevel || !window.Text.Any(char.IsLetter))
                continue;
            aligner.Hear(window.Text, window.End);
            if (aligner.CurrentLineNumber >= 0)
                followed.Add((window.End, aligner.CurrentLineNumber));
        }
        return followed;
    }

    /// <summary>
    /// Compares the lines Follow voice showed with the reference, every <paramref name="step"/>
    /// seconds from the first word sung to a moment after the last.
    /// </summary>
    public static TrainingScore Score(ReferenceTimeline reference, IReadOnlyList<(double Time, int Line)> followed, double step = 0.5)
    {
        if (reference.IsEmpty)
            return new(0, 0);
        int ticks = 0, exact = 0, near = 0, next = 0, shown = -1;
        for (var t = reference.Start; t <= reference.End + 2; t += step)
        {
            while (next < followed.Count && followed[next].Time <= t)
                shown = followed[next++].Line;
            var line = reference.LineAt(t);
            ticks++;
            if (shown == line)
                exact++;
            if (shown >= 0 && Math.Abs(shown - line) <= 1)
                near++;
        }
        return new(exact / (double)ticks, near / (double)ticks);
    }

    /// <summary>
    /// Tries every combination of settings on what was heard and returns the best, with the score of
    /// <paramref name="current"/> for comparison (null if it couldn't be tried on these passes).
    /// </summary>
    /// <param name="passes">What was heard with each window length and with or without lyric hints:
    /// windows ending every second, plus every half second where that was transcribed too.</param>
    public static (VoiceTuning Best, TrainingScore BestScore, TrainingScore? CurrentScore) Choose(
        Song song, ReferenceTimeline reference,
        IReadOnlyDictionary<(double Window, bool Prompt), IReadOnlyList<HeardWindow>> passes, VoiceTuning current)
    {
        TrainingScore Try(VoiceTuning tuning) =>
            Score(reference, Simulate(song, tuning, passes[(tuning.WindowSeconds, tuning.UsePrompt)]));

        var currentScore = Covers(passes, current) ? Try(current) : null;
        var (best, bestScore) = (current, currentScore ?? new TrainingScore(0, 0));
        foreach (var ((window, prompt), heard) in passes)
        {
            double[] intervals = heard.Any(w => Math.Abs(w.End % 1 - 0.5) < 1e-6) ? [1, 0.5] : [1];
            foreach (var interval in intervals)
            foreach (var level in Levels)
            foreach (var words in MinWordChoices)
            {
                var tuning = new VoiceTuning(level, window, interval, prompt, words);
                var score = Try(tuning);
                // Change settings only for a real gain: a point or two is noise on one recording.
                if (score.Rank > bestScore.Rank + (best == current ? 0.02 : 0))
                    (best, bestScore) = (tuning, score);
            }
        }
        return (best, bestScore, currentScore);
    }

    private static bool Covers(IReadOnlyDictionary<(double Window, bool Prompt), IReadOnlyList<HeardWindow>> passes, VoiceTuning tuning) =>
        passes.TryGetValue((tuning.WindowSeconds, tuning.UsePrompt), out var heard)
        && (tuning.IntervalSeconds == 1 || (tuning.IntervalSeconds == 0.5 && heard.Any(w => Math.Abs(w.End % 1 - 0.5) < 1e-6)));
}
