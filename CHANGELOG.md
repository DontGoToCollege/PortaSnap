# Changelog

## 0.3.0 — 2026-10-18

- Editor zoom: new "Fit to border" level (fills the view edge to edge, enlarging small captures) and it is
  now the default. Right-click any level in the zoom list to make it the default (marked ★); Ctrl+0 returns
  to the default, Ctrl+1 is 100%.
- Settings › Magnifier zoom: choose how close the capture magnifier zooms (9× … 2×). The loupe keeps its
  size and look; it just shows more pixels when zoomed out.
- Recent tray: sort button (newest first by default ↔ oldest first). Left/Right arrow keys now actually
  open the previous/next capture (when nothing is selected).
- Drawing: new objects are drawn as-is with no selection box. With the Arrow/Line (also Pen, Marker, Step)
  tool active, clicking directly on an existing one of that kind selects it so its handles can be dragged.
- New Slice tool (X): drag across a band to cut it out. Vertical by default, hold Alt for horizontal, or pick
  the direction in the panel. "Snap the pieces together" (on by default) closes the gap; off leaves it transparent.

## 0.2.0 — 2026-10-18

- Dark mode: Settings › Appearance (Match Windows / Light / Dark). Switches live; "Match Windows"
  follows the Windows app theme as it changes. All controls (menus, dropdowns, checkboxes, sliders,
  scroll bars, tooltips) are themed, plus dark title bars and a dark transparency checkerboard.
- Start with Windows: new "Start PortaSnap when I sign in to Windows" option adds a per-user Run entry
  (`PortaSnap.exe --tray`), kept pointing at the exe's current location since the app is portable.
  "Start in tray" now only affects manual launches. Settings from 0.1.0 with "start in tray" on are
  migrated to "start with Windows" on.
- The editor now opens the newest capture automatically when opened without a specific file.

## 0.1.0 — 2026-10-18

First version.

- Portable single-file exe; all data in `PortaSnap-Data` next to the exe (LocalAppData fallback).
- Capture: region / window / pane / full screen / all screens with frozen-screen overlay, magnifier,
  delay timer, cursor option; scrolling capture with automatic stitching.
- Screen recording to MP4 (H.264/AAC, Windows' built-in encoder) with optional microphone,
  pause/resume, countdown; preview window with frame grab.
- Global hotkeys via low-level keyboard hook (Print Screen family by default), tray icon.
- Editor: arrow, line, shape, text, callout, step, highlight, blur/pixelate/redact, pen, marker,
  magnify, spotlight, crop; per-tool remembered styles; border / drop shadow / torn-edge effects;
  resize and canvas expand; undo/redo; copy, paste, drag-out; export PNG/JPG/BMP/GIF/TIFF/.psnap.
- Library with thumbnails, search, filters, recycle-bin delete; recent-captures tray in the editor.
- Snagit `.snagx` import (single files or a whole library), including annotations.
- Command line: `--export`, `--record`, `--tray`, open file.
