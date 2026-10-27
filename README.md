# PortaSnap

A portable, offline screen-capture tool and image editor for Windows 10/11 — a from-scratch
recreation of the Snagit workflow. One exe, no installer, no account, no internet.

## Use it

Copy `dist\PortaSnap.exe` anywhere (USB stick, synced folder, another PC) and run it. It keeps
everything in a `PortaSnap-Data` folder next to itself, so the exe and that folder travel together:

```
PortaSnap.exe
PortaSnap-Data\
  settings.json      hotkeys, tool styles, options
  Library\           your captures as image files (JPG by default) and recordings (.mp4)
    .projects\       (hidden) the editable .psnap project behind each capture
  portasnap.log
```

If the exe sits somewhere read-only, data falls back to `%LocalAppData%\PortaSnap`.

"Start with Windows" is the one thing written outside that folder: a per-user Run entry
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PortaSnap`). Untick it before retiring a copy.

## Features

**Capture**
- Region (drag) or window/pane (hover + click; mouse-wheel picks smaller panes), full screen, all screens
- Frozen-screen overlay with crosshair, size badge and pixel magnifier; works across mixed-DPI monitors
- Delay timer (3/5/10 s), optional cursor capture
- Scrolling capture — auto-scrolls a window and stitches it into one tall image
- Screen recording to MP4 (H.264/AAC via the encoder built into Windows), optional microphone,
  pause/resume, countdown, frame grab from recordings
- Global hotkeys (Print Screen by default) via a keyboard hook, so they beat the Snipping Tool binding

**Editor**
- Arrow, line, shape (rect/rounded/ellipse), text, callout, numbered steps, highlight, blur/pixelate/redact,
  pen, marker, magnifier lens, spotlight, crop, slice (cut out a vertical/horizontal band and join the pieces)
- Colours, fill, thickness, opacity, shadows, dash styles, arrow heads, fonts — each tool remembers its style
- Image effects: border, drop shadow, torn edges; resize; expand canvas
- Undo/redo (per capture), Revert to the original capture after crops/resizes, copy (Ctrl+Shift+C), paste images,
  drag the result straight into other apps; right-click Recent thumbnails for file actions
- Export PNG/JPG/BMP/GIF/TIFF or an editable `.psnap` project

**App**
- Light / dark / match-Windows theme; optional start with Windows (quietly, in the tray)

**Library**
- Every capture is a normal image file (JPG/JPEG/PNG/BMP/GIF/TIFF, JPEG quality adjustable) backed by an
  editable .psnap project; saving in the editor rewrites the image. Thumbnail grid with search and filters
- Optional .psnap file association (Settings), per user
- Import your Snagit library (`.snagx` files, including annotations) — see `docs/SNAGX-FORMAT.md`

## Hotkeys (default)

| Action | Keys |
|---|---|
| Region / window capture | Print Screen |
| Full screen | Ctrl + Print Screen |
| Scrolling capture | Ctrl + Shift + Print Screen |
| Start / stop recording | Shift + Print Screen |

Editor: V select, A arrow, L line, R shape, T text, O callout, S step, H highlight, B blur, P pen,
K marker, M magnify, G spotlight, C crop, X slice (Alt = other direction) · Ctrl+Z/Y undo/redo · Ctrl+S save · Ctrl+E export ·
Ctrl+D duplicate · Delete (the selected object, or the open capture when nothing is selected) · arrows nudge the selection (Shift = 10 px) or, with nothing selected, step
through the Recent tray · Ctrl+wheel zoom · Ctrl+0 default zoom (right-click a zoom level to set it) · Ctrl+1 100%.

## Command line

```
PortaSnap.exe <file>                                   open a .psnap / .snagx / image / .mp4
PortaSnap.exe --tray                                   start hidden in the tray
PortaSnap.exe --export <in> <out.png|.jpg|...>         flatten a capture to an image
PortaSnap.exe --record <x,y,w,h> <seconds> <out.mp4> [--fps N] [--mic] [--no-cursor]
```

## Build

Requires the .NET SDK (8+). `.\publish.ps1` → `dist\PortaSnap.exe` (self-contained single file).
Before starting a new version run `.\archive-version.ps1`, then bump `<Version>` in the csproj and
`app.manifest` and add a `CHANGELOG.md` entry.

## Layout

```
src\PortaSnap\
  Capture\        overlay, GDI grabber, window detection, scrolling capture, controller
  Video\          MP4 recorder (Media Foundation via WinRT), recording UI
  Editor\         editor window, canvas, dialogs
  Model\          annotations, document, renderer, .psnap file, undo
  Library\        library store
  Import\         .snagx importer
  Infrastructure\ Win32 interop, settings, hotkeys, tray, portable paths, CLI
```
