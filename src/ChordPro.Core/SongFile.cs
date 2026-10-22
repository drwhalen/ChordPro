using System.Text;

namespace ChordPro.Core;

/// <summary>How a song file is written: its line ending and whether it starts with a byte order mark.</summary>
public sealed record TextFileFormat(string NewLine, bool ByteOrderMark)
{
    public static TextFileFormat Default { get; } = new("\n", false);
}

/// <summary>
/// Reads and writes song files for editing. The text is edited with "\n" line endings; saving puts
/// back the file's own line endings and byte order mark, so other apps reading the same files
/// (LivePrompter, a converter, a text editor) see only the edits.
/// </summary>
public static class SongFile
{
    public static (string Text, TextFileFormat Format) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        return (text.ReplaceLineEndings("\n"), new TextFileFormat(newLine, bom));
    }

    public static void Write(string path, string text, TextFileFormat format)
    {
        var body = text.ReplaceLineEndings("\n");
        if (format.NewLine != "\n")
            body = body.Replace("\n", format.NewLine);
        File.WriteAllText(path, body, new UTF8Encoding(format.ByteOrderMark));
    }
}
