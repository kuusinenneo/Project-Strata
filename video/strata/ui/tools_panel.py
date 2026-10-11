"""Narrow vertical tool strip next to the timeline."""
from __future__ import annotations

from PySide6.QtCore import QSize, Qt
from PySide6.QtWidgets import QButtonGroup, QToolButton, QVBoxLayout, QWidget

from .icons import icon

TOOLS = [
    ("select", "select", "Selection tool (V)"),
    ("razor", "razor", "Razor tool (C) — click a clip to cut it; Shift+click cuts all tracks"),
    ("ripple", "ripple", "Ripple delete tool (B) — click a clip to remove it and close the gap"),
    ("text", "text", "Text tool (T) — click a video track to add a text overlay"),
]


class ToolsPanel(QWidget):
    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.controller = controller
        self.group = QButtonGroup(self)
        self.group.setExclusive(True)
        self.buttons: dict[str, QToolButton] = {}
        lay = QVBoxLayout(self)
        lay.setContentsMargins(3, 6, 3, 6)
        lay.setSpacing(4)
        for key, ic, tip in TOOLS:
            b = QToolButton()
            b.setIcon(icon(ic))
            b.setIconSize(QSize(20, 20))
            b.setCheckable(True)
            b.setToolTip(tip)
            b.setFixedSize(32, 32)
            b.clicked.connect(lambda _=False, k=key: controller.set_tool(k))
            self.group.addButton(b)
            self.buttons[key] = b
            lay.addWidget(b, 0, Qt.AlignHCenter)
        lay.addStretch(1)
        self.setMinimumWidth(38)
        controller.tool_changed.connect(self._sync)
        self._sync(controller.tool)

    def _sync(self, tool: str) -> None:
        b = self.buttons.get(tool)
        if b is not None:
            b.setChecked(True)
