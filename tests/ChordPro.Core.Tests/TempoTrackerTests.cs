using ChordPro.Core.Scrolling;

namespace ChordPro.Core.Tests;

public class TempoTrackerTests
{
    private const int SampleRate = 16000;

    // A strum-like burst on every beat: a quick attack decaying over ~80 ms, over faint noise.
    private static float[] Clicks(double bpm, double seconds, double swing = 0, int seed = 1)
    {
        var random = new Random(seed);
        var samples = new float[(int)(seconds * SampleRate)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)((random.NextDouble() - 0.5) * 0.002);

        var beat = 60.0 / bpm;
        for (var n = 0; n * beat < seconds; n++)
        {
            var start = (int)((n * beat + (n % 2 == 1 ? swing : 0)) * SampleRate);
            for (var j = 0; j < SampleRate * 0.08 && start + j < samples.Length; j++)
                samples[start + j] += (float)((random.NextDouble() - 0.5) * Math.Exp(-j / (SampleRate * 0.02)));
        }
        return samples;
    }

    private static TempoTracker Feed(float[] samples, double expectedTempo)
    {
        var tracker = new TempoTracker(SampleRate, expectedTempo);
        // Feed in small buffers, as a microphone callback would.
        for (var i = 0; i < samples.Length; i += 256)
            tracker.Process(samples.AsSpan(i, Math.Min(256, samples.Length - i)));
        return tracker;
    }

    [Theory]
    [InlineData(120, 100)]
    [InlineData(90, 100)]
    [InlineData(72, 80)]
    [InlineData(150, 140)]
    public void EstimatesTheTempoOfABeat(double actual, double expected)
    {
        var tracker = Feed(Clicks(actual, 10), expected);

        Assert.NotNull(tracker.Tempo);
        Assert.InRange(tracker.Tempo!.Value, actual * 0.97, actual * 1.03);
        Assert.True(tracker.IsPlaying);
    }

    [Fact]
    public void ToleratesUnevenPlaying()
    {
        var tracker = Feed(Clicks(110, 12, swing: 0.03, seed: 7), 110);

        Assert.InRange(tracker.Tempo!.Value, 104, 116);
    }

    [Fact]
    public void PrefersTheWrittenTempoOverHalfOrDoubleTime()
    {
        // Clicks at 120 could be heard as 60 or 120; the written tempo decides.
        Assert.InRange(Feed(Clicks(120, 10), 120).Tempo!.Value, 115, 125);
        Assert.InRange(Feed(Clicks(120, 10), 62).Tempo!.Value, 57, 63);
    }

    [Fact]
    public void NeedsAFewSecondsBeforeEstimating() =>
        Assert.Null(Feed(Clicks(120, 2), 120).Tempo);

    [Fact]
    public void SilenceIsNotPlaying()
    {
        var tracker = Feed(new float[SampleRate * 5], 100);

        Assert.False(tracker.IsPlaying);
        Assert.Null(tracker.Tempo);
    }

    [Fact]
    public void NoticesWhenPlayingStops()
    {
        var samples = Clicks(100, 8).Concat(new float[SampleRate * 2]).ToArray();
        var tracker = Feed(samples, 100);

        Assert.False(tracker.IsPlaying);
        Assert.NotNull(tracker.Tempo); // keeps the last estimate for when playing resumes
    }
}
