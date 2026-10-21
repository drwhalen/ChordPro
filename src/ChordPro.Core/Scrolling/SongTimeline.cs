using System.Globalization;

namespace ChordPro.Core.Scrolling;

/// <summary>When one line of the song starts and how long it lasts.</summary>
public sealed record LineTiming(Section Section, int LineIndex, SongLine Line, double Start, double Length)
{
    public double End => Start + Length;
}

/// <summary>
/// A musical timeline: every line placed in bars, from the tempo, time signature and bar counts in
/// the song. Lines without a bar count get a default: one bar per chord, two bars for a lyric line
/// without chords, four for a block of tab or grid, and none for comments and blank lines.
/// </summary>
public sealed class MusicalTimeline
{
    public const double DefaultTempo = 100;
    private const int DefaultBeatsPerBar = 4;
    private const double LyricLineBars = 2;
    private const double PreformattedBlockBars = 4;

    private MusicalTimeline(double tempo, int beatsPerBar, bool tempoFromSong, List<LineTiming> lines)
    {
        Tempo = tempo;
        BeatsPerBar = beatsPerBar;
        TempoFromSong = tempoFromSong;
        Lines = lines;
    }

    /// <summary>Beats per minute the timeline was built for.</summary>
    public double Tempo { get; }
    public int BeatsPerBar { get; }
    /// <summary>False when the song has no usable <c>{tempo}</c> and <see cref="DefaultTempo"/> was assumed.</summary>
    public bool TempoFromSong { get; }
    /// <summary>Every line in song order, with <see cref="LineTiming.Start"/> and length in bars.</summary>
    public IReadOnlyList<LineTiming> Lines { get; }

    public double TotalBars => Lines.Count == 0 ? 0 : Lines[^1].End;
    public double SecondsPerBar => BeatsPerBar * 60 / Tempo;
    public double TotalSeconds => TotalBars * SecondsPerBar;

    /// <summary>The line being played at <paramref name="bar"/> (zero-based), skipping zero-length lines.</summary>
    public LineTiming? LineAt(double bar) =>
        Lines.LastOrDefault(l => l.Length > 0 && l.Start <= bar) ?? Lines.FirstOrDefault(l => l.Length > 0);

    public static MusicalTimeline Build(Song song)
    {
        var tempoFromSong = double.TryParse(song.Tempo, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempo) && tempo is >= 20 and <= 400;
        var beatsPerBar = ParseBeatsPerBar(song.Time);

        var lines = new List<LineTiming>();
        var bar = 0.0;
        foreach (var section in song.Sections)
        {
            var lengths = SectionLengths(section);
            for (var i = 0; i < section.Lines.Count; i++)
            {
                lines.Add(new LineTiming(section, i, section.Lines[i], bar, lengths[i]));
                bar += lengths[i];
            }
        }

        return new MusicalTimeline(tempoFromSong ? tempo : DefaultTempo, beatsPerBar, tempoFromSong, lines);
    }

    /// <summary>The top number of a time signature such as "3/4" or "6/8"; 4 if missing.</summary>
    public static int ParseBeatsPerBar(string? time)
    {
        var top = time?.Split('/')[0].Trim();
        return int.TryParse(top, NumberStyles.None, CultureInfo.InvariantCulture, out var beats) && beats is >= 1 and <= 32
            ? beats
            : DefaultBeatsPerBar;
    }

    private static double[] SectionLengths(Section section)
    {
        var lines = section.Lines;
        var lengths = new double[lines.Count];
        var defaults = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Bars is { } explicitBars)
                lengths[i] = explicitBars;
            else
                defaults[i] = DefaultBars(lines, i);
        }

        // A section length (bars="16") is shared out over the lines that don't give their own.
        if (section.Bars is { } sectionBars)
        {
            var remaining = Math.Max(0, sectionBars - lengths.Sum());
            var weight = defaults.Sum();
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Bars is null)
                    defaults[i] = weight > 0 ? remaining * defaults[i] / weight : 0;
            }
            if (weight == 0 && remaining > 0 && lines.Count > 0)
                defaults[FirstContentLine(lines)] = remaining;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Bars is null)
                lengths[i] = defaults[i];
        }
        return lengths;
    }

    private static double DefaultBars(List<SongLine> lines, int index) => lines[index] switch
    {
        LyricLine { HasChords: true } line => Math.Max(1, line.Segments.Count(s => !string.IsNullOrEmpty(s.Chord))),
        LyricLine => LyricLineBars,
        // A tab or grid block is read as a whole: give its first line the time, the rest none.
        PreformattedLine => index == 0 || lines[index - 1] is not PreformattedLine ? PreformattedBlockBars : 0,
        _ => 0,
    };

    private static int FirstContentLine(List<SongLine> lines)
    {
        var index = lines.FindIndex(l => l is LyricLine or PreformattedLine);
        return index >= 0 ? index : 0;
    }
}

/// <summary>Timestamp markers (<c>{x_time: 1:05}</c>) in song order, each later than the one before.</summary>
public static class TimeMarkers
{
    public static IReadOnlyList<(Section Section, int LineIndex, TimeSpan Time)> Find(Song song)
    {
        var markers = new List<(Section, int, TimeSpan)>();
        var last = TimeSpan.MinValue;
        foreach (var section in song.Sections)
        {
            for (var i = 0; i < section.Lines.Count; i++)
            {
                // A recalled {chorus} repeats the original's markers; keep only times that move forward.
                if (section.Lines[i].Time is { } time && time > last)
                {
                    markers.Add((section, i, time));
                    last = time;
                }
            }
        }
        return markers;
    }
}
