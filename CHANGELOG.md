# Changelog

## 0.4.1 — 2026-10-18

- Deleting a capture (Delete key, right-click › Delete, or the library) asks "Move … to the Recycle Bin?"
  with Yes / No and a "Don't ask me again" box. Turn the question back on in Settings › General ›
  "Ask before deleting a capture".

## 0.4.0 — 2026-10-18

- Capture magnifier now shows the selection (or highlighted window) edge at pixel scale, with the outside
  dimmed, so edges can be lined up exactly.
- Crop undo: undo/redo now refits the view and updates the size info, so an undone crop actually shows the
  full image again (previously it stayed at the cropped zoom and looked permanent). Each capture keeps its own
  undo history while the editor is open, even after switching to other captures and back.
- New Revert button: returns a capture to its original image from before the first crop / resize / canvas /
  slice — stored in the .psnap (`original.png`), so it works after reopening too. Undoable.
- Crop tool: dragging anywhere on the image now draws a crop box straight away (it used to only move the
  full-image box).
- Delete key with no object selected moves the open capture to the Recycle Bin (after a Yes/No prompt) and
  opens the next one.
- Status bar shows the capture's size, when it was created and when its file was last updated.
- Right-click a Recent thumbnail: Open, Open file location, Copy image, Copy file path, Export…, Delete,
  Properties (Windows' file properties). The library's menu gained Copy file path and Properties too.

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
