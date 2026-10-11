# Project Strata — Progress

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
