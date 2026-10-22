using System.Diagnostics;
using System.Text;

namespace ChordPro.Viewer;

/// <summary>
/// Reads and writes recordings as 16 kHz mono samples, the form speech recognition takes. WAV files
/// are read directly; on macOS other formats (a QuickTime or Voice Memos .m4a, .mp3, .aiff) are
/// converted with the system's afconvert first.
/// </summary>
public static class AudioFile
{
    /// <summary>The file types that can be opened here.</summary>
    public static IReadOnlyList<string> Extensions { get; } = OperatingSystem.IsMacOS()
        ? ["wav", "m4a", "mp3", "aif", "aiff", "caf", "mp4", "mov"]
        : ["wav"];

    /// <summary>Reads a recording, mixed to mono and resampled to <paramref name="rate"/>.</summary>
    /// <exception cref="InvalidDataException">The file isn't audio this can read.</exception>
    public static float[] Load(string path, int rate = Transcriber.SampleRate)
    {
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var (samples, fileRate) = ReadWav(File.ReadAllBytes(path));
            return Resample(samples, fileRate, rate);
        }
        if (!OperatingSystem.IsMacOS())
            throw new InvalidDataException("Only WAV recordings can be opened on this system.");

        var wav = Path.Combine(Path.GetTempPath(), $"chordpro-{Guid.NewGuid():N}.wav");
        try
        {
            var convert = Process.Start(new ProcessStartInfo("/usr/bin/afconvert",
                ["-f", "WAVE", "-d", $"LEF32@{rate}", "-c", "1", path, wav])
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            }) ?? throw new InvalidDataException("afconvert couldn't be started.");
            var error = convert.StandardError.ReadToEnd();
            convert.WaitForExit();
            if (convert.ExitCode != 0 || !File.Exists(wav))
                throw new InvalidDataException($"The recording couldn't be converted ({error.Trim()}).");
            var (samples, fileRate) = ReadWav(File.ReadAllBytes(wav));
            return Resample(samples, fileRate, rate);
        }
        finally
        {
            File.Delete(wav);
        }
    }

    /// <summary>Saves samples as a 32-bit float mono WAV file.</summary>
    public static void SaveWav(string path, float[] samples, int rate = Transcriber.SampleRate)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var bytes = samples.Length * 4;
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)3);        // IEEE float
        writer.Write((short)1);        // mono
        writer.Write(rate);
        writer.Write(rate * 4);
        writer.Write((short)4);
        writer.Write((short)32);
        writer.Write("data"u8);
        writer.Write(bytes);
        foreach (var s in samples)
            writer.Write(s);
    }

    /// <summary>The samples of a WAV file, mixed to mono, and its sample rate.</summary>
    public static (float[] Samples, int Rate) ReadWav(byte[] wav)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("This isn't a WAV file.");
        int format = 0, channels = 0, rate = 0, bits = 0;
        for (var i = 12; i + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, i, 4);
            var size = BitConverter.ToInt32(wav, i + 4);
            var body = i + 8;
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(wav, body);
                channels = BitConverter.ToUInt16(wav, body + 2);
                rate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToUInt16(wav, body + 14);
                if (format == 0xFFFE && size >= 26)
                    format = BitConverter.ToUInt16(wav, body + 24); // extensible: the real format leads the subformat
            }
            else if (id == "data" && channels > 0)
                return (Decode(wav.AsSpan(body, Math.Min(size, wav.Length - body)), format, channels, bits), rate);
            i = body + size + (size & 1);
        }
        throw new InvalidDataException("The WAV file has no audio this can read.");
    }

    private static float[] Decode(ReadOnlySpan<byte> data, int format, int channels, int bits)
    {
        if ((format, bits) is not ((3, 32) or (1, 8) or (1, 16) or (1, 24) or (1, 32)))
            throw new InvalidDataException($"WAV files with {bits}-bit {(format == 3 ? "float" : "format " + format)} samples can't be read.");
        var frames = data.Length / (bits / 8) / channels;
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += Sample(data, f * channels + c, format, bits);
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static float Sample(ReadOnlySpan<byte> data, int i, int format, int bits) => (format, bits) switch
    {
        (3, _) => BitConverter.ToSingle(data.Slice(i * 4, 4)),
        (_, 16) => BitConverter.ToInt16(data.Slice(i * 2, 2)) / 32768f,
        (_, 24) => ((data[i * 3] << 8 | data[i * 3 + 1] << 16 | data[i * 3 + 2] << 24) >> 8) / 8388608f,
        (_, 32) => BitConverter.ToInt32(data.Slice(i * 4, 4)) / 2147483648f,
        _ => (data[i] - 128) / 128f,
    };

    /// <summary>
    /// Resamples by linear interpolation, after averaging over each output step when going down, so
    /// higher frequencies don't fold down (speech sits well below 8 kHz).
    /// </summary>
    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate || input.Length == 0)
            return input;
        var step = fromRate / (double)toRate;
        var span = Math.Max(1, (int)step);
        var output = new float[(int)(input.Length / step)];
        for (var o = 0; o < output.Length; o++)
        {
            var at = o * step;
            var i = (int)at;
            if (span > 1)
            {
                var sum = 0f;
                var n = 0;
                for (var k = Math.Max(0, i - span + 1); k <= i; k++, n++)
                    sum += input[k];
                output[o] = sum / n;
            }
            else
            {
                var next = Math.Min(i + 1, input.Length - 1);
                output[o] = (float)(input[i] + (input[next] - input[i]) * (at - i));
            }
        }
        return output;
    }
}
