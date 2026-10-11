"""Locating the FFmpeg executable used for export."""
from __future__ import annotations

import functools
import os
import shutil
import subprocess

from ..resources import bundled_ffmpeg


@functools.lru_cache(maxsize=1)
def find_ffmpeg() -> str:
    """Return a usable ffmpeg executable path.

    Order: $STRATA_FFMPEG, the copy shipped inside the packaged app, ffmpeg on
    PATH, the binary bundled with imageio-ffmpeg (running from source).
    """
    env = os.environ.get("STRATA_FFMPEG")
    if env and os.path.isfile(env):
        return env
    bundled = bundled_ffmpeg()
    if bundled:
        return bundled
    on_path = shutil.which("ffmpeg")
    if on_path:
        return on_path
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception as exc:  # pragma: no cover - depends on environment
        raise FileNotFoundError(
            "FFmpeg not found. Install it (e.g. 'winget install Gyan.FFmpeg') "
            "or 'pip install imageio-ffmpeg'.") from exc


def ffmpeg_version() -> str:
    try:
        out = subprocess.run([find_ffmpeg(), "-version"], capture_output=True, text=True,
                             timeout=10, creationflags=_no_window()).stdout
        return out.splitlines()[0] if out else "unknown"
    except Exception:
        return "unavailable"


def _no_window() -> int:
    """Prevent console windows from flashing up on Windows."""
    return getattr(subprocess, "CREATE_NO_WINDOW", 0)


def subprocess_flags() -> int:
    return _no_window()
