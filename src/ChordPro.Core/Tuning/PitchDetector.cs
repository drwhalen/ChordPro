namespace ChordPro.Core.Tuning;

/// <summary>
/// Finds the fundamental frequency of a single note using the YIN algorithm (de Cheveigné and
/// Kawahara, 2002), which copes well with the strong overtones of plucked strings.
/// </summary>
public sealed class PitchDetector
{
    public const double MinFrequency = 60;    // below a guitar's low E (82 Hz), allowing for drop tunings
    public const double MaxFrequency = 1200;  // well above a ukulele's A string (440 Hz)

    private const double Threshold = 0.15;     // YIN's absolute threshold on the normalised difference
    private const double MinLevel = 0.002;     // RMS below this is treated as silence

    private readonly int _sampleRate;
    private readonly int _window;
    private readonly int _minLag;
    private readonly int _maxLag;
    private readonly double[] _difference;

    public PitchDetector(int sampleRate)
    {
        _sampleRate = sampleRate;
        _minLag = Math.Max(2, (int)(sampleRate / MaxFrequency));
        _maxLag = (int)Math.Ceiling(sampleRate / MinFrequency);
        _window = _maxLag; // long enough to hold one full period of the lowest note
        _difference = new double[_maxLag + 2];
    }

    /// <summary>How many samples <see cref="Detect"/> needs.</summary>
    public int RequiredSamples => _window + _maxLag + 2;

    /// <summary>
    /// The frequency in Hz of the note in <paramref name="samples"/> (its most recent
    /// <see cref="RequiredSamples"/> are used), and how clearly it was heard (0–1); null when
    /// it is too quiet or not clearly pitched.
    /// </summary>
    public (double Frequency, double Clarity)? Detect(ReadOnlySpan<float> samples)
    {
        if (samples.Length < RequiredSamples)
            return null;
        samples = samples[^RequiredSamples..];

        var sumSquares = 0.0;
        foreach (var s in samples)
            sumSquares += s * s;
        if (Math.Sqrt(sumSquares / samples.Length) < MinLevel)
            return null;

        // Difference function d(τ) = Σ (x[j] − x[j+τ])².
        for (var lag = 1; lag <= _maxLag + 1; lag++)
        {
            var sum = 0.0;
            for (var j = 0; j < _window; j++)
            {
                var delta = samples[j] - samples[j + lag];
                sum += delta * delta;
            }
            _difference[lag] = sum;
        }

        // Cumulative mean normalised difference d'(τ) = d(τ)·τ / Σ₁..τ d.
        _difference[0] = 1;
        var running = 0.0;
        for (var lag = 1; lag <= _maxLag + 1; lag++)
        {
            running += _difference[lag];
            _difference[lag] = running > 0 ? _difference[lag] * lag / running : 1;
        }

        // The first dip below the threshold, followed down to its minimum, is the period. Taking
        // the first (shortest) one avoids reporting a note an octave too low.
        var best = -1;
        for (var lag = _minLag; lag <= _maxLag; lag++)
        {
            if (_difference[lag] < Threshold)
            {
                while (lag + 1 <= _maxLag && _difference[lag + 1] < _difference[lag])
                    lag++;
                best = lag;
                break;
            }
        }
        if (best < 0)
            return null;

        // Refine between samples with a parabola through the minimum and its neighbours.
        var (left, centre, right) = (_difference[best - 1], _difference[best], _difference[best + 1]);
        var curvature = left - 2 * centre + right;
        var offset = curvature > 0 ? Math.Clamp(0.5 * (left - right) / curvature, -0.5, 0.5) : 0;

        return (_sampleRate / (best + offset), Math.Clamp(1 - centre, 0, 1));
    }
}
