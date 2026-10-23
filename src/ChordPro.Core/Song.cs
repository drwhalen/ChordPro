using ChordPro.Core.Chords;

namespace ChordPro.Core;

/// <summary>A parsed ChordPro song: metadata plus an ordered list of sections.</summary>
public sealed class Song
{
    public string? Title => GetMeta("title");
    public string? Subtitle => GetMeta("subtitle");
    public string? Artist => GetMeta("artist");
    public string? Key => GetMeta("key");
    public string? Capo => GetMeta("capo");
    public string? Tempo => GetMeta("tempo");
    public string? Time => GetMeta("time");
    public string? Duration => GetMeta("duration");
    /// <summary>The tuning the song is played in (<c>{x_tuning: Drop D}</c>); see <see cref="Chords.SongTuning"/>.</summary>
    public string? Tuning => GetMeta("tuning");

    /// <summary>The capo fret from <c>{capo}</c>, or 0 when there is none or it isn't a number.</summary>
    public int CapoFret => int.TryParse(Capo, out var capo) ? Math.Clamp(capo, 0, 11) : 0;

    /// <summary>All metadata values, keyed by canonical directive name (e.g. "title", "artist").</summary>
    public Dictionary<string, List<string>> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<Section> Sections { get; } = [];

    /// <summary>Guitar fingerings declared in the file with <c>{define}</c>, keyed by chord name.</summary>
    public Dictionary<string, ChordShape> ChordDefinitions { get; } = [];

    /// <summary>
    /// Fingerings for one instrument, from <c>{define-ukulele: C …}</c>: keyed by the directive's
    /// selector (<see cref="Chords.Instrument.Selector"/>), then by chord. ChordPro apps use a
    /// selected directive only for that instrument.
    /// </summary>
    public Dictionary<string, Dictionary<string, ChordShape>> InstrumentDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every chord played, in order and with repeats (tab and grid sections excluded).</summary>
    public IEnumerable<string> ChordUsages() =>
        Sections
            .Where(s => s.Kind is not (SectionKind.Tab or SectionKind.Grid))
            .SelectMany(s => s.Lines.OfType<LyricLine>())
            .SelectMany(l => l.Segments)
            .Select(s => s.Chord)
            .OfType<string>()
            .Where(c => c.Length > 0);

    public string? GetMeta(string name) =>
        Metadata.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>Every value of a directive that may repeat, such as <c>{book}</c>.</summary>
    public IReadOnlyList<string> GetMetaValues(string name) =>
        Metadata.TryGetValue(name, out var values) ? values : [];

    internal void AddMeta(string name, string value)
    {
        if (!Metadata.TryGetValue(name, out var values))
            Metadata[name] = values = [];
        values.Add(value);
    }
}

public enum SectionKind
{
    /// <summary>Content that is not inside an explicit section.</summary>
    None,
    Verse,
    Chorus,
    Bridge,
    Tab,
    Grid,
    /// <summary>Any other <c>start_of_xxx</c> section.</summary>
    Other,
}

public sealed class Section(SectionKind kind, string? label = null, double? bars = null)
{
    public SectionKind Kind { get; } = kind;
    public string? Label { get; } = label;
    /// <summary>Length in bars, from <c>bars="16"</c> on the section directive.</summary>
    public double? Bars { get; } = bars;
    public List<SongLine> Lines { get; } = [];
}

public abstract record SongLine
{
    /// <summary>Length in bars, from a <c>{x_bars: 4}</c> directive before the line.</summary>
    public double? Bars { get; init; }

    /// <summary>When the line should be reached, from a <c>{x_time: 1:05}</c> directive before the line.</summary>
    public TimeSpan? Time { get; init; }
}

/// <summary>A lyric line with chords positioned inline, e.g. <c>[G]Amazing [C]grace</c>.</summary>
public sealed record LyricLine(IReadOnlyList<ChordSegment> Segments) : SongLine
{
    public bool HasChords => Segments.Any(s => s.Chord is not null);

    public bool Equals(LyricLine? other) => other is not null && Segments.SequenceEqual(other.Segments);

    public override int GetHashCode() => Segments.Count;
}

/// <summary>A chord (optional) and the lyric text that follows it, up to the next chord.</summary>
public sealed record ChordSegment(string? Chord, string Lyric);

public enum CommentStyle { Normal, Italic, Box }

public sealed record CommentLine(string Text, CommentStyle Style) : SongLine;

/// <summary>A change of key part way through a song: a second <c>{key}</c> directive.</summary>
public sealed record KeyChangeLine(string Key) : SongLine;

/// <summary>A line that must be shown verbatim in a monospaced font (tabs, grids).</summary>
public sealed record PreformattedLine(string Text) : SongLine;

public sealed record BlankLine : SongLine;
