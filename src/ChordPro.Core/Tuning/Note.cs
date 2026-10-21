namespace ChordPro.Core.Tuning;

/// <summary>The nearest equal-tempered note to a frequency, and how far off it is.</summary>
public readonly record struct Note(int Midi, double Cents, double Frequency)
{
    public const double StandardReference = 440;

    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Note name without octave, e.g. "F#".</summary>
    public string Name => Names[((Midi % 12) + 12) % 12];

    /// <summary>Scientific pitch octave: middle C is C4.</summary>
    public int Octave => Midi / 12 - 1;

    /// <summary>E.g. "E2", "A4".</summary>
    public override string ToString() => $"{Name}{Octave}";

    /// <param name="reference">The frequency of A4 (usually 440 Hz).</param>
    public static Note FromFrequency(double frequency, double reference = StandardReference)
    {
        var exact = 69 + 12 * Math.Log2(frequency / reference);
        var midi = (int)Math.Round(exact);
        return new Note(midi, (exact - midi) * 100, frequency);
    }

    /// <summary>The exact frequency of a MIDI note.</summary>
    public static double FrequencyOf(int midi, double reference = StandardReference) =>
        reference * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>The name and octave of a MIDI note, e.g. 40 → "E2".</summary>
    public static string NameOf(int midi) => new Note(midi, 0, 0).ToString();
}
