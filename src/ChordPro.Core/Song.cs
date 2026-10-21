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

    /// <summary>All metadata values, keyed by canonical directive name (e.g. "title", "artist").</summary>
    public Dictionary<string, List<string>> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<Section> Sections { get; } = [];

    /// <summary>Guitar fingerings declared in the file with <c>{define}</c>, keyed by chord name.</summary>
    public Dictionary<string, ChordShape> ChordDefinitions { get; } = [];

    public string? GetMeta(string name) =>
        Metadata.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

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

public sealed class Section(SectionKind kind, string? label = null)
{
    public SectionKind Kind { get; } = kind;
    public string? Label { get; } = label;
    public List<SongLine> Lines { get; } = [];
}

public abstract record SongLine;

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

/// <summary>A line that must be shown verbatim in a monospaced font (tabs, grids).</summary>
public sealed record PreformattedLine(string Text) : SongLine;

public sealed record BlankLine : SongLine;
