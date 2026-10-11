# Project-Strata
Video Editor

**Strata** is a desktop video editor written in Python with PySide6 and FFmpeg.
It has a dockable, dark, multi-panel layout (Source / Program monitors, Project and Effects
panels, a multi-track timeline, tools strip and audio meters), effects, transitions,
text overlays, undo/redo, JSON project files and MP4 (H.264 + AAC) export.

## Requirements

- Windows 10/11, macOS or Linux
- Python 3.10+ (developed on Python 3.13)
- FFmpeg — nothing to install. The packaged app ships its own copy. From source, Strata
  uses `ffmpeg` on your PATH if present, otherwise the FFmpeg binary bundled with the
  `imageio-ffmpeg` package (installed by `requirements.txt`). To use a
  system FFmpeg on Windows: `winget install Gyan.FFmpeg`. You can also point Strata to a
  specific binary with the `STRATA_FFMPEG` environment variable.

## Setup

```powershell
# from the project folder
python -m venv .venv
.venv\Scripts\activate          # macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
```

## Run

```powershell
python main.py                    # or: python -m strata
python main.py path\to\project.strata.json   # open a project directly
```

## Build a Windows app (.exe)

```powershell
.uild.bat           # build + self-test  ->  dist\Strata\Strata.exe
.uild.bat notest    # build only
```

`build.bat` creates the venv if needed, installs `requirements-build.txt` (adds
PyInstaller), builds with `strata.spec` and then runs `Strata.exe --self-test`, which
checks FFmpeg, decoding, an export and the audio device inside the packaged app.

The result is a folder, `dist\Strata\`, that you can zip or copy to another Windows
PC; run `Strata.exe` inside it. It is a windowed app (no console) in PyInstaller's
onedir mode for fast startup, and it already contains Python, Qt and FFmpeg, so the
other PC needs nothing installed. FFmpeg's license notice and the GPLv3 text are in
`dist\Strata\licenses\`. Unexpected errors in the packaged app are shown in a dialog
and logged to `%LOCALAPPDATA%\Strata\strata-errors.log`.

To regenerate the app icon (`assets/strata.ico`): `python tools\make_icon.py`.

## Run the tests

```powershell
pip install -r requirements-dev.txt
python -m pytest
```

The tests generate small synthetic clips with FFmpeg and cover the model, decoding,
rendering, export and headless UI interactions.

## Using Strata

1. **Import** media with *File → Import Media* (Ctrl+I), the import button in the Project
   panel, or by dragging files from Explorer into the Project panel or the timeline.
2. **Preview** a clip by double-clicking it: it opens in the Source monitor where you can set
   In/Out points (I / O) and insert (,) or overwrite (.) it at the playhead, or drag the
   picture to the timeline.
3. **Edit** on the timeline: drag clips from the Project panel (Ctrl+drop inserts and ripples),
   move them between tracks and in time, trim by dragging clip edges, cut with the razor
   (C) or at the playhead (Ctrl+K), ripple delete (Shift+Delete or the ripple tool), join
   clips to close gaps (Ctrl+J). Snapping (S) snaps to clip edges and the playhead.
   **Separating audio and video:** a video's picture and sound are linked and move together.
   Alt+click selects just one part (then Delete removes only that part), right-click →
   *Unlink audio and video* (Ctrl+L) splits them permanently, and the chain button in the
   timeline toolbar turns linked selection off entirely. Ctrl+L on a selected video + audio
   clip links them again.
4. **Tracks**: every track header has visibility/mute, lock and solo toggles, a **+** button
   to add a track (video tracks stack on top, audio tracks go below) and a **×** button to
   remove an empty track. Upper video tracks are drawn over lower ones; all audio tracks
   are mixed together.
5. **Effects & transitions**: drag items from the Effects tab onto a clip, then fine-tune in
   Effect Controls: scale, position, gradual zoom/pan, opacity, speed, volume, brightness,
   contrast, saturation, black & white, sepia, blur, fade in/out and cross dissolve.
6. **Text**: use the text tool (T), drag a text preset from the Effects tab, or
   *Clip → Add Text at Playhead*. Pick a layout preset (centered title, lower third, top
   caption, subtitle), font, size, colour, background box and fades in Effect Controls.
7. **Export** with *File → Export Media* (Ctrl+M): choose frame rate, resolution,
   quality (CRF presets or a custom bitrate), encoding speed and audio bitrate. The
   estimated file size updates as you change settings, and the progress bar shows the
   time remaining with a cancel button.

Panels can be dragged, tabbed, floated and closed; *Window → Reset Layout* restores the
default arrangement. *Help → Keyboard Shortcuts* (F1) lists every shortcut.

### Keyboard shortcuts

| Key | Action |
| --- | --- |
| V / C / B / T | Selection / razor / ripple delete / text tool |
| Space | Play / pause the active monitor |
| J / K / L | Play backward / stop / play forward (press again for 2×, 4×, 8×) |
| I / O | Mark in / out (source clip or sequence) |
| ← / → , Home / End | Step one frame, go to start / end |
| Delete / Shift+Delete | Remove clip / ripple delete |
| Ctrl+K | Cut at playhead |
| Alt+click | Select only the audio or only the video of a linked clip |
| Ctrl+L / Ctrl+Shift+L | Link/unlink audio and video / toggle linked selection |
| Ctrl+Z / Ctrl+Shift+Z | Undo / redo |
| + / − / \\ | Timeline zoom in / out / fit |
| S | Toggle snapping |

## Project structure

```
strata/
  model/      project data model, editing operations, undo stack, JSON I/O (no Qt)
  media/      FFmpeg discovery, probing, video/image decoding (PyAV), audio decoding
  render/     compositor (layers, transforms, transitions), effects, text, audio mixer
  export/     export settings and the FFmpeg MP4 pipeline
  ui/         main window, controller, monitors, timeline, panels, dialogs, theme, icons
tests/        pytest suite
assets/       app icon
licenses/     third-party license texts shipped with the app
tools/        helper scripts (icon generator)
strata.spec   PyInstaller build definition (used by build.bat)
```

Preview and export share one renderer, so the exported video matches what the Program
monitor shows. Monitor frames are rendered on a background thread: while you scrub, the
nearest keyframe is shown immediately and replaced with the exact frame as soon as you
pause, so the timeline stays responsive even with long-GOP or 4K footage.
