"""Snapshot-based undo/redo.

Before every edit the caller calls ``push(label)`` which stores the project as
it was. Undo restores the previous snapshot, redo the next. Repeated pushes
with the same ``merge_key`` (e.g. dragging a slider) collapse into one step.
"""
from __future__ import annotations

import json
import time
from typing import Optional

from .project import Project


class UndoStack:
    def __init__(self, project: Project, limit: int = 200):
        self.project = project
        self.limit = limit
        self._undo: list[tuple[str, str, int]] = []  # (label, json snapshot, state id)
        self._redo: list[tuple[str, str, int]] = []
        self._last_key: Optional[str] = None
        self._last_time = 0.0
        self._next_state = 1
        self.state = 0  # id of the current project state
        self.clean_state = 0  # state id at last save

    def _snapshot(self) -> str:
        return json.dumps(self.project.to_dict())

    def push(self, label: str, merge_key: Optional[str] = None, merge_window: float = 1.5) -> None:
        now = time.monotonic()
        if (merge_key is not None and merge_key == self._last_key
                and now - self._last_time < merge_window and self._undo):
            self._last_time = now
            self._redo.clear()
            return
        self._undo.append((label, self._snapshot(), self.state))
        if len(self._undo) > self.limit:
            self._undo.pop(0)
        self._redo.clear()
        self.state = self._next_state
        self._next_state += 1
        self._last_key = merge_key
        self._last_time = now

    def break_merge(self) -> None:
        self._last_key = None

    def can_undo(self) -> bool:
        return bool(self._undo)

    def can_redo(self) -> bool:
        return bool(self._redo)

    def undo_label(self) -> str:
        return self._undo[-1][0] if self._undo else ""

    def redo_label(self) -> str:
        return self._redo[-1][0] if self._redo else ""

    def undo(self) -> bool:
        if not self._undo:
            return False
        label, snap, state = self._undo.pop()
        self._redo.append((label, self._snapshot(), self.state))
        self.project.restore(json.loads(snap))
        self.state = state
        self._last_key = None
        return True

    def redo(self) -> bool:
        if not self._redo:
            return False
        label, snap, state = self._redo.pop()
        self._undo.append((label, self._snapshot(), self.state))
        self.project.restore(json.loads(snap))
        self.state = state
        self._last_key = None
        return True

    def clear(self) -> None:
        self._undo.clear()
        self._redo.clear()
        self._last_key = None
        self.state = self.clean_state = 0

    def mark_clean(self) -> None:
        self.clean_state = self.state

    def is_dirty(self) -> bool:
        return self.state != self.clean_state
