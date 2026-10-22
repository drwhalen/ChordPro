using System.Text;
using ChordPro.Core;

namespace ChordPro.Core.Tests;

public sealed class SongFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("chordpro-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public void SavingKeepsTheFilesLineEndingsAndByteOrderMark(string newLine, bool bom)
    {
        var path = Path.Combine(_folder, "song.cho");
        File.WriteAllText(path, $"{{title: Café}}{newLine}[G]Line one{newLine}", new UTF8Encoding(bom));

        var (text, format) = SongFile.Read(path);
        Assert.Equal("{title: Café}\n[G]Line one\n", text);
        Assert.Equal(new TextFileFormat(newLine, bom), format);

        SongFile.Write(path, text + "[C]Line two\n", format);

        var expected = $"{{title: Café}}{newLine}[G]Line one{newLine}[C]Line two{newLine}";
        Assert.Equal(new UTF8Encoding(bom).GetPreamble().Concat(Encoding.UTF8.GetBytes(expected)), File.ReadAllBytes(path));
    }
}
