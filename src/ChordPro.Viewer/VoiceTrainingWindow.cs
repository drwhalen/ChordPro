using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChordPro.Core;
using ChordPro.Core.Scrolling;

namespace ChordPro.Viewer;

/// <summary>
/// Trains Follow voice to the singer: record (or import) yourself singing the open song, and
/// <see cref="VoiceTrainer"/> finds the settings that follow you best, shown as before and after.
/// The recording stays in memory and is gone when the window closes, unless the singer keeps it.
/// </summary>
public sealed class VoiceTrainingWindow : Window
{
    private static readonly string RecordingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChordPro Viewer", "Recordings");

    private readonly Song _song;
    private readonly VoiceTuning _current;
    private readonly string _modelPath;
    private readonly Action<VoiceTuning?> _save;
    private readonly Button _record = new() { Content = "● Record" };
    private readonly Button _import = new() { Content = "Import a recording…" };
    private readonly TextBlock _time = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 1, Height = 6, MinHeight = 6, IsVisible = false };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    private readonly TextBlock _status;
    private readonly TextBlock _result;
    private readonly TextBlock _detail;
    private readonly CheckBox _keep = new() { Content = "Keep the recording (saved when you close this window)", IsVisible = false };
    private readonly Button _use = new() { Content = "Use these settings", IsVisible = false, Classes = { "accent" } };
    private readonly Button _reset = new() { Content = "Go back to the standard settings" };
    private readonly DispatcherTimer _meter;
    private readonly object _lock = new();
    private Microphone? _microphone;
    private List<float> _recorded = [];
    private float _recordedLevel;
    private DateTime _recordingStarted;
    private CancellationTokenSource? _training;
    private float[]? _audio;
    private TrainingResult? _found;

    public VoiceTrainingWindow() : this(new Song(), VoiceTuning.Default, false, SpeechModel.FilePath,
        SongPalette.FromTheme(Core.Themes.SongTheme.Day), _ => { }) { }

    /// <param name="current">The settings Follow voice uses now.</param>
    /// <param name="customized">Whether those came from training (so going back to the standard ones is offered).</param>
    /// <param name="save">Saves settings; null goes back to the standard ones.</param>
    public VoiceTrainingWindow(Song song, VoiceTuning current, bool customized, string modelPath, SongPalette palette,
        Action<VoiceTuning?> save)
    {
        _song = song;
        _current = current;
        _modelPath = modelPath;
        _save = save;

        Title = "Train Follow voice";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = palette.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBlock Text(string text, double size = 13, IBrush? brush = null, FontWeight weight = FontWeight.Normal) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush ?? palette.Text,
            TextWrapping = TextWrapping.Wrap,
        };
        _status = Text("", 13, palette.MutedText);
        _result = Text("", 15, null, FontWeight.SemiBold);
        _detail = Text("", 12, palette.MutedText);
        _time.Foreground = palette.MutedText;

        _record.Click += (_, _) => { if (_microphone is null) StartRecording(); else StopRecording(); };
        _import.Click += async (_, _) => await ImportAsync();
        _use.Click += (_, _) => UseFound();
        _reset.Click += (_, _) =>
        {
            _save(null);
            _reset.IsVisible = false;
            _status.Text = "Follow voice is back to the standard settings.";
        };
        _reset.IsVisible = customized;
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();

        // (The constructor that takes the handler would start the timer straight away.)
        _meter = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _meter.Tick += (_, _) => ShowRecordingLevel();

        var title = song.Title is { Length: > 0 } t ? $"“{t}”" : "this song";
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                Text("Train Follow voice", 18, null, FontWeight.SemiBold),
                Text($"Sing {title} from the start, the way you would on stage, with the microphone you'll use. "
                    + "Follow voice then replays your singing with different settings and keeps the ones that follow you best."),
                Text("Everything stays on this computer. The recording is deleted when you close this window unless you keep it.",
                    12, palette.MutedText),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _record, _import, _time } },
                _level,
                _progress,
                _status,
                _result,
                _detail,
                _keep,
                new DockPanel
                {
                    LastChildFill = false,
                    Children =
                    {
                        Docked(close, Dock.Right),
                        Docked(_use, Dock.Right),
                        Docked(_reset, Dock.Left),
                    },
                },
            },
        };
        _status.Text = current == VoiceTuning.Default ? "Using the standard settings." : $"Using trained settings: {current}.";
        Closed += (_, _) => OnClosed();
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.Margin = new Thickness(dock == Dock.Right ? 8 : 0, 0, 0, 0);
        return control;
    }

    private void StartRecording()
    {
        if (Microphone.TryOpen(out var error) is not { } microphone)
        {
            _status.Text = $"Can't record: {error}";
            return;
        }
        lock (_lock)
            _recorded = new List<float>(microphone.SampleRate * 240);
        microphone.SamplesReceived += samples =>
        {
            lock (_lock)
            {
                foreach (var s in samples)
                    _recorded.Add(s);
                _recordedLevel = Transcriber.Level(samples);
            }
        };
        _microphone = microphone;
        _recordingStarted = DateTime.UtcNow;
        _record.Content = "■ Stop";
        _import.IsEnabled = false;
        _level.IsVisible = true;
        ClearResult();
        _status.Text = "Recording. Sing the song, then press Stop.";
        _meter.Start();
    }

    private void ShowRecordingLevel()
    {
        float level;
        lock (_lock)
            level = _recordedLevel;
        // Show the level in decibels, -60 dB to full scale, as a meter would.
        _level.Value = Math.Clamp(1 + 20 * Math.Log10(Math.Max(level, 1e-6)) / 60, 0, 1);
        _time.Text = (DateTime.UtcNow - _recordingStarted).ToString(@"m\:ss");
    }

    private void StopRecording()
    {
        if (_microphone is not { } microphone)
            return;
        _meter.Stop();
        _microphone = null;
        microphone.Dispose();
        float[] recorded;
        lock (_lock)
        {
            recorded = [.. _recorded];
            _recorded = [];
        }
        _record.Content = "● Record";
        _import.IsEnabled = true;
        _level.IsVisible = false;
        _ = TrainAsync(AudioFile.Resample(recorded, microphone.SampleRate, Transcriber.SampleRate), canKeep: true);
    }

    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a recording of you singing this song",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Recordings") { Patterns = [.. AudioFile.Extensions.Select(e => "*." + e)] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        await TrainOnFileAsync(path);
    }

    /// <summary>Trains on a recording file (what Import does after the file is chosen).</summary>
    public async Task TrainOnFileAsync(string path)
    {
        ClearResult();
        _status.Text = $"Reading {Path.GetFileName(path)}…";
        float[] audio;
        try
        {
            audio = await Task.Run(() => AudioFile.Load(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't read the recording: {ex.Message}";
            return;
        }
        // An imported file is already kept where it came from.
        await TrainAsync(audio, canKeep: false);
    }

    /// <summary>Trains on a recording of 16 kHz mono samples.</summary>
    public async Task TrainAsync(float[] audio, bool canKeep)
    {
        if (!File.Exists(_modelPath))
        {
            _status.Text = "The speech model isn't downloaded yet. Choose Follow voice and press “Get voice model” first.";
            return;
        }
        _audio = canKeep ? audio : null;
        _training = new CancellationTokenSource();
        _record.IsEnabled = _import.IsEnabled = false;
        _progress.Value = 0;
        _progress.IsVisible = true;
        _time.Text = TimeSpan.FromSeconds(audio.Length / (double)Transcriber.SampleRate).ToString(@"m\:ss");
        var progress = new Progress<(double Fraction, string? Status)>(p =>
        {
            _progress.Value = p.Fraction;
            if (p.Status is not null)
                _status.Text = p.Status;
        });
        try
        {
            ShowResult(await VoiceTrainer.TrainAsync(_modelPath, _song, audio, _current, progress, _training.Token));
        }
        catch (TrainingException ex)
        {
            _status.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // closed while training
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Voice training", ex);
            _status.Text = $"Training didn't finish: {ex.Message}";
        }
        finally
        {
            _progress.IsVisible = false;
            _record.IsEnabled = _import.IsEnabled = true;
            _training = null;
        }
    }

    private void ShowResult(TrainingResult found)
    {
        _found = found;
        _status.Text = $"Heard {found.Reference.Coverage:P0} of the lyrics in the recording.";
        if (found.Improved)
        {
            _result.Text = found.CurrentScore is { } before
                ? $"Before: {before.Exact:P0} · After: {found.BestScore.Exact:P0}"
                : $"After: {found.BestScore.Exact:P0}";
            _detail.Text = $"The share of the time Follow voice showed the line you were singing"
                + (found.CurrentScore is { } was ? $" ({was.Near:P0} → {found.BestScore.Near:P0} within a line)." : ".")
                + $"\nNew settings: {found.Best}.";
            _use.IsVisible = true;
        }
        else
        {
            _result.Text = $"Your current settings already follow you best: {found.BestScore.Exact:P0}";
            _detail.Text = $"It showed the line you were singing {found.BestScore.Exact:P0} of the time ({found.BestScore.Near:P0} within a line). Nothing to change.";
        }
        _keep.IsVisible = _audio is not null;
        _keep.IsChecked = false;
    }

    private void ClearResult()
    {
        _found = null;
        _result.Text = _detail.Text = "";
        _use.IsVisible = _keep.IsVisible = false;
    }

    private void UseFound()
    {
        if (_found is null)
            return;
        _save(_found.Best);
        _use.IsVisible = false;
        _reset.IsVisible = true;
        _status.Text = "Saved. Follow voice will use these settings from now on.";
    }

    /// <summary>Whether the recording will be kept when the window closes (for testing).</summary>
    public bool KeepRecording
    {
        get => _keep.IsChecked == true;
        set => _keep.IsChecked = value;
    }

    /// <summary>The training result shown, if any (for testing).</summary>
    public TrainingResult? Found => _found;

    private void OnClosed()
    {
        _training?.Cancel();
        _meter.Stop();
        _microphone?.Dispose();
        _microphone = null;
        if (_audio is not null && KeepRecording)
        {
            try
            {
                Directory.CreateDirectory(RecordingsFolder);
                var name = string.Concat((_song.Title ?? "Song").Split(Path.GetInvalidFileNameChars()));
                AudioFile.SaveWav(Path.Combine(RecordingsFolder, $"{name} {DateTime.Now:yyyy-MM-dd HHmm}.wav"), _audio);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write("Keeping the training recording", ex);
            }
        }
        _audio = null;
    }
}
