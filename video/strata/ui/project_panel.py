"""Project panel: imported media with thumbnails; drag items onto the timeline."""
from __future__ import annotations

import os

import numpy as np
from PySide6.QtCore import QMimeData, QObject, QRunnable, QSize, Qt, QThreadPool, Signal
from PySide6.QtGui import QAction, QIcon, QImage, QPixmap
from PySide6.QtWidgets import (QAbstractItemView, QFileDialog, QHBoxLayout, QLabel, QMenu,
                               QMessageBox, QToolButton, QTreeWidget, QTreeWidgetItem,
                               QVBoxLayout, QWidget)

from ..media.decoder import load_image, thumbnail
from ..media.probe import file_filter
from .icons import icon
from .mime import MEDIA_MIME, encode_ids
from .playback import timecode

THUMB = QSize(64, 36)


class _ThumbSignals(QObject):
    done = Signal(str, QImage)


class _ThumbJob(QRunnable):
    def __init__(self, media_id: str, path: str, kind: str, signals: _ThumbSignals):
        super().__init__()
        self.media_id, self.path, self.kind, self.signals = media_id, path, kind, signals

    def run(self):
        try:
            if self.kind == "image":
                arr = load_image(self.path)
                fmt = QImage.Format_RGBA8888
            else:
                arr = thumbnail(self.path, 0.5, 128)
                fmt = QImage.Format_RGB888
            if arr is None:
                return
            arr = np.ascontiguousarray(arr)
            img = QImage(arr.data, arr.shape[1], arr.shape[0], arr.strides[0], fmt).copy()
            img = img.scaled(THUMB * 2, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            self.signals.done.emit(self.media_id, img)
        except Exception:
            pass


class MediaTree(QTreeWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setColumnCount(4)
        self.setHeaderLabels(["Name", "Type", "Duration", "Format"])
        self.setRootIsDecorated(False)
        self.setAlternatingRowColors(True)
        self.setIconSize(THUMB)
        self.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.setDragEnabled(True)
        self.setDragDropMode(QAbstractItemView.DragOnly)
        self.setUniformRowHeights(True)
        self.setColumnWidth(0, 210)
        self.setColumnWidth(1, 60)
        self.setColumnWidth(2, 90)
        self.setAcceptDrops(True)

    def mimeData(self, items):
        mime = QMimeData()
        mime.setData(MEDIA_MIME, encode_ids([i.data(0, Qt.UserRole) for i in items]))
        return mime

    def mimeTypes(self):
        return [MEDIA_MIME]

    # accept files dropped from the OS file manager
    def dragEnterEvent(self, e):
        if e.mimeData().hasUrls():
            e.acceptProposedAction()
        else:
            super().dragEnterEvent(e)

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls():
            e.acceptProposedAction()
        else:
            super().dragMoveEvent(e)

    def dropEvent(self, e):
        if e.mimeData().hasUrls():
            paths = [u.toLocalFile() for u in e.mimeData().urls() if u.isLocalFile()]
            self.parent().import_paths(paths)
            e.acceptProposedAction()
        else:
            super().dropEvent(e)


class ProjectPanel(QWidget):
    open_in_source = Signal(str)  # media id

    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.controller = controller
        self.tree = MediaTree(self)
        self.tree.itemDoubleClicked.connect(self._double_clicked)
        self.tree.setContextMenuPolicy(Qt.CustomContextMenu)
        self.tree.customContextMenuRequested.connect(self._context_menu)
        self.count_label = QLabel("0 items")
        self.count_label.setStyleSheet("color:#8c9098;")
        self._thumbs: dict[str, QIcon] = {}
        self._pool = QThreadPool(self)
        self._pool.setMaxThreadCount(2)
        self._signals = _ThumbSignals()
        self._signals.done.connect(self._thumb_ready)
        self._last_dir = os.path.expanduser("~")

        bar = QHBoxLayout()
        bar.setContentsMargins(4, 2, 4, 2)
        for name, tip, slot in (("import", "Import media… (Ctrl+I)", self.import_dialog),
                                ("insert", "Append selected to the end of the sequence", self.append_selected),
                                ("trash", "Remove selected from project", self.remove_selected)):
            b = QToolButton()
            b.setIcon(icon(name))
            b.setToolTip(tip)
            b.setAutoRaise(True)
            b.clicked.connect(slot)
            bar.addWidget(b)
        bar.addStretch(1)
        bar.addWidget(self.count_label)

        lay = QVBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(0)
        lay.addLayout(bar)
        lay.addWidget(self.tree)
        controller.media_changed.connect(self.refresh)
        self.refresh()

    # ------------------------------------------------------------------ actions
    def import_dialog(self) -> None:
        paths, _ = QFileDialog.getOpenFileNames(self, "Import media", self._last_dir, file_filter())
        if paths:
            self._last_dir = os.path.dirname(paths[0])
            self.import_paths(paths)

    def import_paths(self, paths) -> None:
        added, errors = self.controller.import_files(paths)
        if added:
            self.controller.message.emit(f"Imported {len(added)} item(s)")
        if errors:
            QMessageBox.warning(self, "Import", "Some files could not be imported:\n\n" + "\n".join(errors))

    def selected_ids(self) -> list[str]:
        return [i.data(0, Qt.UserRole) for i in self.tree.selectedItems()]

    def append_selected(self) -> None:
        ids = self.selected_ids()
        if not ids:
            self.controller.message.emit("Select media in the Project panel first")
            return
        self.controller.append_to_sequence(ids)

    def remove_selected(self) -> None:
        ids = self.selected_ids()
        if not ids:
            return
        used = {c.media_id for c in self.controller.project.sequence.clips.values()}
        if any(i in used for i in ids):
            if QMessageBox.question(self, "Remove media",
                                    "Some selected media is used in the sequence. Remove it and its clips?"
                                    ) != QMessageBox.Yes:
                return
        self.controller.remove_media(ids)

    def _double_clicked(self, item, _col) -> None:
        self.open_in_source.emit(item.data(0, Qt.UserRole))

    def _context_menu(self, pos) -> None:
        menu = QMenu(self)
        item = self.tree.itemAt(pos)
        if item is not None:
            menu.addAction("Open in Source Monitor", lambda: self._double_clicked(item, 0))
            menu.addAction("Append to sequence", self.append_selected)
            menu.addAction("Remove", self.remove_selected)
            menu.addSeparator()
        act = QAction(icon("import"), "Import…", menu)
        act.triggered.connect(self.import_dialog)
        menu.addAction(act)
        menu.exec(self.tree.viewport().mapToGlobal(pos))

    # ------------------------------------------------------------------ view
    def refresh(self) -> None:
        selected = set(self.selected_ids())
        self.tree.clear()
        media = list(self.controller.project.media.values())
        for m in media:
            fmt = f"{m.width}x{m.height}" if m.width else ""
            if m.fps and m.kind == "video":
                fmt += f" {m.fps:.3g} fps"
            if m.kind == "audio":
                fmt = f"{m.sample_rate} Hz"
            dur = timecode(m.duration, m.fps or 30) if m.duration else "—"
            if m.mark_in is not None or m.mark_out is not None:
                a, b = m.marked_range()
                dur = timecode(b - a, m.fps or 30) + " ◆"
            it = QTreeWidgetItem([m.name, m.kind.capitalize(), dur, fmt])
            it.setData(0, Qt.UserRole, m.id)
            it.setToolTip(0, m.path + ("" if os.path.exists(m.path) else "\n(missing!)"))
            it.setIcon(0, self._thumbs.get(m.id) or icon({"video": "film", "audio": "audio",
                                                          "image": "image"}[m.kind]))
            if not os.path.exists(m.path):
                it.setForeground(0, Qt.red)
            self.tree.addTopLevelItem(it)
            if m.id in selected:
                it.setSelected(True)
            if m.id not in self._thumbs and m.kind != "audio" and os.path.exists(m.path):
                self._thumbs[m.id] = None  # pending
                self._pool.start(_ThumbJob(m.id, m.path, m.kind, self._signals))
        self.count_label.setText(f"{len(media)} item{'s' if len(media) != 1 else ''}")

    def _thumb_ready(self, media_id: str, img: QImage) -> None:
        self._thumbs[media_id] = QIcon(QPixmap.fromImage(img))
        for i in range(self.tree.topLevelItemCount()):
            it = self.tree.topLevelItem(i)
            if it.data(0, Qt.UserRole) == media_id:
                it.setIcon(0, self._thumbs[media_id])
