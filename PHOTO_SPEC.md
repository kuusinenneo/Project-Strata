This repo will contain two apps: Strata Video (the existing Python/PySide6 video editor) and Strata Photo (a new C# image editor). Work in the phases below, in order. Save this full prompt as PHOTO_SPEC.md first, and track progress in PROGRESS.md.

PHASE 1: RESTRUCTURE THE REPO
- Move the existing video editor into a /video folder
- Rename it from "Strata" to "Strata Video" everywhere: window title, exe name, installer name, README
- Create a /shared folder with the design system both apps use: color palette, font choices, spacing and an icon set, stored as a simple JSON or similar file plus the icon files
- Make Strata Video read its theme colors from /shared
- Update the root README.md to describe both apps and how to run and build each one
- Test that Strata Video still runs from source and that its build script still produces a working exe before moving on. Commit.

PHASE 2: BUILD STRATA PHOTO in /photo
Tech: C# on .NET 8 or newer, Avalonia for the UI, SkiaSharp for rendering, Dock.Avalonia for dockable panels. Check that the .NET SDK is installed first.

Solution structure:
- StrataPhoto.Core: class library with no UI code. Document model, layers, blend modes, masks, selections, tools logic, adjustments, filters, undo/redo history, file loading and saving
- StrataPhoto.App: the Avalonia interface, which only uses Core
- StrataPhoto.Tests: unit tests for Core

Layout (Photoshop style, own icons and styling, no Adobe branding, matching Strata Video's look from /shared):
- Top: menu bar and an options bar showing settings for the active tool (brush size, hardness, opacity and so on)
- Left: vertical tools strip
- Center: canvas with tabs for multiple open documents, zoom and pan, rulers
- Right: dockable panels for Layers, Adjustments, Properties, History, Color and Swatches
- Bottom: status bar with zoom level, document size and cursor position
- All panels dockable, plus "Reset layout"

Features:
- New document, open PNG, JPG, WebP, BMP, and save layered projects in its own format
- Layers: add, delete, duplicate, reorder, rename, hide, lock, opacity, blend modes (normal, multiply, screen, overlay and the other common ones), layer masks, grouping
- Tools: move, brush, eraser, fill bucket, gradient, eyedropper, clone stamp, text, shapes (rectangle, ellipse, line), crop, hand, zoom
- Brush settings: size, hardness, opacity, flow, with pressure support for drawing tablets
- Selections: rectangle, ellipse, lasso, magic wand, select all, deselect, invert, feather, and copy/cut/paste to new layer
- Image: resize image, resize canvas, rotate, flip
- Adjustments: brightness/contrast, levels, curves, hue/saturation, color balance, exposure, black and white, invert
- Filters: gaussian blur, sharpen, noise, pixelate, emboss
- Text layers with font, size, color, alignment, editable after creation
- Undo/redo with a History panel
- Export to PNG, JPG and WebP with quality settings and resize option

Shortcuts (Photoshop-like): V move, B brush, E eraser, G fill, I eyedropper, T text, M marquee, L lasso, W magic wand, C crop, H hand, Z zoom, [ and ] brush size, X swap colors, Ctrl+Z / Ctrl+Shift+Z undo/redo, Ctrl+J duplicate layer, Ctrl+D deselect, Ctrl+T free transform, Ctrl+Plus / Ctrl+Minus zoom

Performance: brushes must feel smooth on 4000x4000 images. Only redraw changed regions and keep rendering on the GPU through Skia.

PHASE 3: BUILD THE STRATA PHOTO EXE
- Publish as a self-contained win-x64 build so it runs on computers without .NET installed
- Add an app icon from /shared
- Add a build script (photo/build.bat) that rebuilds with one command
- Add build output folders to .gitignore
- Test that the built exe launches, then tell me where it is

RULES
- Commit after each working module with a clear message
- Never add Co-Authored-By or any AI attribution to commit messages
- Run and test as you go, and fix errors before telling me something is done
