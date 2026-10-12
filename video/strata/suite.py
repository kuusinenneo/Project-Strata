"""Finding and starting the other Strata apps (mirrors shared/dotnet/Strata.Ui/SuiteApps.cs).

Looked up, in order: an environment variable (STRATA_PHOTO_EXE / STRATA_PDF_EXE), a sibling
folder in a combined suite folder (...\\StrataPhoto\\StrataPhoto.exe), each app's build output in
the repository, and the development builds.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path
from typing import Iterable, Optional

from .resources import base_dir, is_frozen

_CANDIDATES = {
    "photo": ("STRATA_PHOTO_EXE", [
        Path("StrataPhoto") / "StrataPhoto.exe",
        Path("photo") / "publish" / "StrataPhoto" / "StrataPhoto.exe",
        Path("photo") / "src" / "StrataPhoto.App" / "bin" / "Release" / "net10.0" / "StrataPhoto.exe",
        Path("photo") / "src" / "StrataPhoto.App" / "bin" / "Debug" / "net10.0" / "StrataPhoto.exe",
    ]),
    "pdf": ("STRATA_PDF_EXE", [
        Path("StrataPDF") / "StrataPDF.exe",
        Path("pdf") / "publish" / "StrataPDF" / "StrataPDF.exe",
        Path("pdf") / "src" / "StrataPdf.App" / "bin" / "Release" / "net10.0" / "StrataPDF.exe",
        Path("pdf") / "src" / "StrataPdf.App" / "bin" / "Debug" / "net10.0" / "StrataPDF.exe",
    ]),
}


def _search_roots() -> list[Path]:
    start = Path(sys.executable).parent if is_frozen() else base_dir()
    return [start, *start.parents]


def find_app(app: str) -> Optional[str]:
    env, relative = _CANDIDATES[app]
    exe = os.environ.get(env)
    if exe and os.path.isfile(exe):
        return exe
    for root in _search_roots():
        for rel in relative:
            p = root / rel
            if p.is_file():
                return str(p)
    return None


def launch(app: str, files: Iterable[str] = ()) -> bool:
    exe = find_app(app)
    if exe is None:
        return False
    flags = getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    try:
        subprocess.Popen([exe, *files], cwd=os.path.dirname(exe), creationflags=flags, close_fds=True)
    except OSError:
        return False
    return True
