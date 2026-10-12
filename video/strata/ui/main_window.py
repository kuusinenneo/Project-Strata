"""Main window: dock layout, menus, shortcuts and wiring between panels."""
from __future__ import annotations

import os
from typing import Optional

from PySide6.QtCore import QSettings, Qt, QTimer
from PySide6.QtGui import QAction, QIcon, QKeySequence
from PySide6.QtWidgets import (QComboBox, QDialog, QDialogButtonBox, QDockWidget,
                               QFileDialog, QFormLayout, QMainWindow, QMessageBox,
                               QSpinBox, QTabWidget, QTextBrowser, QVBoxLayout, QWidget)

from .. import APP_NAME, __version__
from ..media.ffmpeg import ffmpeg_version, find_ffmpeg
from ..model.autosave import AutosaveManager
from ..model.io import PROJECT_EXTENSION
from ..render.mixer import AudioMixer
from ..resources import app_icon_path, user_data_dir
from .controller import EditorController
from .effect_controls import EffectControls
from .effects_panel import EffectsPanel
from .export_dialog import ExportDialog
from .icons import icon
from .meters import AudioMeters
from .monitors import MonitorPanel, ProgramMonitor, SourceMonitor
from .playback import AudioOutput
from .project_panel import ProjectPanel
from .timeline import TimelinePanel
from .tools_panel import ToolsPanel

AUTOSAVE_INTERVAL_MS = 2 * 60 * 1000
PROJECT_FILTER = f"Strata Video project (*{PROJECT_EXTENSION} *.strata.json *.json)"

SHORTCUTS_HELP = """
<h3>Tools</h3>
<table cellpadding=3>
<tr><td><b>V</b></td><td>Selection tool</td></tr>
<tr><td><b>C</b></td><td>Razor tool (Shift+click cuts all tracks)</td></tr>
<tr><td><b>B</b></td><td>Ripple delete tool</td></tr>
<tr><td><b>T</b></td><td>Text tool</td></tr>
</table>
<h3>Playback (active monitor)</h3>
<table cellpadding=3>
<tr><td><b>Space</b></td><td>Play / pause</td></tr>
<tr><td><b>J / K / L</b></td><td>Play backward (repeat for faster) / stop / play forward (repeat for faster)</td></tr>
<tr><td><b>I / O</b></td><td>Mark in / out</td></tr>
<tr><td><b>Left / Right</b></td><td>Previous / next frame</td></tr>
<tr><td><b>Home / End</b></td><td>Go to start / end</td></tr>
<tr><td><b>, / .</b></td><td>Insert / overwrite source clip at playhead</td></tr>
</table>
<h3>Editing</h3>
<table cellpadding=3>
<tr><td><b>Delete</b></td><td>Remove selected clips</td></tr>
<tr><td><b>Shift+Delete</b></td><td>Ripple delete selected clips</td></tr>
<tr><td><b>Ctrl+K</b></td><td>Cut at playhead</td></tr>
<tr><td><b>Ctrl+J</b></td><td>Join selected clips (close gaps)</td></tr>
<tr><td><b>Ctrl+L</b></td><td>Link / unlink the selected audio and video</td></tr>
<tr><td><b>Ctrl+Shift+L</b></td><td>Toggle linked selection</td></tr>
<tr><td><b>Alt+click</b></td><td>Select only the audio or only the video part of a linked clip</td></tr>
<tr><td><b>Ctrl+Z / Ctrl+Shift+Z</b></td><td>Undo / redo</td></tr>
<tr><td><b>Ctrl+A / Ctrl+Shift+A</b></td><td>Select all / deselect</td></tr>
<tr><td><b>S</b></td><td>Toggle snapping</td></tr>
<tr><td><b>+ / - / \\</b></td><td>Timeline zoom in / out / fit</td></tr>
<tr><td><b>Ctrl+Wheel</b></td><td>Zoom timeline; Shift+Wheel scrolls horizontally</td></tr>
<tr><td><b>Ctrl+drop</b></td><td>Insert (ripple) instead of overwrite when dropping media</td></tr>
</table>
<h3>Project</h3>
<table cellpadding=3>
<tr><td><b>Ctrl+N / O / S</b></td><td>New / open / save project</td></tr>
<tr><td><b>Ctrl+I</b></td><td>Import media</td></tr>
<tr><td><b>Ctrl+M</b></td><td>Export media</td></tr>
</table>
"""


class SequenceSettingsDialog(QDialog):
    PRESETS = [("Custom", None), ("1080p HD (1920×1080)", (1920, 1080)), ("720p HD (1280×720)", (1280, 720)),
               ("1440p (2560×1440)", (2560, 1440)), ("4K UHD (3840×2160)", (3840, 2160)),
               ("Vertical 1080×1920", (1080, 1920)), ("Square 1080×1080", (1080, 1080))]

    def __init__(self, seq, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Sequence Settings")
        self.preset = QComboBox()
        for name, size in self.PRESETS:
            self.preset.addItem(name, size)
        self.w = QSpinBox()
        self.h = QSpinBox()
        for sb in (self.w, self.h):
            sb.setRange(16, 8192)
            sb.setSingleStep(2)
        self.w.setValue(seq.width)
        self.h.setValue(seq.height)
        self.fps = QComboBox()
        self.fps.setEditable(True)
        for f in ("23.976", "24", "25", "29.97", "30", "50", "59.94", "60"):
            self.fps.addItem(f)
        self.fps.setCurrentText(f"{seq.fps:g}")
        self.preset.currentIndexChanged.connect(self._preset)
        form = QFormLayout(self)
        form.addRow("Preset", self.preset)
        form.addRow("Width", self.w)
        form.addRow("Height", self.h)
        form.addRow("Frame rate", self.fps)
        bb = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        bb.accepted.connect(self.accept)
        bb.rejected.connect(self.reject)
        form.addRow(bb)

    def _preset(self) -> None:
        size = self.preset.currentData()
        if size:
            self.w.setValue(size[0])
            self.h.setValue(size[1])

    def values(self) -> tuple[int, int, float]:
        try:
            fps = float(self.fps.currentText())
        except ValueError:
            fps = 30.0
        return self.w.value(), self.h.value(), max(1.0, min(240.0, fps))


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.controller = EditorController(self)
        self.settings = QSettings("Strata", APP_NAME)
        ico = app_icon_path()
        self.setWindowIcon(QIcon(str(ico)) if ico.is_file() else icon("logo"))
        self.resize(1600, 960)
        self.setDockNestingEnabled(True)
        self.setDockOptions(QMainWindow.AllowNestedDocks | QMainWindow.AllowTabbedDocks
                            | QMainWindow.AnimatedDocks)
        self.setTabPosition(Qt.AllDockWidgetAreas, QTabWidget.North)
        # Every panel is a dock; the central widget is only an invisible anchor.
        placeholder = QWidget()
        placeholder.setMaximumSize(0, 0)
        self.setCentralWidget(placeholder)

        c = self.controller
        self.audio = AudioOutput(c.audio_store.sample_rate, self)
        self.mixer = AudioMixer(c.project, c.audio_store)

        self.source = SourceMonitor(c, self.audio)
        self.program = ProgramMonitor(c, self.audio, self.mixer)
        self.effect_controls = EffectControls(c)
        self.project_panel = ProjectPanel(c)
        self.effects_panel = EffectsPanel()
        self.timeline = TimelinePanel(c)
        self.tools = ToolsPanel(c)
        self.meters = AudioMeters()
        self.audio.levels.connect(self.meters.set_levels)

        self.d_source = self._dock("Source", self.source, "dock_source")
        self.d_effect_controls = self._dock("Effect Controls", self.effect_controls, "dock_effect_controls")
        self.d_program = self._dock("Program", self.program, "dock_program")
        self.d_project = self._dock("Project", self.project_panel, "dock_project")
        self.d_effects = self._dock("Effects", self.effects_panel, "dock_effects")
        self.d_timeline = self._dock("Timeline", self.timeline, "dock_timeline")
        self.d_tools = self._dock("Tools", self.tools, "dock_tools")
        self.d_meters = self._dock("Audio", self.meters, "dock_meters")
        self.docks = [self.d_source, self.d_effect_controls, self.d_program, self.d_project,
                      self.d_effects, self.d_tools, self.d_timeline, self.d_meters]

        self.active_monitor: MonitorPanel = self.program
        self.source.activated.connect(self._set_active_monitor)
        self.program.activated.connect(self._set_active_monitor)
        self.timeline.canvas.installEventFilter(self)
        self.project_panel.open_in_source.connect(self._open_in_source)
        self.source.insert_requested.connect(lambda mid: self._source_edit(mid, True))
        self.source.overwrite_requested.connect(lambda mid: self._source_edit(mid, False))
        self.timeline.clip_activated.connect(self._show_effect_controls)

        self._build_actions()
        self._build_menus()
        self.apply_default_layout()
        self._set_active_monitor(self.program)

        self.statusBar().showMessage("Ready — import media with Ctrl+I or drag files into the Project panel")
        c.message.connect(lambda m: self.statusBar().showMessage(m, 5000))
        c.history_changed.connect(self._update_title)
        c.history_changed.connect(self._update_undo_actions)
        c.project_replaced.connect(self._update_title)
        self._update_title()
        self._update_undo_actions()

        geo = self.settings.value("geometry")
        if geo is not None:
            self.restoreGeometry(geo)

        # Autosave every couple of minutes while there are unsaved changes.
        self.autosave = AutosaveManager(os.environ.get("STRATA_AUTOSAVE_DIR")
                                        or str(user_data_dir() / "autosave"))
        self._autosaved_state = None
        self.autosave_timer = QTimer(self)
        self.autosave_timer.setInterval(AUTOSAVE_INTERVAL_MS)
        self.autosave_timer.timeout.connect(self.autosave_now)
        self.autosave_timer.start()
        self.controller.use_proxies = self.settings.value("use_proxies", True, type=bool)
        self.a_proxies.setChecked(self.controller.use_proxies)

    # ------------------------------------------------------------------ docks
    def _dock(self, title: str, widget: QWidget, name: str) -> QDockWidget:
        d = QDockWidget(title, self)
        d.setObjectName(name)
        d.setWidget(widget)
        d.setFeatures(QDockWidget.DockWidgetMovable | QDockWidget.DockWidgetFloatable
                      | QDockWidget.DockWidgetClosable)
        d.setAllowedAreas(Qt.AllDockWidgetAreas)
        return d

    def apply_default_layout(self) -> None:
        """Source+Effect Controls | Program on top; Project+Effects | Tools | Timeline | Meters below."""
        for d in self.docks:
            if d.isFloating():
                d.setFloating(False)
            self.removeDockWidget(d)
        self.addDockWidget(Qt.TopDockWidgetArea, self.d_source)
        self.addDockWidget(Qt.TopDockWidgetArea, self.d_program)
        self.splitDockWidget(self.d_source, self.d_program, Qt.Horizontal)
        self.tabifyDockWidget(self.d_source, self.d_effect_controls)
        self.addDockWidget(Qt.BottomDockWidgetArea, self.d_project)
        self.splitDockWidget(self.d_project, self.d_tools, Qt.Horizontal)
        self.splitDockWidget(self.d_tools, self.d_timeline, Qt.Horizontal)
        self.splitDockWidget(self.d_timeline, self.d_meters, Qt.Horizontal)
        self.tabifyDockWidget(self.d_project, self.d_effects)
        for d in self.docks:
            d.show()
        self.d_source.raise_()
        self.d_project.raise_()
        QTimer.singleShot(0, self._default_sizes)

    def _default_sizes(self) -> None:
        w, h = max(800, self.width()), max(600, self.height())
        self.resizeDocks([self.d_source, self.d_program], [w // 2, w // 2], Qt.Horizontal)
        self.resizeDocks([self.d_project, self.d_tools, self.d_timeline, self.d_meters],
                         [int(w * 0.24), 44, int(w * 0.70), 70], Qt.Horizontal)
        self.resizeDocks([self.d_source, self.d_project], [int(h * 0.5), int(h * 0.5)], Qt.Vertical)

    # ------------------------------------------------------------------ actions
    def _act(self, text: str, slot, shortcut=None, icon_name: Optional[str] = None,
             checkable: bool = False) -> QAction:
        a = QAction(text, self)
        if icon_name:
            a.setIcon(icon(icon_name))
        if shortcut is not None:
            if isinstance(shortcut, (list, tuple)):
                a.setShortcuts([QKeySequence(s) for s in shortcut])
            else:
                a.setShortcut(QKeySequence(shortcut))
        a.setCheckable(checkable)
        a.triggered.connect(slot)
        self.addAction(a)  # shortcuts work no matter which panel has focus
        return a

    def _build_actions(self) -> None:
        c = self.controller
        A = self._act
        self.a_new = A("&New Project", self.new_project, QKeySequence.New)
        self.a_open = A("&Open Project…", self.open_project, QKeySequence.Open)
        self.a_save = A("&Save Project", self.save_project, QKeySequence.Save)
        self.a_save_as = A("Save Project &As…", self.save_project_as, "Ctrl+Shift+S")
        self.a_import = A("&Import Media…", self.project_panel.import_dialog, "Ctrl+I", "import")
        self.a_export = A("&Export Media…", self.export_media, "Ctrl+M", "export")
        self.a_quit = A("E&xit", self.close, "Ctrl+Q")

        self.a_undo = A("&Undo", c.do_undo, "Ctrl+Z")
        self.a_redo = A("&Redo", c.do_redo, ["Ctrl+Shift+Z", "Ctrl+Y"])
        self.a_delete = A("&Delete", self.delete_pressed, QKeySequence(Qt.Key_Delete))
        self.a_ripple = A("Ripple Delete", lambda: c.delete_selection(ripple=True), "Shift+Del")
        self.a_select_all = A("Select &All", c.select_all, "Ctrl+A")
        self.a_deselect = A("Deselect All", c.clear_selection, "Ctrl+Shift+A")

        self.a_cut = A("Cut at Playhead", lambda: c.cut_at(c.playhead) or c.message.emit("No clip under the playhead"), "Ctrl+K", "razor")
        self.a_join = A("Join Selected Clips (close gaps)", c.join_selected, "Ctrl+J")
        self.a_link = A("Link / Unlink Audio and Video", c.toggle_link_selected, "Ctrl+L", "link")
        self.a_unlink = A("Unlink Selected", lambda: c.link_selected(False), None, "unlink")
        self.a_linked_sel = A("Linked Selection", c.set_linked_selection, "Ctrl+Shift+L", "link",
                              checkable=True)
        self.a_linked_sel.setChecked(c.linked_selection)
        c.linked_selection_changed.connect(self.a_linked_sel.setChecked)
        self.a_text = A("Add Text at Playhead", lambda: c.add_text(None, c.playhead), "Ctrl+T", "text")

        self.a_tool_select = A("Selection Tool", lambda: c.set_tool("select"), "V", "select")
        self.a_tool_razor = A("Razor Tool", lambda: c.set_tool("razor"), "C", "razor")
        self.a_tool_ripple = A("Ripple Delete Tool", lambda: c.set_tool("ripple"), "B", "ripple")
        self.a_tool_text = A("Text Tool", lambda: c.set_tool("text"), "T", "text")

        self.a_seq_settings = A("Sequence &Settings…", self.sequence_settings)
        self.a_add_v = A("Add Video Track", lambda: c.add_track("video"), None, "plus")
        self.a_add_a = A("Add Audio Track", lambda: c.add_track("audio"), None, "plus")
        self.a_zoom_in = A("Zoom In", self.timeline.zoom_in, ["+", "=", "Ctrl++"])
        self.a_zoom_out = A("Zoom Out", self.timeline.zoom_out, ["-", "Ctrl+-"])
        self.a_zoom_fit = A("Zoom to Fit", self.timeline.zoom_to_fit, "\\")
        self.a_snap = A("Snapping", c.set_snapping, "S", "magnet", checkable=True)
        self.a_snap.setChecked(c.snapping)
        c.snapping_changed.connect(self.a_snap.setChecked)
        self.a_clear_marks = A("Clear In/Out", lambda: self.active_monitor.clear_marks(), "Ctrl+Shift+X")

        self.a_play = A("Play / Pause", lambda: self.active_monitor.toggle_play(), "Space", "play")
        self.a_j = A("Play Backward (J)", lambda: self.active_monitor.shuttle(-1), "J")
        self.a_k = A("Stop (K)", lambda: self.active_monitor.stop(), "K", "stop")
        self.a_l = A("Play Forward (L)", lambda: self.active_monitor.shuttle(1), "L")
        self.a_in = A("Mark In", lambda: self.active_monitor.mark_in(), "I", "mark_in")
        self.a_out = A("Mark Out", lambda: self.active_monitor.mark_out(), "O", "mark_out")
        self.a_prev = A("Previous Frame", lambda: self.active_monitor.step(-1), QKeySequence(Qt.Key_Left))
        self.a_next = A("Next Frame", lambda: self.active_monitor.step(1), QKeySequence(Qt.Key_Right))
        self.a_home = A("Go to Start", lambda: self.active_monitor.go_start(), QKeySequence(Qt.Key_Home))
        self.a_end = A("Go to End", lambda: self.active_monitor.go_end(), QKeySequence(Qt.Key_End))
        self.a_insert = A("Insert Source at Playhead", lambda: self._source_edit(None, True), ",")
        self.a_overwrite = A("Overwrite Source at Playhead", lambda: self._source_edit(None, False), ".")

        self.a_proxies = A("Use &Proxies for Playback", self._toggle_proxies)
        self.a_proxies.setCheckable(True)
        self.a_proxies.setChecked(self.controller.use_proxies)
        self.a_proxies.setToolTip("Play 4K footage from small proxy files (export always uses the originals)")
        self.a_reset_layout = A("&Reset Layout", self.apply_default_layout)
        self.a_shortcuts = A("Keyboard &Shortcuts", self.show_shortcuts, "F1")
        self.a_about = A(f"&About {APP_NAME}", self.show_about)

    def _build_menus(self) -> None:
        mb = self.menuBar()
        m = mb.addMenu("&File")
        for a in (self.a_new, self.a_open, self.a_save, self.a_save_as, None, self.a_import,
                  self.a_export, None, self.a_quit):
            m.addSeparator() if a is None else m.addAction(a)
        m = mb.addMenu("&Edit")
        for a in (self.a_undo, self.a_redo, None, self.a_delete, self.a_ripple, None,
                  self.a_select_all, self.a_deselect):
            m.addSeparator() if a is None else m.addAction(a)
        m = mb.addMenu("&Clip")
        for a in (self.a_cut, self.a_join, None, self.a_link, self.a_unlink, self.a_linked_sel, None,
                  self.a_text):
            m.addSeparator() if a is None else m.addAction(a)
        m = mb.addMenu("&Sequence")
        for a in (self.a_seq_settings, None, self.a_add_v, self.a_add_a, None, self.a_zoom_in,
                  self.a_zoom_out, self.a_zoom_fit, self.a_snap, None, self.a_clear_marks):
            m.addSeparator() if a is None else m.addAction(a)
        m = mb.addMenu("&Tools")
        for a in (self.a_tool_select, self.a_tool_razor, self.a_tool_ripple, self.a_tool_text):
            m.addAction(a)
        m = mb.addMenu("&Playback")
        for a in (self.a_play, self.a_j, self.a_k, self.a_l, None, self.a_in, self.a_out, None,
                  self.a_prev, self.a_next, self.a_home, self.a_end, None, self.a_insert, self.a_overwrite,
                  None, self.a_proxies):
            m.addSeparator() if a is None else m.addAction(a)
        m = mb.addMenu("&Window")
        for d in self.docks:
            m.addAction(d.toggleViewAction())
        m.addSeparator()
        m.addAction(self.a_reset_layout)
        m = mb.addMenu("&Help")
        m.addAction(self.a_shortcuts)
        m.addAction(self.a_about)

    def _update_undo_actions(self) -> None:
        u = self.controller.undo
        self.a_undo.setEnabled(u.can_undo())
        self.a_redo.setEnabled(u.can_redo())
        self.a_undo.setText(f"&Undo {u.undo_label()}".strip())
        self.a_redo.setText(f"&Redo {u.redo_label()}".strip())

    def _update_title(self) -> None:
        p = self.controller.project
        name = os.path.basename(p.path) if p.path else "Untitled"
        dirty = "*" if self.controller.undo.is_dirty() else ""
        self.setWindowTitle(f"{name}{dirty} — {APP_NAME}")

    # ------------------------------------------------------------------ monitors
    def _set_active_monitor(self, monitor: MonitorPanel) -> None:
        self.active_monitor = monitor
        for m in (self.source, self.program):
            m.title_label.setStyleSheet("color:#409cff; font-weight:600;" if m is monitor else "color:#8c9098;")
        if monitor is self.source and self.program.player.playing:
            self.program.player.pause()
        if monitor is self.program and self.source.player.playing:
            self.source.player.pause()

    def eventFilter(self, obj, event):
        if obj is self.timeline.canvas and event.type() == event.Type.FocusIn:
            self._set_active_monitor(self.program)
        return super().eventFilter(obj, event)

    def _open_in_source(self, media_id: str) -> None:
        m = self.controller.project.media.get(media_id)
        if m is None:
            return
        self.source.load_media(m)
        self.d_source.show()
        self.d_source.raise_()
        self._set_active_monitor(self.source)

    def _source_edit(self, media_id: Optional[str], insert: bool) -> None:
        mid = media_id or (self.source.media.id if self.source.media else None)
        if mid is None:
            self.controller.message.emit("Open a clip in the Source monitor first")
            return
        c = self.controller
        track = next((t for t in c.project.sequence.video_tracks if not t.locked), None)
        c.add_media_to_timeline([mid], track.id if track else None, c.playhead, insert=insert)

    def _show_effect_controls(self, clip_id: str) -> None:
        self.d_effect_controls.show()
        self.d_effect_controls.raise_()

    def delete_pressed(self) -> None:
        if self.project_panel.tree.hasFocus():
            self.project_panel.remove_selected()
        else:
            self.controller.delete_selection()

    def _toggle_proxies(self) -> None:
        self.controller.use_proxies = self.a_proxies.isChecked()
        self.settings.setValue("use_proxies", self.controller.use_proxies)
        self.program.request_render()
        self.controller.message.emit("Playing from proxies" if self.controller.use_proxies else "Playing from original media")

    # ------------------------------------------------------------------ autosave / recovery
    def autosave_now(self) -> bool:
        """Write an autosave if the project changed since the last one."""
        undo = self.controller.undo
        if not undo.is_dirty() or undo.state == self._autosaved_state:
            return False
        try:
            self.autosave.save(self.controller.project)
        except Exception as exc:  # never interrupt editing because of autosave
            self.controller.message.emit(f"Autosave failed: {exc}")
            return False
        self._autosaved_state = undo.state
        self.controller.message.emit("Autosaved")
        return True

    def check_recovery(self) -> bool:
        """Offer to restore work from a session that crashed. Returns True if recovered."""
        for cand in self.autosave.find_recoverable():
            r = QMessageBox.question(
                self, "Recover unsaved work",
                f"{APP_NAME} did not close properly last time.\n\nRecover {cand.describe()}?",
                QMessageBox.Yes | QMessageBox.No)
            if r != QMessageBox.Yes:
                self.autosave.discard(cand)
                continue
            try:
                missing = self.controller.recover_project(cand.project_file, cand.original_path)
            except Exception as exc:
                QMessageBox.critical(self, "Recover", f"Could not recover the project:\n{exc}")
                self.autosave.discard(cand)
                continue
            self.autosave.discard(cand)
            self.autosave_now()  # keep protecting the recovered work under this session
            if missing:
                QMessageBox.warning(self, "Missing media",
                                    "These files could not be found and will show as offline:\n\n" + "\n".join(missing))
            self.controller.message.emit("Recovered unsaved work — save it to keep it")
            return True
        return False

    # ------------------------------------------------------------------ project files
    def maybe_save(self) -> bool:
        if not self.controller.undo.is_dirty():
            return True
        r = QMessageBox.question(self, APP_NAME, "Save changes to the current project?",
                                 QMessageBox.Save | QMessageBox.Discard | QMessageBox.Cancel)
        if r == QMessageBox.Save:
            return self.save_project()
        return r == QMessageBox.Discard

    def new_project(self) -> None:
        if self.maybe_save():
            self.source.player.pause()
            self.controller.new_project()

    def open_project(self, path: Optional[str] = None) -> None:
        if not self.maybe_save():
            return
        if not path:
            start = self.settings.value("last_dir", os.path.expanduser("~"))
            path, _ = QFileDialog.getOpenFileName(self, "Open project", start, PROJECT_FILTER)
        if not path:
            return
        try:
            missing = self.controller.open_project(path)
        except Exception as exc:
            QMessageBox.critical(self, "Open project", f"Could not open project:\n{exc}")
            return
        self.settings.setValue("last_dir", os.path.dirname(path))
        if missing:
            QMessageBox.warning(self, "Missing media",
                                "These files could not be found and will show as offline:\n\n" + "\n".join(missing))
        self.controller.message.emit(f"Opened {os.path.basename(path)}")

    def save_project(self) -> bool:
        p = self.controller.project
        if not p.path:
            return self.save_project_as()
        return self._save_to(p.path)

    def save_project_as(self) -> bool:
        start = self.settings.value("last_dir", os.path.expanduser("~"))
        path, _ = QFileDialog.getSaveFileName(self, "Save project", os.path.join(start, "Untitled" + PROJECT_EXTENSION),
                                              PROJECT_FILTER)
        if not path:
            return False
        if not path.lower().endswith((PROJECT_EXTENSION, ".json")):
            path += PROJECT_EXTENSION
        return self._save_to(path)

    def _save_to(self, path: str) -> bool:
        try:
            self.controller.save_project(path)
        except Exception as exc:
            QMessageBox.critical(self, "Save project", f"Could not save project:\n{exc}")
            return False
        self.settings.setValue("last_dir", os.path.dirname(path))
        self.autosave.clear()
        self._autosaved_state = self.controller.undo.state
        self._update_title()
        self.controller.message.emit(f"Saved {path}")
        return True

    # ------------------------------------------------------------------ dialogs
    def export_media(self) -> None:
        if self.controller.project.sequence.duration <= 0:
            QMessageBox.information(self, "Export", "The sequence is empty. Add clips to the timeline first.")
            return
        try:
            find_ffmpeg()
        except FileNotFoundError as exc:
            QMessageBox.critical(self, "Export", str(exc))
            return
        self.program.player.pause()
        self.source.player.pause()
        ExportDialog(self.controller, self).exec()

    def sequence_settings(self) -> None:
        dlg = SequenceSettingsDialog(self.controller.project.sequence, self)
        if dlg.exec() == QDialog.Accepted:
            self.controller.set_sequence_settings(*dlg.values())

    def show_shortcuts(self) -> None:
        dlg = QDialog(self)
        dlg.setWindowTitle("Keyboard Shortcuts")
        dlg.resize(560, 640)
        tb = QTextBrowser()
        tb.setHtml(SHORTCUTS_HELP)
        lay = QVBoxLayout(dlg)
        lay.addWidget(tb)
        bb = QDialogButtonBox(QDialogButtonBox.Close)
        bb.rejected.connect(dlg.reject)
        lay.addWidget(bb)
        dlg.exec()

    def show_about(self) -> None:
        try:
            ff = find_ffmpeg()
        except FileNotFoundError:
            ff = "not found"
        QMessageBox.about(self, f"About {APP_NAME}",
                          f"<h2>{APP_NAME} {__version__}</h2><p>A desktop video editor built with "
                          f"PySide6 and FFmpeg.</p><p><small>FFmpeg: {ff}<br>{ffmpeg_version()}</small></p>")

    # ------------------------------------------------------------------ lifecycle
    def closeEvent(self, e):
        if not self.maybe_save():
            e.ignore()
            return
        self.program.shutdown()
        self.source.shutdown()
        self.controller.proxy_worker.stop()
        self.audio.stop()
        self.settings.setValue("geometry", self.saveGeometry())
        self.autosave_timer.stop()
        self.autosave.clear()  # clean exit: nothing to recover next time
        e.accept()
