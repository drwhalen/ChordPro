# ChordPro Viewer

A cross-platform (macOS / Windows / Linux) .NET 10 desktop app for reading and playing from songs
written in [ChordPro](https://www.chordpro.org/chordpro/) format. Songs are shown read-only, with
chords placed above the lyrics.

## Run

```sh
dotnet run --project src/ChordPro.Viewer                          # reopens the last song
dotnet run --project src/ChordPro.Viewer -- samples/morning-road.cho
```

## Features

| Toolbar / sidebar | What it does |
| --- | --- |
| **Songs** sidebar → Library | Choose a folder; every `.cho/.chopro/.chordpro/.crd/.pro` file in it (and subfolders) is listed by title and artist, with search. |
| Sidebar → Recent | The last 20 songs you opened. |
| Sidebar → Setlist | Build an ordered list of songs: add, remove, reorder, save/open `.setlist` files, and step through with **◀ Previous / Next ▶** or the ← → keys (most page-turner pedals send arrow keys). |
| **A− / A+** | Text size. |
| **Transpose − / +** | Move the song up or down by semitones; chords and the key follow. Click the number to reset. |
| **Capo − / +** | Choose the capo you're playing with. The pitch stays the same and the chord shapes change, e.g. a song in E with capo 2 shows D shapes. Starts at the file's `{capo}`. |
| **Scroll mode** (per song) | **Manual**: Page Up/Down or Space (what most foot pedals send), the ▲ ▼ buttons, tapping the top or bottom of the song, or swiping. **Timed**: paced to `{duration}`, or to `{x_time: m:ss}` markers; − / + change the speed. **Musical**: paced line by line from `{tempo}`, `{time}` and bar counts, so it holds through instrumental breaks, moves quickly past comments, and brings each line in a beat early; − / + change the tempo. **Follow audio (beta)**: listens through the microphone and plays the musical timeline at the tempo you're playing, holding still when you stop. Scrolling by hand always takes over, and the mode carries on from where you leave it. |
| **View** | Show or hide chords (a lyrics-only view for singers), show chord diagrams, and choose guitar or ukulele diagrams. |
| **Theme** | System (follows light/dark), Day, Night, three high-contrast themes (black on white, white on black, yellow on black) and an all-red Night vision theme for dark stages. **New theme…** opens an editor with color pickers, a live preview and WCAG contrast ratings (7:1 and up is high contrast); your themes are saved and can be edited or deleted. Themes other than Day/Night recolor the whole window, not just the song. |
| **Stage** | Full screen, larger text, and two columns on wide screens. Esc to exit. |
| **PDF…** | Save a print-ready PDF (Letter or A4 by region) and open it in your PDF viewer to print. |

It also:
- remembers transpose, capo, text size and scroll speed for each song, plus your library, setlist and view options;
- reloads the song when it is saved in another editor, keeping your place;
- opens songs, setlists or a library folder dropped onto the window.

Shortcuts: ⌘/Ctrl+O open, ⌘/Ctrl+P PDF, ⌘/Ctrl +/− text size, Space start/stop scrolling (page down in manual mode),
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
  (`Chords/`), themes and contrast checks (`Themes/`), scroll timing and tempo tracking (`Scrolling/`), `Setlist`, `SongLibrary`. No UI dependencies.
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
`{define}` chord shapes, and `#` comment lines. Formatting and layout directives (fonts, columns, page
breaks) are ignored.
