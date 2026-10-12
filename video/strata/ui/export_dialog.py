"""Export dialog: choose settings, see the estimated size, render with progress."""
from __future__ import annotations

import os
import time

from PySide6.QtCore import QThread, QUrl, Signal
from PySide6.QtGui import QDesktopServices
from PySide6.QtWidgets import (QComboBox, QDialog, QDialogButtonBox, QFileDialog, QFormLayout,
                               QGroupBox, QHBoxLayout, QLabel, QLineEdit, QMessageBox,
                               QProgressBar, QPushButton, QSpinBox, QVBoxLayout)

from ..export.exporter import ExportCancelled, Exporter
from ..export.settings import (AUDIO_BITRATES, CUSTOM_BITRATE, EXPORT_PRESETS, FRAME_RATES, QUALITY_CRF,
                               RESOLUTIONS, SPEED_PRESETS, ExportSettings, estimate_size_bytes,
                               export_range, format_size, resolve_fps, resolve_size)
from .playback import timecode


class ExportJob(QThread):
    progress = Signal(int, int, float)
    finished_ok = Signal(str)
    failed = Signal(str)
    cancelled = Signal()

    def __init__(self, exporter: Exporter, parent=None):
        super().__init__(parent)
        self.exporter = exporter

    def run(self):
        try:
            out = self.exporter.run(lambda d, t, eta: self.progress.emit(d, t, eta))
            self.finished_ok.emit(out)
        except ExportCancelled:
            self.cancelled.emit()
        except Exception as exc:
            self.failed.emit(str(exc))


def _fmt_eta(seconds: float) -> str:
    seconds = int(max(0, seconds))
    if seconds >= 3600:
        return f"{seconds // 3600}h {seconds // 60 % 60:02d}m"
    return f"{seconds // 60}:{seconds % 60:02d}"


class ExportDialog(QDialog):
    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Export Media")
        self.setMinimumWidth(520)
        self.controller = controller
        self.project = controller.project
        self.job: ExportJob | None = None
        self._t0 = 0.0

        default_dir = os.path.dirname(self.project.path) if self.project.path else os.path.expanduser("~/Videos")
        if not os.path.isdir(default_dir):
            default_dir = os.path.expanduser("~")
        base = os.path.splitext(os.path.basename(self.project.path or "Sequence"))[0].replace(".strata", "")
        self.path_edit = QLineEdit(os.path.join(default_dir, base + ".mp4"))
        browse = QPushButton("Browse…")
        browse.clicked.connect(self._browse)
        path_row = QHBoxLayout()
        path_row.addWidget(self.path_edit, 1)
        path_row.addWidget(browse)

        self.fps = QComboBox()
        for r in FRAME_RATES:
            self.fps.addItem(r if r == "Match source" else f"{r} fps", r)
        self.res = QComboBox()
        for k in RESOLUTIONS:
            self.res.addItem(k, k)
        self.quality = QComboBox()
        for k, crf in QUALITY_CRF.items():
            self.quality.addItem(f"{k}  (CRF {crf})", k)
        self.quality.addItem(CUSTOM_BITRATE, CUSTOM_BITRATE)
        self.quality.setCurrentIndex(2)
        self.bitrate = QSpinBox()
        self.bitrate.setRange(500, 200000)
        self.bitrate.setSingleStep(500)
        self.bitrate.setValue(10000)
        self.bitrate.setSuffix(" kbps")
        self.speed = QComboBox()
        for k, preset in SPEED_PRESETS.items():
            self.speed.addItem(f"{k}  ({preset})", k)
        self.speed.setCurrentIndex(1)
        self.abr = QComboBox()
        for b in AUDIO_BITRATES:
            self.abr.addItem(f"{b} kbps", b)
        self.abr.setCurrentIndex(1)
        self.range = QComboBox()
        self.range.addItem("Entire sequence", "all")
        seq = self.project.sequence
        if seq.mark_in is not None or seq.mark_out is not None:
            self.range.addItem("Sequence In to Out", "inout")
            self.range.setCurrentIndex(1)

        self.preset = QComboBox()
        self.preset.addItem("Custom", None)
        for name in EXPORT_PRESETS:
            self.preset.addItem(name, name)
        self.preset.currentIndexChanged.connect(self._apply_preset)
        self._applying_preset = False

        settings = QGroupBox("Video (H.264) and Audio (AAC)")
        form = QFormLayout(settings)
        form.addRow("Preset", self.preset)
        form.addRow("Output file", path_row)
        form.addRow("Range", self.range)
        form.addRow("Frame rate", self.fps)
        form.addRow("Resolution", self.res)
        form.addRow("Quality", self.quality)
        form.addRow("Video bitrate", self.bitrate)
        form.addRow("Encoding speed", self.speed)
        form.addRow("Audio bitrate", self.abr)

        self.summary = QLabel()
        self.summary.setStyleSheet("color:#9fc6ff; padding:6px;")
        self.summary.setWordWrap(True)

        self.progress = QProgressBar()
        self.progress.setVisible(False)
        self.status = QLabel("")
        self.status.setStyleSheet("color:#8c9098;")

        self.buttons = QDialogButtonBox()
        self.export_btn = self.buttons.addButton("Export", QDialogButtonBox.AcceptRole)
        self.cancel_btn = self.buttons.addButton("Cancel", QDialogButtonBox.RejectRole)
        self.export_btn.setDefault(True)
        self.export_btn.clicked.connect(self._start)
        self.cancel_btn.clicked.connect(self._cancel)

        lay = QVBoxLayout(self)
        lay.addWidget(settings)
        lay.addWidget(self.summary)
        lay.addWidget(self.progress)
        lay.addWidget(self.status)
        lay.addWidget(self.buttons)

        for w in (self.fps, self.res, self.quality, self.speed, self.abr, self.range):
            w.currentIndexChanged.connect(self._update_summary)
        for w in (self.fps, self.res, self.quality, self.speed, self.abr):
            w.currentIndexChanged.connect(self._preset_edited)
        self.bitrate.valueChanged.connect(self._update_summary)
        self._update_summary()

    # ------------------------------------------------------------------ settings
    def settings(self) -> ExportSettings:
        s = ExportSettings(
            output_path=self.path_edit.text().strip(),
            frame_rate=self.fps.currentData(),
            resolution=self.res.currentData(),
            quality=self.quality.currentData(),
            custom_bitrate_kbps=self.bitrate.value(),
            speed=self.speed.currentData(),
            audio_bitrate_kbps=self.abr.currentData(),
        )
        seq = self.project.sequence
        if self.range.currentData() == "inout":
            s.range_start = seq.mark_in or 0.0
            s.range_end = seq.mark_out if seq.mark_out is not None else None
        return s

    def _apply_preset(self) -> None:
        name = self.preset.currentData()
        if name is None:
            return
        values = EXPORT_PRESETS[name]
        self._applying_preset = True
        try:
            for combo, key in ((self.res, "resolution"), (self.fps, "frame_rate"), (self.quality, "quality"),
                               (self.speed, "speed"), (self.abr, "audio_bitrate_kbps")):
                i = combo.findData(values[key])
                if i >= 0:
                    combo.setCurrentIndex(i)
        finally:
            self._applying_preset = False
        self._update_summary()

    def _preset_edited(self) -> None:
        if not self._applying_preset and self.preset.currentIndex() != 0:
            self.preset.blockSignals(True)
            self.preset.setCurrentIndex(0)  # edited by hand: now a custom setting
            self.preset.blockSignals(False)

    def _update_summary(self) -> None:
        s = self.settings()
        self.bitrate.setEnabled(s.quality == CUSTOM_BITRATE)
        w, h = resolve_size(self.project, s)
        fps = resolve_fps(self.project, s)
        start, end = export_range(self.project, s)
        size = estimate_size_bytes(self.project, s)
        self.summary.setText(
            f"Output: {w}×{h} @ {fps:g} fps, duration {timecode(end - start, fps)}\n"
            f"Estimated file size: ~{format_size(size)}")
        self.export_btn.setEnabled(end - start > 1e-3)

    def _browse(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Export as", self.path_edit.text(), "MP4 video (*.mp4)")
        if path:
            if not path.lower().endswith(".mp4"):
                path += ".mp4"
            self.path_edit.setText(path)

    # ------------------------------------------------------------------ run
    def _set_running(self, running: bool) -> None:
        for w in self.findChildren(QComboBox) + [self.path_edit, self.bitrate]:
            w.setEnabled(not running)
        self.export_btn.setEnabled(not running)
        self.progress.setVisible(True)
        self.cancel_btn.setText("Cancel export" if running else "Close")

    def _start(self) -> None:
        s = self.settings()
        if not s.output_path:
            QMessageBox.warning(self, "Export", "Choose an output file.")
            return
        out_dir = os.path.dirname(os.path.abspath(s.output_path))
        if not os.path.isdir(out_dir):
            QMessageBox.warning(self, "Export", f"Folder does not exist:\n{out_dir}")
            return
        if os.path.exists(s.output_path):
            if QMessageBox.question(self, "Export", "File exists. Overwrite?") != QMessageBox.Yes:
                return
        try:
            exporter = Exporter(self.project, s, self.controller.audio_store)
        except FileNotFoundError as exc:
            QMessageBox.critical(self, "Export", str(exc))
            return
        self.job = ExportJob(exporter, self)
        self.job.progress.connect(self._on_progress)
        self.job.finished_ok.connect(self._on_done)
        self.job.failed.connect(self._on_failed)
        self.job.cancelled.connect(self._on_cancelled)
        self.progress.setRange(0, 0)
        self.status.setText("Preparing audio…")
        self._t0 = time.monotonic()
        self._set_running(True)
        self.job.start()

    def _on_progress(self, done: int, total: int, eta: float) -> None:
        self.progress.setRange(0, total)
        self.progress.setValue(done)
        elapsed = time.monotonic() - self._t0
        self.status.setText(f"Frame {done} of {total}  ·  elapsed {_fmt_eta(elapsed)}  ·  "
                            f"time remaining ~{_fmt_eta(eta)}")

    def _finish(self) -> None:
        self._set_running(False)
        self.job = None

    def _on_done(self, path: str) -> None:
        self._finish()
        self.progress.setRange(0, 1)
        self.progress.setValue(1)
        size = os.path.getsize(path) if os.path.exists(path) else 0
        self.status.setText(f"Done in {_fmt_eta(time.monotonic() - self._t0)}: {path} ({format_size(size)})")
        box = QMessageBox(self)
        box.setWindowTitle("Export complete")
        box.setText(f"Exported to:\n{path}\n\nSize: {format_size(size)}")
        open_btn = box.addButton("Open folder", QMessageBox.ActionRole)
        box.addButton(QMessageBox.Ok)
        box.exec()
        if box.clickedButton() is open_btn:
            QDesktopServices.openUrl(QUrl.fromLocalFile(os.path.dirname(path)))

    def _on_failed(self, msg: str) -> None:
        self._finish()
        self.status.setText("Export failed")
        QMessageBox.critical(self, "Export failed", msg)

    def _on_cancelled(self) -> None:
        self._finish()
        self.progress.setValue(0)
        self.status.setText("Export cancelled")

    def _cancel(self) -> None:
        if self.job is not None and self.job.isRunning():
            self.status.setText("Cancelling…")
            self.job.exporter.cancel()
        else:
            self.reject()

    def closeEvent(self, e):
        if self.job is not None and self.job.isRunning():
            self.job.exporter.cancel()
            self.job.wait(10000)
        super().closeEvent(e)

    def reject(self):
        if self.job is not None and self.job.isRunning():
            self._cancel()
            return
        super().reject()
