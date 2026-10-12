"""Autosave and crash recovery (no Qt).

Each running editor owns a session in the autosave folder:

    autosave-<pid>.strata       latest autosaved copy of the open project
    autosave-<pid>.json         metadata: original project path, save time, pid

A clean exit deletes both files. If the editor crashes they stay behind, and the
next launch finds them (their pid is no longer running) and offers recovery.
"""
from __future__ import annotations

import json
import os
import sys
import time
from dataclasses import dataclass
from typing import Optional

from .io import write_project_file
from .project import Project


def pid_alive(pid: int) -> bool:
    """True if a process with this id is running. Never signals the process."""
    if pid <= 0:
        return False
    if pid == os.getpid():
        return True
    if sys.platform == "win32":
        # os.kill() on Windows would terminate the process, so ask the kernel instead.
        import ctypes
        from ctypes import wintypes
        PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        STILL_ACTIVE = 259
        k32 = ctypes.windll.kernel32
        h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not h:
            return False
        try:
            code = wintypes.DWORD()
            ok = k32.GetExitCodeProcess(h, ctypes.byref(code))
            return bool(ok) and code.value == STILL_ACTIVE
        finally:
            k32.CloseHandle(h)
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


@dataclass
class RecoveryCandidate:
    project_file: str  # the autosaved .strata file
    meta_file: str
    original_path: Optional[str]
    saved_at: float
    pid: int

    def describe(self) -> str:
        name = os.path.basename(self.original_path) if self.original_path else "Untitled project"
        return f"{name} (autosaved {time.strftime('%Y-%m-%d %H:%M', time.localtime(self.saved_at))})"


class AutosaveManager:
    def __init__(self, folder: str, pid: Optional[int] = None):
        self.folder = folder
        self.pid = pid if pid is not None else os.getpid()
        os.makedirs(folder, exist_ok=True)

    @property
    def project_file(self) -> str:
        return os.path.join(self.folder, f"autosave-{self.pid}.strata")

    @property
    def meta_file(self) -> str:
        return os.path.join(self.folder, f"autosave-{self.pid}.json")

    def save(self, project: Project) -> str:
        """Write the current project to this session's autosave file."""
        write_project_file(project, self.project_file)
        meta = {"original_path": project.path, "saved_at": time.time(), "pid": self.pid}
        tmp = self.meta_file + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(meta, f)
        os.replace(tmp, self.meta_file)
        return self.project_file

    def clear(self) -> None:
        """Remove this session's autosave (after a save or a clean exit)."""
        for p in (self.project_file, self.meta_file):
            try:
                os.remove(p)
            except FileNotFoundError:
                pass

    def find_recoverable(self) -> list[RecoveryCandidate]:
        """Autosaves left behind by sessions that are no longer running, newest first."""
        out = []
        for name in os.listdir(self.folder):
            if not (name.startswith("autosave-") and name.endswith(".json")):
                continue
            meta_file = os.path.join(self.folder, name)
            try:
                with open(meta_file, "r", encoding="utf-8") as f:
                    meta = json.load(f)
                pid = int(meta.get("pid", 0))
            except (OSError, ValueError):
                continue
            project_file = meta_file[:-5] + ".strata"
            if pid == self.pid or pid_alive(pid) or not os.path.exists(project_file):
                continue
            out.append(RecoveryCandidate(project_file, meta_file, meta.get("original_path"),
                                         float(meta.get("saved_at", 0)), pid))
        return sorted(out, key=lambda c: c.saved_at, reverse=True)

    @staticmethod
    def discard(candidate: RecoveryCandidate) -> None:
        for p in (candidate.project_file, candidate.meta_file):
            try:
                os.remove(p)
            except FileNotFoundError:
                pass
