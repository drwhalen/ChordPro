# ChordPro Viewer

A cross-platform (macOS / Windows / Linux) .NET 10 desktop app for reading and playing from songs
written in [ChordPro](https://www.chordpro.org/chordpro/) format. Songs are shown read-only, with
chords placed above the lyrics.

## Run

```sh
dotnet run --project src/ChordPro.Viewer                          # reopens the last song
dotnet run --project src/ChordPro.Viewer -- samples/morning-road.cho
```

## Mac app

```sh
scripts/package-mac.sh          # this Mac's architecture (arm64 on Apple Silicon)
scripts/package-mac.sh x64      # Intel Macs
```

This builds `artifacts/ChordPro Viewer.app` (self-contained, no .NET install needed) plus a `.zip` and a
`.dmg`. Drag the app to Applications. It opens `.cho`, `.chopro`, `.chordpro`, `.crd`, `.pro` and
`.setlist` files from Finder (double-click or "Open With"), and asks for the microphone in its own name
for the tuner and Follow audio.

The app is ad-hoc signed, which is enough to run it on the Mac that built it. To share it without
Gatekeeper warnings, sign it with an Apple Developer ID certificate (hardened runtime, with the
`com.apple.security.device.audio-input` entitlement for the microphone) and notarize it with
`xcrun notarytool`. The version comes from `<Version>` in `src/ChordPro.Viewer/ChordPro.Viewer.csproj`.

## Features

| Toolbar / sidebar | What it does |
| --- | --- |
| **Songs** sidebar → Library | Choose a folder; every `.cho/.chopro/.chordpro/.crd/.pro` file in it (and subfolders), plus `.txt` files that contain ChordPro, is listed by title and artist, with search. **Group by artist** or **by book** (`{book:}` tags; a song in several books is listed under each) shows collapsible headings instead, with artists sorted ignoring a leading "The". **Key** and **capo** filters narrow the list, e.g. songs in G played without a capo (C# and Db count as one key). The list updates by itself when songs are added, renamed or removed. **Check…** lists problems to fix: headings with nothing under them, a missing `{key}` or one that disagrees with the file name, title and artist swapped, bracketed text that isn't a chord, fingerings whose name doesn't match the chord as written, and setlist lines that match no song. |
| Sidebar → Recent | The last 20 songs you opened. |
| Sidebar → Setlist | Build an ordered list of songs: add, remove, reorder, save/open `.setlist` files or LivePrompter setlists (a `.txt` file of song names, one per line, with `--- Set 2 ---` dividers between sets). Setlists in the library folder (`.setlist` files, and LivePrompter's `Setlists` folder) are listed at the top for one-click switching. Step through with **◀ Previous / Next ▶** or the ← → keys (most page-turner pedals send arrow keys). |
| **A− / A+** | Text size. |
| **Transpose − / +** | Move the song up or down by semitones; chords and the key follow. Click the number to reset. |
| **Capo − / +** | Choose the capo you're playing with. The pitch stays the same and the chord shapes change, e.g. a song in E with capo 2 shows D shapes. Starts at the file's `{capo}`. |
| **Capo → Suggest…** | Lists the easiest capo positions for the song, e.g. "Capo 3 · G shapes · G C D Em · no barre chords". Click one to use it; the song sounds the same, only the shapes change. |
| **Scroll mode** (per song) | **Manual**: Page Up/Down or Space (what most foot pedals send), the ▲ ▼ buttons, tapping the top or bottom of the song, or swiping. **Timed**: paced to `{duration}`, or to `{x_time: m:ss}` markers; − / + change the speed. **Musical**: paced line by line from `{tempo}`, `{time}` and bar counts, so it holds through instrumental breaks, moves quickly past comments, and brings each line in a beat early; − / + change the tempo. **Follow audio (beta)**: listens through the microphone and plays the musical timeline at the tempo you're playing, holding still when you stop. Scrolling by hand always takes over, and the mode carries on from where you leave it. |
| **View** | Show or hide chords (a lyrics-only view for singers), chord diagrams, guitar or ukulele diagrams, and **Easy chords**, which shows simpler forms (Cmaj9 → C, Am7 → Am, E9 → E7, G/B → G, F#m7b5 → F#m; dominant 7ths and sus chords stay). |
| **Tap a chord** | Click any chord, in the lyrics or the diagrams, to see its fingering. ◀ ▶ page through alternatives: the song's own `{define}` shape if it has one, the standard shape, then movable shapes up the neck. |
| **Theme** | System (follows light/dark), Day, Night, three high-contrast themes (black on white, white on black, yellow on black) and an all-red Night vision theme for dark stages. **New theme…** opens an editor with color pickers, a live preview and WCAG contrast ratings (7:1 and up is high contrast); your themes are saved and can be edited or deleted. Themes other than Day/Night recolor the whole window, not just the song. |
| **Stage** | Full screen, larger text, and two columns on wide screens. When the song is in the setlist, the next song and its key show in the corner ("Next: Set 2 · Bike (G)"). Esc to exit. |
| **PDF…** | Save a print-ready PDF (Letter or A4 by region) and open it in your PDF viewer to print. |
| **Tuner** | A chromatic tuner using the microphone: the note, a cents needle (green within ±5 cents), "tune up / tune down", the frequency, and your instrument's strings (guitar EADGBE or ukulele GCEA) with the matching one highlighted. The A4 reference is adjustable (440 Hz by default). |

Page layout: a chord is never separated from its lyric. Printed pages and on-screen page turns keep a
section (a chorus, a verse) together whenever it fits; a longer section breaks between lines with at least
two lines on each side; and the title, chord diagrams and comment-only lead-ins like "Solo" stay with
what follows them.

It also:
- remembers transpose, capo, text size and scroll speed for each song, plus your library, setlist and view options;
- reloads the song when it is saved in another editor, keeping your place;
- opens songs, setlists or a library folder dropped onto the window.

Shortcuts: ⌘/Ctrl+O open, ⌘/Ctrl+P PDF, ⌘/Ctrl+T tuner, ⌘/Ctrl +/− text size, Space start/stop scrolling (page down in manual mode),
Page Up/Down turn pages, ← → setlist, Esc leave stage mode.

## Timing markup for scrolling

ChordPro's standard `{tempo}`, `{time}` and `{duration}` are used, plus these extensions (the `x_` prefix
is ChordPro's convention for custom directives, so other ChordPro apps ignore them):

| Markup | Meaning |
| --- | --- |
| `{x_bars: 4}` | The next line lasts 4 bars (musical scrolling). |
| `{start_of_verse: label="Verse 1" bars="16"}` | The section lasts 16 bars, shared across its lines by chord count. |
| `{x_time: 1:05}` | The next line reaches the reading position at 1:05 (timed scrolling). |

Without bar counts, a line gets one bar per chord, a lyric line without chords two bars, a tab or grid
block four, and comments and blank lines none. See `samples/morning-road.cho` (musical) and
`samples/amazing-grace.cho` (timed markers).

Follow audio is experimental. It tracks the rhythm of your strumming or picking and whether you're
playing; it doesn't recognise chords or melody, so it can drift with very free or sparse playing.
Scroll by hand to correct it. macOS asks for microphone permission the first time.

## Chord diagrams

Shapes come from `{define: …}` lines in the song when present. Otherwise standard fingerings are used for
common chords, and a search over the fretboard finds a playable shape for anything else (sevenths,
suspensions, extensions, slash chords), for guitar (EADGBE) or ukulele (GCEA).

## Layout

- `src/ChordPro.Core`: song model, `ChordProParser`, `ChordTransposer`, chord theory and diagram search
  (`Chords/`), themes and contrast checks (`Themes/`), scroll timing and tempo tracking (`Scrolling/`),
  pitch detection for the tuner (`Tuning/`), page breaks (`Layout/`), `Setlist`, `SongLibrary`. No UI dependencies.
- `src/ChordPro.Viewer`: Avalonia UI. `SongRenderer` turns a `Song` into controls, `ChordDiagram` draws
  fretboards, `SmartScroller` and `ScrollTimelineBuilder` (scroll modes), `AudioFollower` (microphone
  via PortAudio), `PdfExporter` (Skia vector PDF), `AppSettings` (JSON settings),
  `WindowTheme` (recolors the Fluent controls) and `ThemeEditorWindow`.
- `tests/ChordPro.Core.Tests`: unit tests (`dotnet test`).

Settings live in `settings.json` under the user's local application-data folder; set
`CHORDPRO_VIEWER_SETTINGS` to use a different file.

## Supported ChordPro

Metadata (`title`/`t`, `subtitle`/`st`, `artist`, `key`, `capo`, `tempo`, `time`, `duration`, `{meta: …}`),
inline `[chords]`, comments (`c`, `ci`, `cb`), sections (`start_of_`/`end_of_` verse, chorus, bridge,
tab, grid and custom, plus `soc`/`eoc`-style short forms and `label="…"`), `{chorus}` recall,
`{define}`/`{chord}` chord shapes, `{book}`, and `#` comment lines. A short plain line ending in a colon
(`Verse 1:`, `Intro:`) is a section heading, as in LivePrompter. Formatting and layout directives (fonts, columns, page
breaks) are ignored.
