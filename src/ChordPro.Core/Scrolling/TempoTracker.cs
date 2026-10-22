namespace ChordPro.Core.Scrolling;

/// <summary>
/// Estimates the tempo of live playing from microphone samples, and whether anything is being
/// played at all. Experimental: it listens for the rhythm of strums and notes (onsets) and finds
/// the beat period that repeats most strongly, preferring tempos near the song's written tempo so
/// it doesn't lock onto half or double time. It does not follow the melody or chords.
/// </summary>
public sealed class TempoTracker
{
    private const int FramesPerSecond = 100;
    private const int HistoryFrames = 8 * FramesPerSecond;     // analyze the last 8 seconds
    private const int MinimumFrames = 4 * FramesPerSecond;     // need 4 seconds before estimating
    private const int AnalysisInterval = FramesPerSecond / 2;  // re-estimate twice a second
    private const double MinTempo = 50, MaxTempo = 200;
    private const double PriorWidthOctaves = 0.3;
    private const double MinConfidence = 0.05;
    private const double Smoothing = 0.3;
    private const double SilenceSeconds = 1.5;
    private const double MinLevel = 1e-6;                      // about -60 dBFS

    private readonly int _hopSize;
    private readonly double[] _onsets = new double[HistoryFrames];
    private int _frameCount;
    private double _hopEnergy;
    private int _hopFill;
    private double _previousLogEnergy;
    private double _recentEnergy;
    private double _noiseFloor = double.MaxValue;
    private int _lastSoundFrame = int.MinValue / 2;

    public TempoTracker(int sampleRate, double expectedTempo)
    {
        _hopSize = Math.Max(1, sampleRate / FramesPerSecond);
        ExpectedTempo = expectedTempo;
    }

    /// <summary>The song's written tempo, which guides the estimate.</summary>
    public double ExpectedTempo { get; set; }

    /// <summary>The smoothed tempo estimate in BPM, or null until there is enough playing to tell.</summary>
    public double? Tempo { get; private set; }

    /// <summary>How clearly the last estimate stood out (0–1).</summary>
    public double Confidence { get; private set; }

    /// <summary>True while sound has been heard within the last <see cref="SilenceSeconds"/>.</summary>
    public bool IsPlaying => _frameCount - _lastSoundFrame <= SilenceSeconds * FramesPerSecond;

    public void Process(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _hopEnergy += sample * sample;
            if (++_hopFill == _hopSize)
            {
                AddFrame(_hopEnergy / _hopSize);
                _hopEnergy = 0;
                _hopFill = 0;
            }
        }
    }

    private void AddFrame(double energy)
    {
        // Onset strength: how sharply the loudness rises (on a log scale, so quiet and loud
        // playing count alike).
        var logEnergy = Math.Log10(energy + 1e-10);
        _onsets[_frameCount % HistoryFrames] = Math.Max(0, logEnergy - _previousLogEnergy);
        _previousLogEnergy = logEnergy;

        // Presence: compare a short-term level with a slowly rising noise floor.
        _recentEnergy = _recentEnergy * 0.9 + energy * 0.1;
        _noiseFloor = Math.Min(_noiseFloor * 1.001, Math.Max(_recentEnergy, 1e-12));
        if (_recentEnergy > Math.Max(_noiseFloor * 10, MinLevel))
            _lastSoundFrame = _frameCount;

        _frameCount++;
        if (_frameCount >= MinimumFrames && _frameCount % AnalysisInterval == 0 && IsPlaying)
            Estimate();
    }

    private void Estimate()
    {
        var count = Math.Min(_frameCount, HistoryFrames);
        var signal = new double[count];
        for (var i = 0; i < count; i++)
            signal[i] = _onsets[(_frameCount - count + i) % HistoryFrames];
        var mean = signal.Average();
        for (var i = 0; i < count; i++)
            signal[i] -= mean;

        var zeroLag = Autocorrelation(signal, 0);
        if (zeroLag <= 0)
            return;

        var minLag = (int)Math.Floor(60.0 * FramesPerSecond / MaxTempo);
        var maxLag = (int)Math.Ceiling(60.0 * FramesPerSecond / MinTempo);
        var correlations = new double[maxLag + 2];
        for (var lag = minLag - 1; lag <= maxLag + 1; lag++)
            correlations[lag] = Autocorrelation(signal, lag) / zeroLag;

        var bestLag = -1;
        var bestScore = double.MinValue;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var tempo = 60.0 * FramesPerSecond / lag;
            var octaves = Math.Log2(tempo / ExpectedTempo) / PriorWidthOctaves;
            var score = correlations[lag] * Math.Exp(-0.5 * octaves * octaves);
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        Confidence = Math.Max(0, correlations[bestLag]);
        if (Confidence < MinConfidence)
            return;

        // Refine between frames with a parabola through the peak and its neighbors.
        var (left, center, right) = (correlations[bestLag - 1], correlations[bestLag], correlations[bestLag + 1]);
        var curvature = left - 2 * center + right;
        var offset = curvature < 0 ? Math.Clamp(0.5 * (left - right) / curvature, -0.5, 0.5) : 0;
        var estimate = 60.0 * FramesPerSecond / (bestLag + offset);

        Tempo = Tempo is { } previous ? previous + Smoothing * (estimate - previous) : estimate;
    }

    private static double Autocorrelation(double[] signal, int lag)
    {
        var sum = 0.0;
        for (var i = lag; i < signal.Length; i++)
            sum += signal[i] * signal[i - lag];
        return sum / (signal.Length - lag);
    }
}
