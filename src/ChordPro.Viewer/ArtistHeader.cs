namespace ChordPro.Viewer;

/// <summary>A heading row in the library list when it is grouped by artist; tapping it expands or collapses the group.</summary>
public sealed record ArtistHeader(string Artist, int Count, bool IsExpanded)
{
    public string Label => $"{(IsExpanded ? "▾" : "▸")}  {Artist}";
}
