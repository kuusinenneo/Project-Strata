# Strata Photo

**Strata Photo** is a layered image editor for Windows, written in C# with .NET 10, Avalonia,
SkiaSharp and Dock.Avalonia. It has a Photoshop-style layout and takes its colours, fonts and
icons from the shared Strata design system (`../shared`), so it matches Strata Video.

## Running the Windows app (StrataPhoto.exe)

After building (see below) the app is in `photo\publish\StrataPhoto\`:

```
Project-Strata\
  photo\
    publish\
      StrataPhoto\
        StrataPhoto.exe   <- double-click this
        shared\           <- theme and icons (keep next to the exe)
```

- It is self-contained: the other PC does **not** need .NET. To move or share it, copy or zip the
  whole `StrataPhoto` folder.
- Open files directly: `StrataPhoto.exe photo.jpg project.sphoto`, or drag files onto the window.
- The exe isn't code-signed, so Windows may show *"Windows protected your PC"* the first time:
  **More info** → **Run anyway**.
- Unexpected errors are logged to `%LOCALAPPDATA%\Strata Photo\strata-photo-errors.log`.

## Build

Requirements: Windows 10/11 and the .NET SDK 8 or newer (`winget install Microsoft.DotNet.SDK.10`).

```powershell
cd photo
.\build.bat           # unit tests + self-contained publish + self-test  ->  publish\StrataPhoto\StrataPhoto.exe
.\build.bat notest    # publish only
```

`build.bat` runs the unit tests, publishes a single-file, self-contained `win-x64` build
(ReadyToRun, about 60 MB) and then runs `StrataPhoto.exe --self-test`. The self-test starts the
real app, draws with every major tool, works with layers, masks, text, adjustments, filters,
transforms, dialogs and files, times the brush on a 4000×4000 image, checks that the canvas is
drawn on the GPU, and writes `publish\selftest.png` plus `publish\selftest.json`.

## Run from source and test

```powershell
cd photo
dotnet run --project src\StrataPhoto.App          # run the app
dotnet test tests\StrataPhoto.Tests               # 53 unit tests for the Core library
```

## Features

- **Files:** new document (presets, background colour), open PNG / JPEG / WebP / BMP (and GIF),
  save layered projects as `.sphoto`, export PNG / JPEG / WebP with quality, resize and an
  estimated file size.
- **Layers:** add, delete, duplicate, reorder (buttons, Ctrl+[ / Ctrl+], drag and drop), rename
  (double-click), hide, lock, opacity, 17 blend modes (Normal, Darken, Multiply, Color Burn,
  Lighten, Screen, Color Dodge, Linear Dodge, Overlay, Soft Light, Hard Light, Difference,
  Exclusion, Hue, Saturation, Color, Luminosity), layer masks (paint on them, enable/disable,
  apply, delete), groups, merge down, flatten.
- **Tools:** move, rectangular / elliptical marquee, lasso, magic wand, crop, eyedropper, brush,
  eraser, clone stamp, paint bucket, gradient (linear / radial), text, rectangle, ellipse, line,
  hand, zoom; Ctrl+T free transform (move, scale, rotate).
- **Brushes:** size, hardness, opacity, flow, pen-pressure control of size and/or flow.
- **Selections:** add / subtract / intersect, select all, deselect, invert, feather, load layer
  transparency; cut / copy / copy merged / paste as a new layer.
- **Image:** image size, canvas size (with anchor), rotate 90° / 180° / any angle, flip, crop to
  selection.
- **Adjustments** (with live preview, in the Adjustments panel): Brightness/Contrast, Levels,
  Curves, Exposure, Hue/Saturation, Color Balance, Black & White, Invert.
- **Filters** (with live preview): Gaussian Blur, Sharpen (Unsharp Mask), Add Noise, Pixelate,
  Emboss.
- **Text layers:** font, size, colour, alignment, bold/italic; stay editable (click with the Text
  tool, or use the Properties panel); missing characters fall back to a system font.
- **Undo/redo** with a History panel (click a step to go back to it).
- **Layout:** menu bar, options bar for the active tool, tools strip, tabbed documents with
  rulers, zoom and pan, dockable Layers / Adjustments / Properties / History / Color / Swatches
  panels (drag to rearrange, tab or float), status bar with zoom, document size, cursor position
  and colour, and *Window → Reset Layout*.

Keyboard shortcuts follow Photoshop: V M L W C I B E S G T U H Z, `[` `]` brush size, X swap
colours, D default colours, Ctrl+Z / Ctrl+Shift+Z, Ctrl+J, Ctrl+D, Ctrl+T, Ctrl+Plus / Ctrl+Minus,
Ctrl+0 fit, Ctrl+1 100%, Space for a temporary hand. *Help → Keyboard Shortcuts* (F1) lists them all.

## How it is built

```
photo/
  src/StrataPhoto.Core/     no UI code: document model, layers, compositor, brush engine,
                            selections, tools, adjustments, filters, history, file formats
  src/StrataPhoto.App/      Avalonia UI (uses only Core): canvas, panels, dialogs, theme
  tests/StrataPhoto.Tests/  xUnit tests for Core
  build.bat                 one-command build
```

Performance on big images comes from doing only the work a change needs:

- Layer pixels live in Skia raster bitmaps. A brush stroke accumulates dabs in a coverage buffer
  and rebuilds only the rectangle the new dabs touched; the compositor then re-blends only that
  rectangle. Undo stores before/after patches of the touched area only.
- The canvas keeps the composite on the GPU as 256×256 tile textures (via Avalonia's Skia lease,
  Direct3D through ANGLE). After an edit, only the changed tiles are uploaded again; zooming and
  panning just redraw the textures.
- Measured on a 4000×4000 document with three layers: ~0.2 ms per pointer event in Core, under
  1–3 ms per event inside the running app.

## Limitations

- Adjustments and filters are applied to the active layer (there are no adjustment layers yet).
- Shapes are drawn as pixels on a new layer, not as editable vector shapes.
- Groups blend as isolated groups (no "Pass Through" mode); PSD files are not supported.
