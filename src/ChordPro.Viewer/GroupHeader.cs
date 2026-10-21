namespace ChordPro.Viewer;

/// <summary>A heading row (an artist or a book) in the grouped library list; tapping it expands or collapses the group.</summary>
public sealed record GroupHeader(string Name, int Count, bool IsExpanded)
{
    public string Label => $"{(IsExpanded ? "▾" : "▸")}  {Name}";
}
