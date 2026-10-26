# Teleprompter

A small always-on-top, semi-transparent teleprompter for Windows that is **hidden from screen capture**
(Zoom, Teams, OBS, Snipping Tool...) via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.

Run `publish\Teleprompter.exe` (needs the .NET 10 Desktop Runtime, already present with the SDK).

Rebuild: `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish`

## Controls
| Window focused | Global (works while Zoom has focus) | Action |
|---|---|---|
| Space | Ctrl+Alt+Space | Play / pause |
| Up / Down | Ctrl+Alt+Up / Down | Speed +/- |
| PgUp / PgDn, Left / Right, mouse wheel | Ctrl+Alt+PgUp / PgDn | Scroll back / forward |
| Home | Ctrl+Alt+Home | Back to top |
| + / - | | Font size |
| O / P | | Background opacity (black only) |
| K / L | | Text opacity (white text only) |
| Z / X | | Stopwatch size |
| E, double-click, Ctrl+V | | Edit text / paste clipboard |
| M | | Mirror |
| B | | Hide / show the button bar |
| | Ctrl+Alt+T | Click-through on / off |
| | Ctrl+Alt+H | Hide / show the window |

The stopwatch in the bottom-left corner counts the time spent playing. Back to top (Home) resets it to 0:00.

Drag the text to move the window; drag the edges to resize. Text and settings are saved to
`%APPDATA%\Teleprompter\settings.json`.

`--show-in-capture` disables the capture exclusion (for testing what the window looks like in a recording).

## Caveats
- Capture exclusion needs Windows 10 version 2004 or later. On older Windows the window shows as a black box.
- Exclusion applies to software capture only; a phone filming the screen will still see it.
- Zoom must share a screen/window or record normally; the window is simply absent from the captured image.

## Version téléphone (web)
Une version web pour enregistrer avec le téléphone est dans [`docs/`](docs/README.md) (GitHub Pages : branche `main`, dossier `/docs`).
