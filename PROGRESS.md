# Strata — Progress

## Done
- SPEC.md saved
- Environment: Windows 11, Python 3.13, venv in `.venv`, requirements.txt (FFmpeg via PATH or bundled imageio-ffmpeg), README setup/run instructions
- Project data model, editing ops (overwrite/split/trim/ripple/join/snap), undo/redo, JSON save/load — with tests
- Media layer: FFmpeg discovery, probing, frame-accurate video decoding, image loading, async audio decoding + waveform peaks — with tests
- Rendering: compositor (track stacking, visibility/solo, scale/position + gradual zoom, opacity, fades, cross dissolve), colour effects + blur, text overlays with 4 presets, audio mixer (volume, fades, equal-power crossfades) — with tests
- Export pipeline: settings (fps/resolution/CRF presets/custom bitrate/x264 speed/audio bitrate), size estimate, render→FFmpeg pipe (H.264 + AAC), progress + ETA, cancel — with tests
- UI
  - Dark theme, own QPainter-drawn icon set
  - Main window with all panels as QDockWidgets + Window → Reset Layout
  - Source monitor (in/out marks, insert/overwrite, drag to timeline) with Effect Controls tab
  - Program monitor with transport controls, J/K/L shuttle, sequence in/out
  - Project panel (import, thumbnails, drag to timeline, OS file drops) + Effects tab (effects, transitions, text presets)
  - Timeline: ruler, playhead, zoom slider, track headers (+ / remove-empty, visibility/mute, lock, solo), move/trim/razor/ripple/text tools, snapping, rubber-band selection, waveforms, context menus
  - Tools strip, stereo audio meters with peak hold
  - Export dialog with estimated size, progress, time remaining, cancel
  - Keyboard shortcuts per spec (+ extras: B, T, Ctrl+K, Ctrl+J, arrows, Home/End)
  - Headless UI tests (layout, editing, mouse tools, drag & drop, shortcuts, save/load, export dialog)
- End-to-end check: app launched on Windows with real media, real audio playback and meters verified
- Separate audio/video of linked clips: Alt+click single-part selection, Unlink/Link (Ctrl+L),
  linked-selection toggle (timeline toolbar, Ctrl+Shift+L), "Delete audio/video only" context
  action, Alt+trim of one part, dangling links cleaned up automatically
- Scrubbing performance: monitor frames rendered on a background thread (newest request wins),
  fast keyframe frames while scrubbing with automatic exact refinement, timeline repaints only
  the playhead strips (UI time per scrub event 44 ms → ~8 ms median)

## In progress
- (nothing)

## Left / possible next steps
- Pitch-preserving audio when clip speed ≠ 1 (currently resampled, so pitch shifts)
- Keyframes beyond the start→end gradual zoom/pan
- Proxy files / background frame cache for very large (4K+) sources
