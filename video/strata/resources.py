"""Locating bundled files both when running from source and as a PyInstaller exe.

Layout:
    from source                         packaged (PyInstaller)
    <repo>/video/licenses/...           <base>/licenses/...
    <repo>/shared/theme.json            <base>/shared/theme.json
    <repo>/shared/icons/app/...         <base>/shared/icons/app/...
                                        <base>/ffmpeg/ffmpeg.exe

<base> is the ``video`` folder from source, and PyInstaller's bundle directory
(``sys._MEIPASS``, the ``_internal`` folder next to StrataVideo.exe) when frozen.
The design system (colours, fonts, spacing, icons) lives in the repository's
``shared`` folder, which is used by both Strata apps.
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


def shared_dir() -> Path:
    """The shared design-system folder (theme.json + icons)."""
    if is_frozen():
        return base_dir() / "shared"
    return base_dir().parent / "shared"


def shared_path(*parts: str) -> Path:
    return shared_dir().joinpath(*parts)


def app_icon_path() -> Path:
    return shared_path("icons", "app", "strata-video.ico")


def bundled_ffmpeg() -> str | None:
    """FFmpeg shipped inside the app folder, if present."""
    exe = "ffmpeg.exe" if os.name == "nt" else "ffmpeg"
    path = resource_path("ffmpeg", exe)
    return str(path) if path.is_file() else None


def user_data_dir() -> Path:
    """Per-user writable folder for logs (never inside the install folder)."""
    root = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    path = Path(root) / "Strata Video"
    path.mkdir(parents=True, exist_ok=True)
    return path
