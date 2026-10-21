# CLAUDE.md

Guidance for working in this repository. See README.md for the user-facing feature list.

## What this is

ChordPro Viewer: a cross-platform .NET 10 desktop app (Avalonia 11.3) that displays ChordPro song files
read-only, with transposition, capo, chord diagrams, themes, setlists, smart scrolling, a tuner and PDF
export. The developer works on macOS (Apple Silicon); WPF/WinForms are not options.

## Commands

```sh
dotnet build                                   # whole solution (ChordPro.slnx)
dotnet test                                    # Core unit tests (xUnit)
dotnet run --project src/ChordPro.Viewer -- samples/morning-road.cho
scripts/package-mac.sh [arm64|x64]             # artifacts/ChordPro Viewer.app + .zip + .dmg
```

Keep the build at zero warnings. NuGet versions are pinned exactly (Avalonia 11.3.20 for every
Avalonia package, including Avalonia.Controls.ColorPicker; PortAudioSharp2 1.0.6). Avalonia 11.3.0
pulled in a vulnerable Tmds.DBus.Protocol, which is why it was bumped.

## Layout

- `src/ChordPro.Core` — all logic that doesn't need a UI, and everything that has unit tests:
  - `ChordProParser`, `Song` (model), `ChordTransposer`, `Setlist`, `SongLibrary`, `SongTime`
  - `Chords/` — `ChordName` (chord → intervals), `ChordShapeFinder` (fretboard search for any tuning),
    `StandardShapes` (conventional guitar shapes that override the search), `ChordDifficulty`,
    `ChordSimplifier` (easy chords), `CapoSuggester`, `Instrument`
  - `Themes/SongTheme` (built-in and custom themes, WCAG contrast), `Layout/PageLayout` (page breaks and
    page turns), `Scrolling/` (`MusicalTimeline`, `TimeMarkers`, `ScrollTimeline`, `TempoTracker`),
    `Tuning/` (`PitchDetector` — YIN, `Note`)
- `src/ChordPro.Viewer` — Avalonia UI. `MainWindow` wires everything; `SongRenderer` builds the song as
  controls from `RenderOptions` (also exposes `Groups` for pagination and `LineControls` for scrolling);
  `SmartScroller` + `ScrollTimelineBuilder` (scroll modes); `PdfExporter` (Skia vector PDF);
  `WindowTheme` (recolors Fluent controls); `ThemeEditorWindow`, `TunerWindow`, `ChordPopup`,
  `ChordDiagram`, `TunerMeter`; `Microphone` (PortAudio), `AudioFollower`; `AppSettings` (JSON).
- `tests/ChordPro.Core.Tests` — tests for Core only; there is no UI test project.
- `samples/` — `amazing-grace.cho` (timed markers), `morning-road.cho` (musical bars, capo, `{define}`),
  `sample.setlist`. Song lyrics in samples must be public domain or original.

## Conventions

- Put new logic in Core with tests whenever it can be expressed without Avalonia; keep the Viewer thin.
- Code style follows the existing files: file-scoped namespaces, primary constructors, collection
  expressions, `GeneratedRegex`, XML doc comments on public members, comments that explain *why*.
- Custom ChordPro directives use the `x_` prefix (`{x_bars}`, `{x_time}`) so other ChordPro apps ignore
  them; section attributes use `key="value"` (`bars="16"`). Unknown directives are ignored, not errors.
- Per-song state (transpose, capo, font size, scroll mode/speed/tempo) lives in `AppSettings.Songs`
  keyed by full path; global preferences are top-level `AppSettings` properties.
- Commits: imperative summary line, then a bulleted body; the user asks before each commit and push.

## Gotchas (learned the hard way)

- **Verifying UI changes:** build a throwaway harness in the scratchpad that references the Viewer and uses
  `Avalonia.Headless` + `Avalonia.Skia` (`UseHeadlessDrawing = false`), then `CaptureRenderedFrame()`
  to PNG. Point `CHORDPRO_VIEWER_SETTINGS` at a scratch file so the user's real settings
  (`~/Library/Application Support/ChordPro Viewer/settings.json` via LocalApplicationData) aren't touched.
  - Under the headless platform, `DispatcherTimer` only ticks inside `Dispatcher.UIThread.MainLoop(token)`;
    `RunJobs()` alone never fires timers. Don't `await` in the harness's top-level code either (the
    continuation lands off the UI thread and hangs).
  - Run the built DLL with `dotnet path/to.dll`; the apphost can't find the Homebrew runtime. macOS has
    no `timeout`; use `perl -e 'alarm 90; exec @ARGV' …`.
  - Headless input works (`window.MouseDown/MouseUp`) for checking taps and popups.
- **Never open the microphone during automated checks** — it triggers a macOS permission prompt on the
  user's screen. `TunerWindow` takes `listen: false` and `Feed(samples)` for testing.
- **Fluent theme recoloring:** Fluent builds brushes from its palette once, so mutating or replacing
  `FluentTheme.Palettes` at runtime doesn't reach every control. `WindowTheme.Apply` swaps in a new
  `FluentTheme` instance instead. `Palettes` only accepts the Light and Dark keys.
- `x:Name` on a `Flyout` generates no field (it's not a control); name the owning button instead.
- `TextBlock` drops trailing spaces when measuring, so lyric cells use non-breaking spaces.
- macOS `FileSystemWatcher` sends spurious events when watching starts; auto-reload compares file text
  before re-rendering.
- `Avalonia.Skia.Helpers.DrawingContextHelper.RenderAsync` overwrites the canvas matrix, so the PDF is
  recorded once into an `SKPicture` and drawn per page with translate/clip.
- **Mac bundle:** everything `dotnet publish` produces goes in `Contents/MacOS`, and macOS treats every
  file there as code, so each file (including `.dll`s) must be signed before the bundle itself, or
  `codesign --verify` fails. Finder opens files through `IActivatableLifetime` / `FileActivatedEventArgs`
  (handled in `App`), not command-line arguments. `packaging/macos/Info.plist` holds the bundle ID,
  document types and microphone usage text; the icon is generated from `Assets/AppIcon.png`.
- Python `str.replace` in edit scripts fails silently on a mismatch; assert the old text occurs exactly
  once before replacing.
