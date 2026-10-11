# Strata — Specification

Build a desktop video editor called Strata in Python using PySide6 (not PyQt6) and FFmpeg. Work file by file, test as you go, and run the app to check for errors before telling me it's done.

## LAYOUT (Premiere Pro style, own icons and styling, no Adobe branding)
- Top left: Source monitor for previewing a clip and setting in/out points, with an Effect Controls tab next to it
- Top right: Program monitor showing the edited sequence with playback controls
- Bottom left: Project panel with imported media, plus an Effects tab for dragging effects and transitions onto clips
- Bottom center: Timeline with a time ruler, playhead and zoom slider. Starts with video tracks V1, V2, V3 and audio tracks A1, A2
- Narrow tools strip next to the timeline: selection, razor, ripple delete, text
- Far right: audio level meters
- All panels dockable with QDockWidget, plus a "Reset layout" option
- Dark theme

## TRACKS
- Track headers have a "+" button to add a new video or audio track and a way to remove empty tracks
- No limit on the number of tracks. New video tracks stack above existing ones, new audio tracks go below
- Each track header shows its name (V1, V2, A1...), plus toggles for visibility/mute, lock and solo
- Multiple video and audio clips can overlap in time on different tracks. Overlapping video layers stack with upper tracks on top, and overlapping audio clips play simultaneously and are mixed together

## FEATURES
- Import video, audio and image files into the Project panel
- Drag clips from Project to timeline, move them between tracks and in time
- Cut clips with the razor tool or at the playhead, trim by dragging clip edges
- Join clips into one sequence, ripple delete, snapping to clip edges and playhead
- Zoom and pan per clip (scale, position X/Y), including a gradual zoom in/out over the clip's duration
- Effects: brightness, contrast, saturation, black and white, sepia, blur, opacity, playback speed
- Transitions: fade in/out and cross dissolve between clips
- Text overlays with layout presets (centered title, lower third, top caption, subtitle), font, size, color, background box, fade in/out
- Per-clip volume
- Undo/redo
- Save and load projects as JSON

## EXPORT
Export to MP4 (H.264 + AAC) through FFmpeg with an export dialog before rendering:
- Frame rate: 24, 25, 30, 50, 60 fps, or match source
- Resolution: 720p, 1080p, 1440p, 4K, or match sequence
- Quality presets: Low, Medium, High, Very high (mapped to CRF values), plus a custom bitrate option
- Encoding speed: Fast, Balanced, Best (mapped to FFmpeg presets)
- Audio bitrate: 128, 192, 320 kbps
- Estimated file size shown before export
- Progress bar with time remaining and a cancel button

## SHORTCUTS
V selection, C razor, I/O in and out points, J/K/L playback, Space play/pause, Delete remove clip, Shift+Delete ripple delete, Ctrl+Z / Ctrl+Shift+Z undo/redo, +/- timeline zoom

## STRUCTURE
Split into sensible modules (main window, panels, timeline, preview/playback, effects, text, export, project model). Keep the project data model separate from the UI.

## SETUP
Check my OS and whether Python and FFmpeg are installed. Create a virtual environment and requirements.txt, and update the existing README.md with setup and run instructions.

## PROGRESS AND GIT
- Save this full spec as SPEC.md in the project root first
- Keep a PROGRESS.md file in the project root that lists what's done, what's in progress and what's left. Update it after finishing each module
- Commit to git after each working module with a clear commit message
