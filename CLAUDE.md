# CLAUDE.md

Guidance for working in this repository. See README.md for the user-facing feature list.

## What this is

ChordPro Viewer: a cross-platform .NET 10 desktop app (Avalonia 11.3) that displays ChordPro song files,
with a built-in text editor and live preview, transposition, capo, chord diagrams, themes, setlists, smart scrolling, a tuner and PDF
export. The developer works on macOS (Apple Silicon); WPF/WinForms are not options. An iPad/iPhone app
for playing live (`ChordPro.Mobile`, Avalonia iOS) is being built on the same Core: read-only songs, no
editor, new songs or voice features (those stay on the Mac).

## Commands

```sh
dotnet build                                   # whole solution (ChordPro.slnx)
dotnet test                                    # Core unit tests (xUnit)
dotnet run --project src/ChordPro.Viewer -- samples/morning-road.cho
scripts/package-mac.sh [arm64|x64]             # artifacts/ChordPro Viewer.app + .zip + .dmg
dotnet build src/ChordPro.Mobile               # iOS app (simulator); not in the .slnx, needs Xcode + `ios` workload
```

Keep the build at zero warnings. NuGet versions are pinned exactly (Avalonia 11.3.20 for every
Avalonia package, including Avalonia.Controls.ColorPicker; PortAudioSharp2 1.0.6). Avalonia 11.3.0
pulled in a vulnerable Tmds.DBus.Protocol, which is why it was bumped.

## Layout

- `src/ChordPro.Core` — all logic that doesn't need a UI, and everything that has unit tests:
  - `ChordProParser`, `Song` (model), `SongFile` (read/write keeping line endings and BOM),
    `ChordTransposer` (also written/sounding keys), `Setlist`
    (incl. LivePrompter `.txt` setlists; `Step` moves through it past set dividers), `SongLibrary` (scan, group, filter, key-convention guess),
    `LibraryCheck`, `SetlistPlanner` (scores song-to-song steps and plans sets that flow), `SetLength`
    (running time from `{duration}`), `EditorInsert` (the editor's Insert menu: sections, directive lines, the key at the cursor),
    `NewSong` (New song… and Save As…: file name, starting template, a copy moved to another key),
    `SongSettings` + `LibrarySongSettings` (per-song state shared through the library folder),
    `LibraryMirror` (the iOS app's offline copy of the library: songs, setlists, dates; iCloud placeholders),
    `Chords/KeyChords` (the chords of a key, for the key companion), `Chords/ChordProgressions` (what
    usually comes next, and Nashville numbers), `Chords/SongFingerings` (writes a
    chosen fingering as `{define}` or `{define-<selector>}`, see `Instrument.Selector`); a second `{key}`
    parses to a `KeyChangeLine`, `VoiceRange`
    (comfortable keys learned from "My key" marks and transposes), `SongTime`
  - `Chords/` — `ChordName` (chord → intervals), `ChordShapeFinder` (fretboard search for any tuning),
    `StandardShapes` (conventional guitar shapes that override the search), `ChordDifficulty`,
    `ChordSimplifier` (easy chords), `CapoSuggester`, `Instrument` (guitar, ukulele and mandolin tunings, dulcimer/Merlin tunings; a
    diatonic neck has `FretSemitones`, and shape frets are positions as laid out, so fret 7 is "6+";
    `Instrument.Find` maps saved names, including the old "Seagull Merlin"), `SongTuning` (`{meta: tuning …}` or `{x_tuning}`; its offset moves the sounding key like a capo,
    applied only within the player's instrument family), `InstrumentFit` (unplayable chords, best
    transposition; used by the diagram call-out and the library filter)
  - `Themes/SongTheme` (built-in and custom themes, WCAG contrast), `Layout/PageLayout` (page breaks and
    page turns), `Layout/LivePrompterWidth` (LivePrompter's 28 em line budget), `Scrolling/`
    (`MusicalTimeline`, `TimeMarkers`, `ScrollTimeline`, `TempoTracker`, `TapTempo`, `LyricAligner` —
    places heard words in the lyrics for Follow voice; `VoiceTuning` — Follow voice settings;
    `VoiceTraining` — reference timeline, replay and scoring for training),
    `Tuning/` (`PitchDetector` — YIN, `Note`)
- `src/ChordPro.UI` — Avalonia controls shared by the Mac and iOS apps (namespace `ChordPro.UI`):
  `SongRenderer` builds the song as controls from `RenderOptions` (also exposes `Groups` for pagination
  and `LineControls` for scrolling); `ChordDiagram`, `ChordPopup`, `WindowTheme` (recolors Fluent
  controls). Core and UI are `IsTrimmable`, since the iOS build is trimmed: no reflection-based JSON.
  `RenderOptions` clashes with `Avalonia.Media.RenderOptions`, so the apps alias it in their csproj.
- `src/ChordPro.Viewer` — the Mac app. `MainWindow` wires everything;
  `SmartScroller` + `ScrollTimelineBuilder` (scroll modes); `PdfExporter` (Skia vector PDF);
  `ThemeEditorWindow`, `TunerWindow`, `TunerMeter`; `Microphone` (PortAudio), `AudioFollower`, `VoiceFollower` (Whisper.net,
  on-device) + `SpeechModel` (one-time model download); `Transcriber` (Whisper windows and whole
  recordings with word times), `VoiceTrainer` + `VoiceTrainingWindow` (Train…), `AudioFile` (WAV, or
  afconvert on macOS); `ChoiceDialog` (Save / Don't Save / Cancel);
  `SetlistSuggestWindow` (Suggest a set);
  `AppSettings` (JSON).
- `src/ChordPro.Mobile` — the iPad/iPhone app (`net10.0-ios`, `Avalonia.iOS`): `App`, `AppDelegate`
  (+ `SceneDelegate`), `MainView` (switches between the list and a song; refreshes on launch and on
  returning to the foreground; plays setlists), `LibraryView` (Songs | Setlists, search, grouping),
  `SetlistView` (a setlist's songs and sets with running times), `StageView` (text size, transpose,
  theme, tap/pedal paging; per-song settings shared with the Mac; in a setlist, Previous / Next with the
  next song's key, and a pedal press at either end of a song moves to the next or previous song), `LibraryAccess` (Files folder picker,
  security-scoped bookmark, `LibraryMirror` refresh into Application Support, asks iCloud to download
  placeholders), `MobileSettings` (+ `AppFolders`). Songs are always read from the device's copy.
  The samples are embedded for the first launch and App Store review.
  Debug builds read `CHORDPRO_LIVE_LIBRARY` (a library folder on the Mac, instead of the picker) and
  `CHORDPRO_LIVE_OPEN` (opens a song by title), `CHORDPRO_LIVE_SETLIST` (opens a setlist by name) and
  `CHORDPRO_LIVE_SETLIST_SONG` (plays its entry at that index), passed as `SIMCTL_CHILD_…` to `simctl launch`. It suppresses IL2037/IL2104,
  which come from Avalonia's own iOS and designer assemblies.
- `tests/ChordPro.Core.Tests` — tests for Core only; there is no UI test project.
- `samples/` — `amazing-grace.cho` (timed markers), `morning-road.cho` (musical bars, capo, `{define}`),
  `sample.setlist`. Song lyrics in samples must be public domain or original.

## Conventions

- Put new logic in Core with tests whenever it can be expressed without Avalonia; keep the Viewer thin.
- Code style follows the existing files: file-scoped namespaces, primary constructors, collection
  expressions, `GeneratedRegex`, XML doc comments on public members, comments that explain *why*.
- Custom ChordPro directives use the `x_` prefix (`{x_bars}`, `{x_time}`) so other ChordPro apps ignore
  them; section attributes use `key="value"` (`bars="16"`). Unknown directives are ignored, not errors.
- Per-song state (transpose, capo, font size, scroll mode/speed/tempo, plays, My key, tuning) for songs in
  the library lives in the library's own `.chordpro-viewer.json` (`LibrarySongSettings`), keyed by
  library-relative path with `/`, so the Mac and iOS apps share it through iCloud; saves merge song by
  song, newest `Modified` winning. Songs outside the library stay in `AppSettings.Songs` by full path, and
  entries there move into the library file when it's opened. Always go through `AppSettings.ForSong` /
  `SettingsOf` / `AllSongSettings`. Global preferences are top-level `AppSettings` properties.
- Commits: imperative summary line, then a bulleted body; the user asks before each commit and push.

## Gotchas (learned the hard way)

- **Verifying UI changes:** build a throwaway harness in the scratchpad that references the Viewer and uses
  `Avalonia.Headless` + `Avalonia.Skia` (`UseHeadlessDrawing = false`), then `CaptureRenderedFrame()`
  to PNG. Point `CHORDPRO_VIEWER_SETTINGS` at a scratch file so the user's real settings
  (`~/Library/Application Support/ChordPro Viewer/settings.json` via LocalApplicationData) aren't touched,
  and give that file a scratch `LibraryFolder`: song settings are written into the library folder itself.
  - Under the headless platform, `DispatcherTimer` only ticks inside `Dispatcher.UIThread.MainLoop(token)`;
    `RunJobs()` alone never fires timers. Don't `await` in the harness's top-level code either (the
    continuation lands off the UI thread and hangs).
  - Run the built DLL with `dotnet path/to.dll`; the apphost can't find the Homebrew runtime. macOS has
    no `timeout`; use `perl -e 'alarm 90; exec @ARGV' …`.
  - Headless input works (`window.MouseDown/MouseUp`) for checking taps and popups.
- **Never open the microphone during automated checks** — it triggers a macOS permission prompt on the
  user's screen. `TunerWindow` takes `listen: false` and `Feed(samples)` for testing.
- **Follow voice testing:** set `MainWindow.CreateVoiceFollower` to `VoiceFollower.ForRecording(...)` and
  `Feed` it a recording in real time from another thread. macOS `say -o x.aiff "…"` plus
  `afconvert -f WAVE -d LEF32@16000 -c 1` makes spoken test lines (public-domain lyrics only); real
  singing needs the user's recordings. Point `CHORDPRO_VIEWER_MODELS` at a folder holding
  `ggml-base.en.bin` so tests don't download into the user's Application Support.
  Training: `VoiceTrainingWindow.TrainOnFileAsync(path)` with a whole-song WAV (never Record).
- **Errors:** `ErrorLog` writes anything unhandled to `~/Library/Logs/ChordPro Viewer/errors.log`
  (`CHORDPRO_VIEWER_LOG` overrides it), since a Finder-launched app has no console, and keeps UI-thread
  errors from closing the app. Its dispatcher hook must be installed after Avalonia starts (in `App`):
  touching `Dispatcher.UIThread` earlier breaks the main loop. A macOS crash report alone shows .NET
  frames as `???`, so check this log first.
- Replace the editor's text only through `SetEditorText`, which clears the selection first; a
  selection left past the end of the new text is a likely crash with macOS text input.
- Whisper's Metal backend aborts at process exit if a model is still loaded (macOS shows a crash
  report), so `VoiceFollower` frees it on `ProcessExit`; always dispose it.
- Whisper invents words in silence and labels sounds (`[BLANK_AUDIO]`, `(music)`): `VoiceFollower` skips
  quiet windows and strips bracketed labels, and `LyricAligner` needs 3+ matching words to move (both tunable).
- Whisper takes a prompt as what was said *before* the audio: given the upcoming lyrics it leaves those
  words out. Lyric hints are the lines before the current one (`LyricAligner.PromptText`).
- `new DispatcherTimer(interval, priority, handler)` starts the timer immediately; create it with an
  `Interval` and `Tick` when it should start later.
- **Fluent theme recoloring:** Fluent builds brushes from its palette once, so mutating or replacing
  `FluentTheme.Palettes` at runtime doesn't reach every control. `WindowTheme.Apply` swaps in a new
  `FluentTheme` instance instead. `Palettes` only accepts the Light and Dark keys.
- `x:Name` on a `Flyout` generates no field (it's not a control); name the owning button instead.
- `TextBlock` drops trailing spaces when measuring, so lyric cells use non-breaking spaces.
- Every way of opening a song goes through `MainWindow.LoadFile`, which asks about unsaved edits
  first (it's `async void`, but loads synchronously when there's nothing to ask), then `LoadSong`.
  Saving sets `_loadedText` so the file watcher doesn't treat the app's own save as an outside change.
- macOS `FileSystemWatcher` sends spurious events when watching starts; auto-reload compares file text
  before re-rendering.
- `Avalonia.Skia.Helpers.DrawingContextHelper.RenderAsync` overwrites the canvas matrix, so the PDF is
  recorded once into an `SKPicture` and drawn per page with translate/clip.
- **Mac bundle:** everything `dotnet publish` produces goes in `Contents/MacOS`, and macOS treats every
  file there as code, so each file (including `.dll`s) must be signed before the bundle itself, or
  `codesign --verify` fails. Finder opens files through `IActivatableLifetime` / `FileActivatedEventArgs`
  (handled in `App`), not command-line arguments. `packaging/macos/Info.plist` holds the bundle ID,
  document types and microphone usage text; the icon is generated from `Assets/AppIcon.png`.
- **iOS app (`ChordPro.Mobile`):**
  - The iOS 27 SDK refuses to launch apps without the UIScene life cycle, which Avalonia 11.3's
    `AvaloniaAppDelegate` predates: `Info.plist` names a `SceneDelegate` that moves Avalonia's window into
    the scene. Avalonia's lifetime interfaces can't be implemented outside Avalonia.
  - An incremental build may not pick up `Info.plist` changes; delete `bin`/`obj` after editing it.
  - The .NET SDK is Homebrew's, so workloads install user-local (`~/.dotnet`) without sudo; if a build
    says the `ios` workload is missing although `dotnet workload list` shows it, run `dotnet workload repair`.
  - Simulator: `xcrun simctl boot <udid>`, `simctl install <udid> src/ChordPro.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/ChordPro.Mobile.app`,
    `simctl launch --console <udid> com.cranberrysoftware.chordpro-live`, `simctl io <udid> screenshot x.png`. These
    run headless (no Simulator window). The first launch on a fresh simulator can be black for ~20 s.
    `log show --predicate 'process == "ChordPro.Mobile"'` via `simctl spawn` has the launch errors.
- Python `str.replace` in edit scripts fails silently on a mismatch; assert the old text occurs exactly
  once before replacing.
