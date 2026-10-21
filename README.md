# ChordPro Viewer

A cross-platform (macOS / Windows / Linux) .NET 10 desktop app that opens a song written in
[ChordPro](https://www.chordpro.org/chordpro/) format and shows it in a read-only, scrollable view,
with chords placed above the lyrics.

## Run

```sh
dotnet run --project src/ChordPro.Viewer                          # then click Open… or drop a file
dotnet run --project src/ChordPro.Viewer -- samples/amazing-grace.cho
```

Shortcuts: ⌘/Ctrl+O open, ⌘/Ctrl +/− change text size.

## Layout

- `src/ChordPro.Core` – song model and `ChordProParser` (no UI dependencies)
- `src/ChordPro.Viewer` – Avalonia UI; `SongRenderer` turns a `Song` into controls
- `tests/ChordPro.Core.Tests` – parser unit tests (`dotnet test`)

## Supported ChordPro

Metadata (`title`/`t`, `subtitle`/`st`, `artist`, `key`, `capo`, `tempo`, `time`, `{meta: …}`),
inline `[chords]`, comments (`c`, `ci`, `cb`), sections (`start_of_`/`end_of_` verse, chorus, bridge,
tab, grid and custom, plus `soc`/`eoc`-style short forms and `label="…"`), `{chorus}` recall, and
`#` comment lines. Formatting and layout directives (fonts, columns, page breaks, chord diagrams) are ignored.
