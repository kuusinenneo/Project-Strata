"""Locating bundled files both when running from source and as a PyInstaller exe.

Layout (same relative paths in both cases):
    <base>/assets/strata.ico
    <base>/ffmpeg/ffmpeg.exe        (only in the packaged app)
    <base>/licenses/...

<base> is the repository root from source, and PyInstaller's bundle directory
(``sys._MEIPASS``, the ``_internal`` folder next to Strata.exe) when frozen.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path


def is_frozen() -> bool:
    return bool(getattr(sys, "frozen", False))


def base_dir() -> Path:
    if is_frozen():
        return Path(getattr(sys, "_MEIPASS", Path(sys.executable).parent))
    return Path(__file__).resolve().parent.parent


def resource_path(*parts: str) -> Path:
    return base_dir().joinpath(*parts)


def asset_path(name: str) -> Path:
    return resource_path("assets", name)


def bundled_ffmpeg() -> str | None:
    """FFmpeg shipped inside the app folder, if present."""
    exe = "ffmpeg.exe" if os.name == "nt" else "ffmpeg"
    path = resource_path("ffmpeg", exe)
    return str(path) if path.is_file() else None


def user_data_dir() -> Path:
    """Per-user writable folder for logs (never inside the install folder)."""
    root = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    path = Path(root) / "Strata"
    path.mkdir(parents=True, exist_ok=True)
    return path
