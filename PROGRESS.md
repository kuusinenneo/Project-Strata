# Strata — Progress

## Done
- SPEC.md saved
- Environment: Windows 11, Python 3.13, venv in `.venv`, requirements.txt (FFmpeg via PATH or bundled imageio-ffmpeg)
- Project data model, editing ops (overwrite/split/trim/ripple/join/snap), undo/redo, JSON save/load — with tests
- Media layer: FFmpeg discovery, probing, frame-accurate video decoding, image loading, async audio decoding + waveform peaks — with tests
- Rendering: compositor (track stacking, visibility/solo, scale/position + gradual zoom, opacity, fades, cross dissolve), colour effects + blur, text overlays with 4 presets, audio mixer (volume, fades, equal-power crossfades) — with tests

- Export pipeline: settings (fps/resolution/CRF presets/custom bitrate/x264 speed/audio bitrate), size estimate, render→FFmpeg pipe (H.264 + AAC), progress + ETA, cancel — with tests

## In progress
- UI

## Left
- Export dialog (UI)
- UI: theme, icons, main window/docks, project & effects panels, monitors, timeline, tools, meters, effect controls
- Shortcuts, README, end-to-end run check
