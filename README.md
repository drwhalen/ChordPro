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
| **▶ Scroll − / +** | Hands-free auto-scroll (Space to start/stop). With `{duration: 3:10}` in the file it's paced to reach the end as the song finishes; − / + adjust the speed. |
| **View** | Show or hide chords (a lyrics-only view for singers), show chord diagrams, and choose guitar or ukulele diagrams. |
| **Stage** | Full screen, larger text, and two columns on wide screens. Esc to exit. |
| **PDF…** | Save a print-ready PDF (Letter or A4 by region) and open it in your PDF viewer to print. |

It also:
- remembers transpose, capo, text size and scroll speed for each song, plus your library, setlist and view options;
- reloads the song when it is saved in another editor, keeping your place;
- opens songs, setlists or a library folder dropped onto the window.

Shortcuts: ⌘/Ctrl+O open, ⌘/Ctrl+P PDF, ⌘/Ctrl +/− text size, Space auto-scroll, ← → setlist, Esc leave stage mode.

## Chord diagrams

Shapes come from `{define: …}` lines in the song when present. Otherwise standard fingerings are used for
common chords, and a search over the fretboard finds a playable shape for anything else (sevenths,
suspensions, extensions, slash chords), for guitar (EADGBE) or ukulele (GCEA).

## Layout

- `src/ChordPro.Core`: song model, `ChordProParser`, `ChordTransposer`, chord theory and diagram search
  (`Chords/`), `Setlist`, `SongLibrary`. No UI dependencies.
- `src/ChordPro.Viewer`: Avalonia UI. `SongRenderer` turns a `Song` into controls, `ChordDiagram` draws
  fretboards, `AutoScroller`, `PdfExporter` (Skia vector PDF), `AppSettings` (JSON settings).
- `tests/ChordPro.Core.Tests`: unit tests (`dotnet test`).

Settings live in `settings.json` under the user's local application-data folder; set
`CHORDPRO_VIEWER_SETTINGS` to use a different file.

## Supported ChordPro

Metadata (`title`/`t`, `subtitle`/`st`, `artist`, `key`, `capo`, `tempo`, `time`, `duration`, `{meta: …}`),
inline `[chords]`, comments (`c`, `ci`, `cb`), sections (`start_of_`/`end_of_` verse, chorus, bridge,
tab, grid and custom, plus `soc`/`eoc`-style short forms and `label="…"`), `{chorus}` recall,
`{define}` chord shapes, and `#` comment lines. Formatting and layout directives (fonts, columns, page
breaks) are ignored.
