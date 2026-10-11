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

## Phase 2: Strata Photo — not started

## Phase 3: Strata Photo exe — not started
