using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChordPro.Core;

/// <summary>Where a song in a gig stands.</summary>
public enum GigSongState { Upcoming, Played, Skipped }

/// <summary>One line of a gig: a song (or a set divider, as the setlist writes it) and how it went.</summary>
public sealed class GigEntry
{
    /// <summary>The song's path (library-relative, so the log outlives the app's folders), or a divider line.</summary>
    public string Song { get; set; } = "";
    /// <summary>Added during the gig, not in the setlist.</summary>
    public bool Requested { get; set; }
    public GigSongState State { get; set; }
    /// <summary>When the song was first shown.</summary>
    public DateTime? Started { get; set; }
    /// <summary>How long it was shown in all, up to the last time it was left.</summary>
    public double Seconds { get; set; }

    [JsonIgnore]
    public bool IsDivider => Setlist.IsDivider(Song);
}

/// <summary>A set's clock: how long it has run, against the setlist's plan.</summary>
/// <param name="Set">The set's divider name ("Set 2"), or null before the first divider.</param>
/// <param name="Planned">The set's songs' lengths as planned (requests not included); null when none has a length.</param>
/// <param name="Projected">When the set will end at this rate: time so far plus what's left of the songs still to play.</param>
public sealed record SetClock(string? Set, TimeSpan GigElapsed, TimeSpan SetElapsed, TimeSpan? Planned, TimeSpan? Projected)
{
    /// <summary>How far the set will run over its plan (negative when under), or null without a plan.</summary>
    public TimeSpan? Over => Projected - Planned;
}

/// <summary>
/// A setlist being played: its songs in the order they're actually played, with requests, skipped songs and
/// the time each was shown. The setlist file itself never changes. A song counts as played once it has been
/// shown for <see cref="PlayedAfter"/>, so one opened by mistake doesn't.
/// </summary>
public sealed class Gig
{
    public static readonly TimeSpan PlayedAfter = TimeSpan.FromSeconds(30);

    public string Name { get; set; } = "";
    /// <summary>The setlist played (library-relative).</summary>
    public string? Setlist { get; set; }
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }
    public List<GigEntry> Entries { get; set; } = [];
    /// <summary>The entry being shown, or −1.</summary>
    public int Current { get; set; } = -1;
    /// <summary>Since when the current entry has been shown; null while away from it (looking at the list).</summary>
    public DateTime? CurrentSince { get; set; }

    /// <summary>A gig of a setlist's entries (songs and dividers, as <see cref="Core.Setlist.Songs"/> holds them).</summary>
    public static Gig Start(string name, string? setlist, IEnumerable<string> entries, DateTime now) => new()
    {
        Name = name,
        Setlist = setlist,
        Started = now,
        Entries = [.. entries.Select(e => new GigEntry { Song = e })],
    };

    /// <summary>Shows an entry from now. Returns the song that just counted as played (the one left), if any.</summary>
    public string? Show(int index, DateTime now)
    {
        var played = Leave(now);
        Current = index;
        CurrentSince = now;
        Entries[index].Started ??= now;
        return played;
    }

    /// <summary>Stops timing the current song (it stays current). Returns it if it just counted as played.</summary>
    public string? Leave(DateTime now)
    {
        if (Current < 0 || CurrentSince is not { } since)
            return null;
        var entry = Entries[Current];
        entry.Seconds += Math.Max(0, (now - since).TotalSeconds);
        CurrentSince = null;
        return MarkPlayed(entry, TimeSpan.Zero);
    }

    /// <summary>Counts the current song as played once it has been shown long enough; returns it when it just did.</summary>
    public string? Update(DateTime now) =>
        Current >= 0 && CurrentSince is { } since ? MarkPlayed(Entries[Current], now - since) : null;

    private static string? MarkPlayed(GigEntry entry, TimeSpan showing)
    {
        if (entry.State == GigSongState.Played || TimeSpan.FromSeconds(entry.Seconds) + showing < PlayedAfter)
            return null;
        entry.State = GigSongState.Played;
        return entry.Song;
    }

    /// <summary>Skips a song still to come, or brings a skipped one back.</summary>
    public void ToggleSkip(int index)
    {
        var entry = Entries[index];
        entry.State = entry.State switch
        {
            GigSongState.Upcoming => GigSongState.Skipped,
            GigSongState.Skipped => GigSongState.Upcoming,
            var played => played,
        };
    }

    /// <summary>Adds a request: right after the current song (or before the first song still to play), or at the end.</summary>
    /// <returns>The new entry's index.</returns>
    public int Request(string song, bool atEnd)
    {
        var index = atEnd ? Entries.Count
            : Current >= 0 ? Current + 1
            : Math.Max(0, Entries.FindIndex(e => !e.IsDivider && e.State == GigSongState.Upcoming));
        Entries.Insert(index, new GigEntry { Song = song, Requested = true });
        if (Current >= index)
            Current++;
        return index;
    }

    /// <summary>Moves an entry up or down one place (past a divider moves it into the next set).</summary>
    public void Move(int index, int direction)
    {
        var to = index + direction;
        if (to < 0 || to >= Entries.Count)
            return;
        (Entries[index], Entries[to]) = (Entries[to], Entries[index]);
        if (Current == index)
            Current = to;
        else if (Current == to)
            Current = index;
    }

    /// <summary>
    /// The song a step away, passing dividers and skipped songs, with the last divider passed ("Set 2"). From −1,
    /// forward starts at the first song. Null past either end.
    /// </summary>
    /// <param name="playable">Songs to stop at (e.g. those in the library); null for all.</param>
    public (int Index, string? Divider)? Step(int index, int direction, Func<string, bool>? playable = null)
    {
        string? divider = null;
        for (var i = index < 0 ? (direction > 0 ? 0 : Entries.Count - 1) : index + direction; i >= 0 && i < Entries.Count; i += direction)
        {
            var entry = Entries[i];
            if (entry.IsDivider)
                divider = Core.Setlist.DividerName(entry.Song);
            else if (entry.State != GigSongState.Skipped && playable?.Invoke(entry.Song) != false)
                return (i, divider);
        }
        return null;
    }

    /// <summary>The clock for the set of the current song (or the first set, before any song).</summary>
    /// <param name="length">A song's length (its <c>{duration}</c>), or null.</param>
    public SetClock Clock(DateTime now, Func<string, TimeSpan?> length)
    {
        if (Entries.Count == 0)
            return new SetClock(null, now - Started, TimeSpan.Zero, null, null);
        var at = Math.Max(0, Current);
        var first = Entries.FindLastIndex(at, e => e.IsDivider) is var d and >= 0 ? d + 1 : 0;
        var set = first > 0 ? Core.Setlist.DividerName(Entries[first - 1].Song) : null;
        var end = Entries.FindIndex(first, e => e.IsDivider) is var n and >= 0 ? n : Entries.Count;
        var songs = Entries.Skip(first).Take(end - first).ToList();

        var started = songs.Select(e => e.Started).Where(s => s is not null).Min();
        var setElapsed = started is { } s ? now - s : TimeSpan.Zero;
        var lengths = songs.Where(e => !e.Requested).Select(e => length(e.Song)).Where(l => l is not null).ToList();
        TimeSpan? planned = lengths.Count == 0 ? null : TimeSpan.FromSeconds(lengths.Sum(l => l!.Value.TotalSeconds));

        TimeSpan? projected = null;
        if (planned is not null)
        {
            // What's left: the rest of the song being played, and every song still to come in the set.
            var left = TimeSpan.Zero;
            for (var i = first; i < end; i++)
            {
                var entry = Entries[i];
                if (i == Current && length(entry.Song) is { } current)
                    left += Max(TimeSpan.Zero, current - Shown(entry, now));
                else if (i > Current && entry.State == GigSongState.Upcoming && length(entry.Song) is { } upcoming)
                    left += upcoming;
            }
            projected = setElapsed + left;
        }
        return new SetClock(set, now - Started, setElapsed, planned, projected);
    }

    private TimeSpan Shown(GigEntry entry, DateTime now) =>
        TimeSpan.FromSeconds(entry.Seconds) + (Entries.IndexOf(entry) == Current && CurrentSince is { } since ? now - since : TimeSpan.Zero);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>The songs played, in the order they were first shown.</summary>
    public IEnumerable<GigEntry> Played =>
        Entries.Where(e => e.State == GigSongState.Played).OrderBy(e => e.Started);

    /// <summary>Ends the gig; returns the song that just counted as played, if any.</summary>
    public string? End(DateTime now)
    {
        var played = Leave(now);
        Ended = now;
        return played;
    }

    public static Gig? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), GigJson.Default.Gig) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves the gig (written whole and then moved, so a crash never leaves half a file).</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, GigJson.Default.Gig));
        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Gig))]
internal sealed partial class GigJson : JsonSerializerContext;
