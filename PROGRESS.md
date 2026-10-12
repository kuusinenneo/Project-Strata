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
- Video: background 540p proxy generation for 4K+ footage, used by the monitors (toggle in Playback menu); export always uses originals
- Shared: moved Strata Photo's theme loader, icon loader, styles and ValueSlider into shared/dotnet/Strata.Ui so Strata PDF can reuse them (Photo unchanged in behaviour; tests + self-test + published build verified)
- PDF: StrataPdf.Core: workspace model (merge, reorder, rotate, delete, undo/redo), annotations (highlight, pen, text box, shapes/arrows, sticky notes, image signatures), page numbers, watermarks, saving with PDFsharp, extract pages, real redaction (page re-rendered as image with the area burned out), strong compression, form fill, image extraction; 15 tests
- PDF: Strata PDF app (Avalonia, Strata look): continuous viewer with thumbnails, zoom, navigation; merge/split/reorder (drag thumbnails)/rotate/delete; highlight, pen, text, shapes, arrows, sticky notes; drawn or image signatures; form filling; page numbers and watermarks; real redaction; compression; extract images to Strata Photo; undo/redo; save/save as; pdf/build.bat (self-contained exe + self-test)
- Suite: Strata Video → Sequence → Send Frame to Strata Photo (Ctrl+Shift+P): renders the playhead frame at full resolution and opens it in Strata Photo; Strata PDF → File → Extract Images to Strata Photo
- Video: Clip > Remove Silence (dB threshold, minimum length, padding; cuts linked video too and closes gaps) and Clip > Detect Scenes and Cut (PySceneDetect if installed, built-in detector otherwise)
- Suite: Strata launcher (launcher/, Strata.exe: one tile per app, open-a-file routing, drag & drop), root build-all.bat that builds/tests everything into dist\Strata Suite\ (apps find each other there), root README describing all three apps and how to run/build each
- Video: Clip > Auto Subtitles with faster-whisper (local, optional requirements-ai.txt): transcribes audio clips and adds subtitle text clips on a new Subtitles track; tested end to end with synthesised speech

### In progress
- (none)

### Blocked
- **OCR for scanned PDFs (Tesseract)** — not attempted. It needs the Tesseract engine plus language
  data installed separately (and a native wrapper), which I couldn't set up and verify unattended.
  Recommended path: `Tesseract` NuGet package + `tessdata` files, running OCR per page image and
  writing an invisible text layer with PDFsharp.
- Nothing else is blocked; no feature had to be reverted.

### Skipped because it already existed
- Video: undo/redo (snapshot stack, every edit), project save/load (JSON), multiple video/audio tracks
  (upper video tracks act as overlay tracks), trim handles, snapping, ripple delete, J/K/L, C, V,
  I/O and Space shortcuts, waveforms, per-clip volume, fades, detach (unlink) audio, cross dissolve,
  brightness/contrast/saturation, slow motion / fast forward, export progress bar and cancel.
- Photo: all of Priority 2 (History panel, layers + blend modes, `.sphoto` projects, PNG/JPG/WebP
  export, rectangle/ellipse/lasso/magic wand + paste to new layer, crop/resize/canvas/rotate/flip,
  brightness/contrast, hue/saturation, levels, curves, brush/eraser settings, text tool, shortcuts).

### Final summary
16 commits on `autonomous-update` (main untouched). Everything builds, launches and passes its tests:
Strata Video 74 tests, Strata Photo 53, Strata PDF 19, plus each app's `--self-test` of the built exe.
`build-all.bat` was run end to end and produced `dist\Strata Suite\` (launcher + all three apps,
each finding the others).

- **Strata Video:** autosave + crash recovery, `.strata` files, Dip to Black / Wipe transitions,
  keyframes (scale, position, rotation, opacity), `.cube` LUTs, reverse playback, YouTube / TikTok /
  Instagram export presets, background proxies for 4K, Send Frame to Strata Photo, Remove Silence,
  Detect Scenes and Cut, Auto Subtitles.
- **Strata PDF (new, `pdf/`):** viewer with thumbnails, merge / split / reorder / rotate / delete,
  highlight, pen, text, shapes, arrows, sticky notes, signatures, form filling, page numbers,
  watermarks, real redaction, compression, extract images to Strata Photo, undo/redo, save / save as.
  Licences: PDFiumCore (Apache-2.0) + PDFium (BSD), PDFsharp (MIT) — no iText, no PyMuPDF.
- **Suite:** shared C# theme library `shared/dotnet/Strata.Ui` (Photo now uses it too), new icons and
  logos (Strata PDF, suite), Strata launcher (`launcher/`), `build-all.bat`, root README for all apps.

### New dependencies to install
- Strata PDF / launcher: nothing new to install beyond the .NET SDK you already have; NuGet packages
  (PDFiumCore, PDFsharp) are restored automatically.
- Strata Video, optional:
  - `pip install -r video/requirements-ai.txt` (faster-whisper) for Auto Subtitles. The first run
    downloads the speech model. **I installed it into `video/.venv`** to test it, and the test
    downloaded the `tiny` model into your Hugging Face cache (`%USERPROFILE%\.cache\huggingface`).
    The exe leaves it out unless you build with `set STRATA_BUNDLE_AI=1`.
  - `pip install -r video/requirements-extras.txt` (PySceneDetect + OpenCV) to use PySceneDetect for
    scene detection; without it the built-in detector is used.
  - Installing faster-whisper upgraded PyAV in the venv to 19.0.1; all video tests and the exe
    self-test pass with it.

### Notes for you
- Undo/redo: the spec asks for QUndoStack with one command class per action. Strata Video already has a
  working, tested snapshot undo stack covering every edit (cut, move, trim, delete, add, effects), so per
  the ground rules I kept it instead of rewriting it.
- "Overlay tracks": upper video tracks already act as overlay tracks (V2+ are composited over V1, text
  clips live on video tracks), so no separate track type was added.
- Strata PDF started on Docnet.Core but I switched rendering to PDFiumCore: Docnet ties each open
  document to one render scale, and closing cached documents crashed PDFium natively in tests.
  PDFiumCore keeps one document per file and renders at any zoom.
- Strata PDF annotations stay editable while the document is open; saving writes them into the
  pages (sticky notes stay real PDF notes), so reopening a saved PDF shows them flattened.
  Thumbnails show page content without annotations.
- Auto Subtitles in the packaged exe shows the "install faster-whisper" message unless the exe was
  built with `STRATA_BUNDLE_AI=1`; from source it works once `requirements-ai.txt` is installed.
- Strata Video's keyframes are linear; reversed clips decode backwards, which is slower to scrub
  than forward playback (proxies help for large files).

### Recommended next steps
1. Review and merge `autonomous-update` into `main`, then run `build-all.bat` on a clean clone.
2. OCR for Strata PDF (see Blocked).
3. Adjustment layers in Strata Photo; editable (re-openable) annotations in Strata PDF, e.g. as real
   PDF annotation objects rather than flattened content.
4. Bezier easing for Strata Video keyframes and a keyframe lane on the timeline.
5. Code-sign the exes so Windows SmartScreen stops warning on first launch.

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
