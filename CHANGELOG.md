# Changelog

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
