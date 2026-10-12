# Project Strata — Progress

## Autonomous update (branch `autonomous-update`)

### What existed before this run
- **Strata Video** (`video/`, Python + PySide6 + FFmpeg): dockable Premiere-style UI (Source/Program
  monitors, Project/Effects panels, timeline, tools, meters); unlimited video/audio tracks with
  lock/solo/mute; insert/overwrite, move, trim by dragging edges, razor (C), ripple delete, snapping;
  snapshot-based undo/redo (Ctrl+Z / Ctrl+Shift+Z) for every edit; JSON projects; J/K/L, Space, I/O,
  V/C/B/T shortcuts; per-clip volume, fades, cross dissolve, scale/position with a start→end zoom/pan,
  brightness/contrast/saturation/B&W/sepia/blur/opacity, speed (forward only); text overlays with
  presets; waveforms; linked audio/video with unlink; MP4 export dialog with progress and cancel;
  PyInstaller build with self-test. 51 tests.
- **Strata Photo** (`photo/`, C# .NET 10 + Avalonia + SkiaSharp + Dock): everything in Priority 2 already
  exists (undo + History panel, full Layers panel with 17 blend modes, masks, groups, `.sphoto` projects,
  PNG/JPG/WebP export, all selection tools + paste to new layer, crop/resize/canvas/rotate/flip,
  8 adjustments incl. curves, brush/eraser with size/hardness/opacity/flow/pressure, text tool,
  all listed shortcuts). 53 unit tests + `--self-test`.
- **Shared** (`shared/`): theme.json, SVG icons, app logos.

### Done
- Video: autosave every 2 minutes + crash recovery on next launch; projects now save as `.strata`
  (old `.strata.json` files still open)
- Video: Dip to Black and Wipe transitions (Effects panel + transition style in Effect Controls; cross dissolve already existed)
- Video: keyframes for scale, position, rotation and opacity (plus a new Rotation control); diamonds on timeline clips; keyframes stay put when clips are split or trimmed
- Video: 3D .cube LUT loading with strength (Effect Controls → Color & Blur); brightness/contrast/saturation already existed
- Video: reverse playback for clips (video + linked audio; Effect Controls checkbox and Effects-panel item). Slow motion / fast forward already existed (0.1x-16x)
- Video: export presets (YouTube 1080p, TikTok/Reels/Shorts 1080x1920, Instagram square 1080x1080) and fixed-frame resolutions; progress bar and cancel already existed

### In progress
- (none)

### Blocked
- (none)

### Notes for you
- Undo/redo: the spec asks for QUndoStack with one command class per action. Strata Video already has a
  working, tested snapshot undo stack covering every edit (cut, move, trim, delete, add, effects), so per
  the ground rules I kept it instead of rewriting it.
- "Overlay tracks": upper video tracks already act as overlay tracks (V2+ are composited over V1, text
  clips live on video tracks), so no separate track type was added.

---

## Earlier work

Spec: [PHOTO_SPEC.md](PHOTO_SPEC.md). Strata Video's own history: [video/PROGRESS.md](video/PROGRESS.md).

## Phase 1: restructure the repo — done
- [x] PHOTO_SPEC.md saved
- [x] Video editor moved into `video/` (code, tests, spec, build files, licenses)
- [x] Renamed to "Strata Video": window title, About menu, exe `StrataVideo.exe` in `video\dist\StrataVideo\`
      (exe version info "Strata Video"), crash-log folder, project-file app tag, READMEs.
      There is no separate installer; the PyInstaller output folder is the distributable.
- [x] `shared/`: `theme.json` (colours, fonts, spacing, icon conventions), 54 SVG UI/tool icons,
      Strata Video + Strata Photo app logos (.svg/.ico/.png), icon generator
- [x] Strata Video reads colours/fonts/spacing from `shared/theme.json` (stylesheet, palette,
      custom-painted widgets, icons); the exe bundles the theme and icon
- [x] Root README describes both apps; `video/README.md`, `shared/README.md`
- [x] Verified: 51 tests pass, runs from source, `build.bat` builds, self-test passes, exe opens
      with title "Untitled — Strata Video"

## Phase 2: Strata Photo — done
- [x] .NET 10 SDK installed (only 6.0 was present); solution in `photo/` with StrataPhoto.Core (no UI),
      StrataPhoto.App (Avalonia 11.3, uses only Core), StrataPhoto.Tests (xUnit); SkiaSharp 2.88, Dock.Avalonia 11.3
- [x] Core: document model, pixel/text/group layers, 17 blend modes, layer masks, region-based compositor,
      brush engine (size, hardness, opacity, flow, pressure), selections (rect, ellipse, lasso, magic wand,
      add/subtract/intersect, invert, feather), tools logic (move, marquees, lasso, wand, crop, eyedropper,
      brush, eraser, clone stamp, fill, gradient, shapes, free transform), adjustments (8), filters (5),
      image size / canvas size / rotate / flip / crop, undo history, PNG/JPEG/WebP/BMP I/O, `.sphoto` projects,
      export with quality and resize, text rendering with font fallback
- [x] App: menu bar, options bar, tools strip, tabbed canvas with rulers, zoom and pan, dockable Layers /
      Adjustments / Properties / History / Color / Swatches panels, status bar, Reset Layout, dialogs,
      Photoshop-like shortcuts, drag & drop, unsaved-changes prompts; styled from `shared/theme.json` + shared icons
- [x] Performance: GPU tile cache (only dirty 256 px tiles re-uploaded), dirty-rect compositing and
      zero-copy Skia drawing; 4000×4000 brush ≈ 0.2 ms/event in Core, ≈ 1–3 ms/event in the running app
- [x] Verified: 53 unit tests; `--self-test` drives the real UI (all tools, layers, masks, text, adjustments,
      filters, transform, pointer events through the canvas, every dialog, Reset Layout, save/reopen,
      export) and confirms GPU rendering

## Phase 3: Strata Photo exe — done
- [x] `photo/build.bat`: unit tests → self-contained single-file win-x64 publish (ReadyToRun) → self-test
- [x] App icon from `shared/icons/app/strata-photo.ico` (exe and window)
- [x] Build outputs (`bin/`, `obj/`, `publish/`) in `.gitignore`
- [x] Built exe passes its self-test and starts with no .NET on PATH: `photo\publish\StrataPhoto\StrataPhoto.exe`

## Known limitations / possible next steps
- Adjustment layers (adjustments and filters are currently applied to the active layer)
- Editable vector shape layers (shapes are drawn as pixels on a new layer)
- "Pass Through" group blending, PSD import/export
- Move the layers-panel drag & drop to Avalonia 11.3's new DataTransfer API (the old API is obsolete but works)
