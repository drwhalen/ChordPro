using ChordPro.Core.Tuning;

namespace ChordPro.Core.Tests;

public class TunerTests
{
    private const int SampleRate = 48000;

    // A plucked-string-like tone: a fundamental plus decaying overtones, optionally with noise.
    private static float[] Tone(double frequency, double seconds = 0.15, double[]? harmonics = null, double noise = 0, int seed = 3)
    {
        harmonics ??= [1.0];
        var random = new Random(seed);
        var samples = new float[(int)(seconds * SampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            var value = 0.0;
            for (var h = 0; h < harmonics.Length; h++)
                value += harmonics[h] * Math.Sin(2 * Math.PI * frequency * (h + 1) * t + h);
            samples[i] = (float)(0.3 * value / harmonics.Sum() + noise * (random.NextDouble() - 0.5));
        }
        return samples;
    }

    private static double Detect(float[] samples) => new PitchDetector(SampleRate).Detect(samples)!.Value.Frequency;

    [Theory]
    [InlineData(82.41)]   // guitar low E
    [InlineData(110.00)]  // A
    [InlineData(146.83)]  // D
    [InlineData(196.00)]  // G
    [InlineData(246.94)]  // B
    [InlineData(329.63)]  // high E
    [InlineData(392.00)]  // ukulele G4
    [InlineData(440.00)]  // ukulele A4
    [InlineData(73.42)]   // drop D
    public void DetectsPureTonesWithinOneCent(double frequency)
    {
        var detected = Detect(Tone(frequency));
        Assert.InRange(1200 * Math.Log2(detected / frequency), -1, 1);
    }

    [Theory]
    [InlineData(82.41)]
    [InlineData(110.00)]
    [InlineData(196.00)]
    public void DetectsTheFundamentalOfAToneWithStrongOvertones(double frequency)
    {
        // Overtones louder than the fundamental, as from a bright guitar string, must not cause octave errors.
        var detected = Detect(Tone(frequency, harmonics: [0.6, 1.0, 0.8, 0.5, 0.3, 0.2], noise: 0.02));
        Assert.InRange(1200 * Math.Log2(detected / frequency), -3, 3);
    }

    [Fact]
    public void SilenceHasNoPitch() =>
        Assert.Null(new PitchDetector(SampleRate).Detect(new float[SampleRate / 5]));

    [Fact]
    public void NoiseHasNoPitch()
    {
        var random = new Random(9);
        var noise = Enumerable.Range(0, SampleRate / 5).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray();
        Assert.Null(new PitchDetector(SampleRate).Detect(noise));
    }

    [Fact]
    public void NeedsEnoughSamples()
    {
        var detector = new PitchDetector(SampleRate);
        Assert.Null(detector.Detect(Tone(110, seconds: 0.01)));
        Assert.True(detector.RequiredSamples < SampleRate / 10); // under 100 ms of audio
    }

    [Theory]
    [InlineData(440, "A4", 0)]
    [InlineData(82.41, "E2", 0)]
    [InlineData(445, "A4", 19.56)]
    [InlineData(435, "A4", -19.78)]
    [InlineData(261.63, "C4", 0)]
    [InlineData(277.18, "C#4", 0)]
    public void NamesTheNearestNoteAndCents(double frequency, string name, double cents)
    {
        var note = Note.FromFrequency(frequency);
        Assert.Equal(name, note.ToString());
        Assert.Equal(cents, note.Cents, precision: 0);
    }

    [Fact]
    public void UsesTheReferencePitch()
    {
        // 442 Hz is in tune when the reference is A4 = 442.
        Assert.Equal(0, Note.FromFrequency(442, reference: 442).Cents, precision: 3);
        Assert.Equal(442, Note.FrequencyOf(69, reference: 442), precision: 6);
    }

    [Fact]
    public void NamesMidiNotes()
    {
        Assert.Equal("E2", Note.NameOf(40));
        Assert.Equal("G4", Note.NameOf(67));
        Assert.Equal(82.41, Note.FrequencyOf(40), precision: 2);
    }
}
