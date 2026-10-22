using System.Diagnostics;
using ChordPro.Core;
using ChordPro.Core.Scrolling;

namespace ChordPro.Viewer;

/// <summary>What training found: the best settings and how they compare with the ones in use.</summary>
public sealed record TrainingResult(VoiceTuning Best, TrainingScore BestScore, VoiceTuning Current, TrainingScore? CurrentScore,
    ReferenceTimeline Reference)
{
    public bool Improved => Best != Current;
}

/// <summary>Training couldn't be done on this recording, with a reason for people.</summary>
public sealed class TrainingException(string message) : Exception(message);

/// <summary>
/// Tunes Follow voice from a recording of the singer: recognizes the whole recording to learn when
/// each line was sung, then recognizes it again a window at a time, as Follow voice would live, with
/// each window length and with and without lyric hints, and lets <see cref="VoiceTraining"/> replay
/// those under every combination of the other settings. All on this computer.
/// </summary>
public static class VoiceTrainer
{
    private const int Rate = Transcriber.SampleRate;

    // Live, a window must be recognized before the next is due; half-second intervals are only
    // tried when recognition is this quick here.
    private const double QuickEnoughForHalfSeconds = 0.3;

    public static Task<TrainingResult> TrainAsync(string modelPath, Song song, float[] audio, VoiceTuning current,
        IProgress<(double Fraction, string? Status)> progress, CancellationToken cancel) =>
        Task.Run(() => Train(modelPath, song, audio, current, progress, cancel), cancel);

    private static async Task<TrainingResult> Train(string modelPath, Song song, float[] audio, VoiceTuning current,
        IProgress<(double Fraction, string? Status)> progress, CancellationToken cancel)
    {
        var seconds = audio.Length / (double)Rate;
        if (seconds < 20)
            throw new TrainingException("The recording is too short. Sing at least a verse and a chorus (a whole song is best).");

        using var transcriber = new Transcriber(modelPath);
        progress.Report((0, "Listening to the whole recording…"));
        var words = await transcriber.TranscribeWordsAsync(audio, cancel);
        var reference = VoiceTraining.Reference(new LyricAligner(song), words);
        if (reference.Points.Count < 15 || reference.Coverage < 0.2)
            throw new TrainingException(reference.Points.Count == 0
                ? "No singing was recognized in the recording. Check that the right microphone was used and that it isn't muted."
                : $"Only {reference.Coverage:P0} of the lyrics were recognized. Is this recording of “{song.Title ?? "this song"}”? Sing the song as written, close to the microphone.");

        // Passes: every window length, with and without hints, windows ending each second; then the
        // best of those again ending on the half seconds. Progress counts windows.
        var keys = VoiceTraining.Windows.SelectMany(w => new[] { (w, false), (w, true) }).ToList();
        var perPass = Math.Max(1, (int)seconds);
        var total = (keys.Count + 1) * perPass;
        var done = 0;
        var passes = new Dictionary<(double Window, bool Prompt), IReadOnlyList<HeardWindow>>();
        var timing = Stopwatch.StartNew();
        foreach (var (window, prompt) in keys)
        {
            progress.Report((0.05 + 0.95 * done / total,
                $"Trying {window:0} second windows{(prompt ? " with lyric hints" : "")}…"));
            passes[(window, prompt)] = await PassAsync(transcriber, song, audio, window, prompt, 0, () =>
            {
                done++;
                progress.Report((0.05 + 0.95 * done / total, null));
            }, cancel);
        }
        var perWindow = timing.Elapsed.TotalSeconds / Math.Max(1, done);

        var (best, _, _) = VoiceTraining.Choose(song, reference, passes, current);
        if (perWindow < QuickEnoughForHalfSeconds)
        {
            // Try the half-second interval with the best window, and with the current one for comparison.
            foreach (var key in new[] { (best.WindowSeconds, best.UsePrompt), (current.WindowSeconds, current.UsePrompt) }.Distinct())
            {
                if (!passes.TryGetValue(key, out var whole))
                    continue;
                progress.Report((0.05 + 0.95 * done / total, "Trying shorter intervals…"));
                var halves = await PassAsync(transcriber, song, audio, key.Item1, key.Item2, 0.5, () =>
                {
                    done = Math.Min(total - 1, done + 1);
                    progress.Report((0.05 + 0.95 * done / total, null));
                }, cancel);
                passes[key] = [.. whole.Concat(halves).OrderBy(w => w.End)];
            }
        }

        progress.Report((1, "Comparing settings…"));
        var (chosen, chosenScore, currentScore) = VoiceTraining.Choose(song, reference, passes, current);
        return new TrainingResult(chosen, chosenScore, current, currentScore, reference);
    }

    // Recognizes the recording a window at a time, windows ending every second (plus offset), as
    // Follow voice would hear it live. Quiet windows aren't recognized, so the gate can be tried later.
    private static async Task<List<HeardWindow>> PassAsync(Transcriber transcriber, Song song, float[] audio,
        double windowSeconds, bool prompt, double offset, Action step, CancellationToken cancel)
    {
        var length = (int)(windowSeconds * Rate);
        // With hints, the hint is what a follower with the usual settings would have placed by then.
        var aligner = prompt ? new LyricAligner(song) : null;
        var heard = new List<HeardWindow>();
        for (var end = windowSeconds + offset; end * Rate <= audio.Length; end += 1)
        {
            cancel.ThrowIfCancellationRequested();
            var window = audio.AsSpan((int)(end * Rate) - length, length).ToArray();
            var level = Transcriber.Level(window);
            var text = level >= VoiceTraining.Levels.Min()
                ? await transcriber.TranscribeAsync(window, aligner?.PromptText(), cancel)
                : "";
            if (aligner is not null && level >= VoiceTuning.DefaultMinLevel && text.Length > 0)
                aligner.Hear(text, end);
            heard.Add(new HeardWindow(end, level, text));
            step();
        }
        return heard;
    }
}
