"""Effects panel: effects, transitions and text presets to drag onto the timeline."""
from __future__ import annotations

from PySide6.QtCore import QMimeData, Qt
from PySide6.QtWidgets import (QAbstractItemView, QLabel, QLineEdit, QTreeWidget, QTreeWidgetItem,
                               QVBoxLayout, QWidget)

from ..model.project import TEXT_PRESETS
from .icons import icon
from .mime import EFFECT_MIME

CATALOG = [
    ("Video Effects", "fx", [
        ("brightness_contrast", "Brightness & Contrast"),
        ("saturation", "Saturation"),
        ("black_white", "Black & White"),
        ("sepia", "Sepia"),
        ("blur", "Blur"),
        ("opacity", "Opacity"),
        ("zoom_in", "Gradual Zoom In"),
        ("zoom_out", "Gradual Zoom Out"),
        ("speed_2x", "Speed ×2"),
        ("speed_half", "Speed ×0.5"),
    ]),
    ("Transitions", "transition", [
        ("cross_dissolve", "Cross Dissolve"),
        ("fade_in", "Fade In"),
        ("fade_out", "Fade Out"),
    ]),
    ("Text", "text", [(f"text:{k}", v) for k, v in TEXT_PRESETS.items()]),
]


class EffectTree(QTreeWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setHeaderHidden(True)
        self.setDragEnabled(True)
        self.setDragDropMode(QAbstractItemView.DragOnly)
        self.setRootIsDecorated(True)

    def mimeData(self, items):
        mime = QMimeData()
        keys = [i.data(0, Qt.UserRole) for i in items if i.data(0, Qt.UserRole)]
        mime.setData(EFFECT_MIME, (keys[0] if keys else "").encode("utf-8"))
        return mime

    def mimeTypes(self):
        return [EFFECT_MIME]


class EffectsPanel(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.search = QLineEdit()
        self.search.setPlaceholderText("Search effects…")
        self.search.setClearButtonEnabled(True)
        self.search.textChanged.connect(self._filter)
        self.tree = EffectTree()
        for cat, ic, entries in CATALOG:
            parent_item = QTreeWidgetItem([cat])
            parent_item.setIcon(0, icon(ic))
            parent_item.setFlags(parent_item.flags() & ~Qt.ItemIsDragEnabled)
            for key, label in entries:
                child = QTreeWidgetItem([label])
                child.setData(0, Qt.UserRole, key)
                child.setIcon(0, icon(ic))
                child.setToolTip(0, "Drag onto a clip" if not key.startswith("text:")
                                 else "Drag onto a video track to add a text overlay")
                parent_item.addChild(child)
            self.tree.addTopLevelItem(parent_item)
        self.tree.expandAll()
        hint = QLabel("Drag an effect or transition onto a clip in the timeline. "
                      "Adjust it in Effect Controls.")
        hint.setWordWrap(True)
        hint.setStyleSheet("color:#8c9098; padding:4px;")
        lay = QVBoxLayout(self)
        lay.setContentsMargins(4, 4, 4, 4)
        lay.addWidget(self.search)
        lay.addWidget(self.tree, 1)
        lay.addWidget(hint)

    def _filter(self, text: str) -> None:
        text = text.lower().strip()
        for i in range(self.tree.topLevelItemCount()):
            cat = self.tree.topLevelItem(i)
            visible = 0
            for j in range(cat.childCount()):
                ch = cat.child(j)
                hide = bool(text) and text not in ch.text(0).lower()
                ch.setHidden(hide)
                visible += not hide
            cat.setHidden(visible == 0)
