# Snagit `.snagx` format notes

How PortaSnap reads Snagit files. This was worked out with REA (Reverse Engineer Anything) by
inspecting the user's own `.snagx` files plus the two onboarding samples that ship with Snagit
(`%LocalAppData%\TechSmith\Snagit\Onboarding\*.snagx`). No Snagit program code was decompiled; this
describes only the file contents, so PortaSnap can open the user's existing captures.

Verified on Snagit 2026.0.1 files, both Windows (`ExportedFromPlatform: "win"`) and macOS.

## Container

A `.snagx` file is a plain ZIP archive:

| Entry | Contents |
|---|---|
| `index.json` | `{ "Pages": ["{GUID}.json"], "Version": "1.0" }` |
| `metadata.json` | Capture info: `AppName`, `AppCompany`, `AppVersion`, `WindowName`, `WebURL`, `CaptureDate` (UTC, `yyyy-MM-dd HH:mm:ss`), `CaptureId`, `LanguageCode` |
| `{GUID}.json` | The page: canvas, background and annotation objects (below) |
| `{GUID}.png` | The captured image (absent for template pages) |
| `thumbnail.png` | Snagit's flattened preview (useful for comparing renders) |

## Page (`{GUID}.json`)

| Key | Meaning |
|---|---|
| `CaptureCanvasWidth`, `CaptureCanvasHeight` | Canvas size in **canvas units** |
| `CaptureBackgroundImage` | Name of the PNG entry |
| `CaptureBackgroundLocation` | `"x,y"` offset of the image on the canvas |
| `DPI` | Capture DPI (e.g. 168 at 175%, 192 at 200%) |
| `BackgroundSize`, `GradientBackgroundColor` | Template/blank canvas size and fill |
| `CaptureObjects` | Array of annotation objects (only present when the capture was annotated) |
| `StepToolSequence*`, `SmartRedactProperties`, `SimplifySettings` | Editor state (ignored) |

**Units.** On Windows, canvas units equal image pixels (`CaptureCanvasWidth` == PNG width). Mac files
use points (PNG is 2× the canvas at Retina). PortaSnap scales everything by `PNG width / canvas width`.
Coordinates are top-left origin, y down, on both platforms.

## Annotation objects (`CaptureObjects[]`)

Type is `ToolMode`. Common fields:

| Field | Meaning |
|---|---|
| `PointsArray` | `["x,y", ...]` — two corners for box tools, the full stroke for `Pen` |
| `StartPoint`, `EndPoint` | Same as first/last point (for `Callout` these hold tail-control ratios instead) |
| `ForegroundColor`, `BackgroundColor` | `#AARRGGBB` |
| `StrokeWidth`, `Opacity` (0–100), `DropShadowEnabled`, `DashType` | Styling |
| `RotationAngle` | Degrees, **clockwise**, around the box centre |
| `ToolShape` | `Rectangle`, `RoundedRectangle`, `Ellipse` |
| `ObjectID` | GUID |

Array order is z-order (first = bottom).

| `ToolMode` | Notes → PortaSnap mapping |
|---|---|
| `Arrow` | `ArrowStart`/`ArrowEnd`: `TaperArrow` (tapered shaft + head), `Flat` (no head), `Round` (rounded cap, *not* a ball). Both ends flat ⇒ a line. |
| `Shape` | Foreground = outline, Background = fill. |
| `Text` | Text is Base64 RTF in `RTFEncodedText` (colour/font/bold come from the RTF; `ForegroundColor` is not the text colour). `ToolHorizontalAlign`/`ToolVerticalAlign`. `BackgroundColor` is not drawn. |
| `Callout` | Body from `PointsArray`; `CalloutTails` = absolute `"x,y"` of the tail tip; `CalloutShape` e.g. `CTBasicSpeechBubble2`; text in RTF. |
| `Step` | **Background = badge fill, Foreground = number colour.** `PlainText` = label, `StepStyle` (`Circle`…), `StepSequenceType`. `Image` holds a pre-rendered PDF badge (ignored). |
| `Highlight` | Foreground colour; honours `Opacity`. |
| `Magnify` | `MagnifyScale` (percent), `ToolShape`, `MagnifyConnectorType`. |
| `Pen` | Freehand points; `PenShape`, `Smoothing`. |
| `Image`, `DropZone` | `Image` is Base64 PNG (imported) or PDF (skipped). Drop zones also have `ImagePosition`/`ImageSize` (crop inside the zone; PortaSnap currently stretches to the zone). |

Objects PortaSnap can't represent are skipped and counted in the import summary/log.
