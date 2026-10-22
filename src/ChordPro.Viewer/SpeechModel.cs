namespace ChordPro.Viewer;

/// <summary>
/// The Whisper speech model used to follow singing. It is too big to ship with the app (148 MB),
/// so it is downloaded once, when the player first asks for it, from the whisper.cpp project's
/// published models; after that everything runs offline.
/// </summary>
public static class SpeechModel
{
    public const string FileName = "ggml-base.en.bin";
    public const string DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + FileName;
    public const long ApproximateBytes = 147_964_211;

    // CHORDPRO_VIEWER_MODELS overrides the folder (useful for testing).
    private static readonly string Folder = Environment.GetEnvironmentVariable("CHORDPRO_VIEWER_MODELS") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChordPro Viewer", "Models");

    public static string FilePath => Path.Combine(Folder, FileName);

    // A finished download is close to the published size; a cut-off one isn't.
    public static bool IsDownloaded => File.Exists(FilePath) && new FileInfo(FilePath).Length > ApproximateBytes * 0.9;

    /// <summary>Downloads the model, reporting progress from 0 to 1. Writes to a temporary file first, so a failed download leaves nothing half-written.</summary>
    public static async Task DownloadAsync(IProgress<double> progress, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(Folder);
        var partial = FilePath + ".part";
        using (var http = new HttpClient())
        using (var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? ApproximateBytes;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var target = File.Create(partial);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                progress.Report(Math.Min(1, done / (double)total));
            }
        }
        File.Move(partial, FilePath, overwrite: true);
    }
}
