using System.Text.RegularExpressions;
using ChordPro.Core.Scrolling;
using Whisper.net;

namespace ChordPro.Viewer;

/// <summary>
/// Speech recognition with Whisper, on this computer: short windows for following live, and whole
/// recordings with word times for training. Not thread-safe; use one call at a time.
/// </summary>
public sealed partial class Transcriber : IDisposable
{
    // Whisper labels sounds rather than words with brackets: "[BLANK_AUDIO]", "(music)", "*laughs*".
    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex SoundLabelRegex();

    public const int SampleRate = 16000; // what Whisper expects

    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _window;
    private static readonly int Threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public Transcriber(string modelPath)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        _window = WindowBuilder().Build();
    }

    private WhisperProcessorBuilder WindowBuilder() => _factory.CreateBuilder()
        .WithLanguage("en")
        .WithThreads(Threads)
        .WithNoContext()       // each window stands alone; earlier text would be repeated
        .WithSingleSegment();

    /// <summary>
    /// The words in a few seconds of audio, without sound labels; empty when there are none.
    /// </summary>
    /// <param name="prompt">The lyrics sung just before, as a hint for unusual words; Whisper takes it
    /// as what was said before this audio. A processor is built for each prompt, which is cheap.</param>
    public async Task<string> TranscribeAsync(float[] window, string? prompt = null, CancellationToken cancel = default)
    {
        var text = "";
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await foreach (var segment in _window.ProcessAsync(window, cancel))
                text += segment.Text;
        }
        else
        {
            await using var processor = WindowBuilder().WithPrompt(prompt).Build();
            await foreach (var segment in processor.ProcessAsync(window, cancel))
                text += segment.Text;
        }
        text = SoundLabelRegex().Replace(text, " ").Trim();
        return text.Any(char.IsLetter) ? text : "";
    }

    /// <summary>Every word in a whole recording, with when it started.</summary>
    public async Task<List<TimedWord>> TranscribeWordsAsync(float[] audio, CancellationToken cancel = default)
    {
        await using var processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Threads)
            .WithTokenTimestamps()
            .Build();
        var words = new List<TimedWord>();
        var inLabel = false;
        await foreach (var segment in processor.ProcessAsync(audio, cancel))
        {
            foreach (var token in segment.Tokens)
            {
                var text = token.Text ?? "";
                // Special tokens ("[_BEG_]", "[_TT_236]", "<|endoftext|>") aren't words.
                if (text.StartsWith("[_") || text.StartsWith("<|"))
                    continue;
                // A sound label may span tokens: " [", "BL", "ANK", "_AUDIO", "]".
                if (text.TrimStart().StartsWith('[') || text.TrimStart().StartsWith('('))
                    inLabel = true;
                if (inLabel)
                {
                    inLabel = !(text.Contains(']') || text.Contains(')'));
                    continue;
                }
                // A token starting with a space starts a word; the rest continue it (" w" + "retch").
                if (words.Count == 0 || text.StartsWith(' '))
                    words.Add(new TimedWord(text.Trim(), token.Start / 100.0)); // token times are in centiseconds
                else
                    words[^1] = words[^1] with { Text = words[^1].Text + text };
            }
        }
        return words;
    }

    /// <summary>The level (RMS, 0–1) of some audio.</summary>
    public static float Level(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return 0;
        var sum = 0.0;
        foreach (var s in samples)
            sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    public void Dispose()
    {
        _window.Dispose();
        _factory.Dispose();
    }
}
