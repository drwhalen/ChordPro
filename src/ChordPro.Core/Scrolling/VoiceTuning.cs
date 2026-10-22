namespace ChordPro.Core.Scrolling;

/// <summary>
/// The settings Follow voice listens with. The defaults suit most voices and microphones; training
/// (see <see cref="VoiceTraining"/>) finds the ones that follow a particular singer best.
/// </summary>
/// <param name="MinLevel">Below this level (RMS, 0–1) a window is taken as silence and not transcribed;
/// Whisper only invents words in silence. A quiet microphone needs it lower, a noisy room higher.</param>
/// <param name="WindowSeconds">How much of the latest audio is transcribed each time: longer windows
/// hold more words (better recognition), shorter ones react sooner.</param>
/// <param name="IntervalSeconds">How often a window is transcribed.</param>
/// <param name="UsePrompt">Whether recognition is told the lyrics just sung, which can help it with
/// unusual words, or lead it astray.</param>
/// <param name="MinWords">How many words must match the lyrics before the place moves.</param>
public sealed record VoiceTuning(
    double MinLevel = VoiceTuning.DefaultMinLevel,
    double WindowSeconds = 4,
    double IntervalSeconds = 1,
    bool UsePrompt = false,
    int MinWords = VoiceTuning.DefaultMinWords)
{
    public const double DefaultMinLevel = 0.003;   // about -50 dBFS
    public const int DefaultMinWords = 3;

    public static VoiceTuning Default { get; } = new();

    /// <summary>A short description for people, e.g. "4 s windows every 1 s, 3 words, gate -50 dB".</summary>
    public override string ToString() =>
        $"{WindowSeconds:0.#} s windows every {IntervalSeconds:0.#} s, {MinWords} words to move, "
        + $"gate {20 * Math.Log10(MinLevel):0} dB{(UsePrompt ? ", lyric hints" : "")}";
}
