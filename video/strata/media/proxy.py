"""Low-resolution proxy files for smooth playback of 4K (and larger) footage.

Proxies live in a per-user cache, keyed by the source file's path, size and
modification time, so they are shared by all projects and never stored in a
project. Preview uses them when available; export always reads the originals.
"""
from __future__ import annotations

import hashlib
import os
import queue
import subprocess
import threading
from typing import Callable, Optional

from .ffmpeg import find_ffmpeg, subprocess_flags

PROXY_HEIGHT = 540
MIN_SOURCE_HEIGHT = 2000  # 4K UHD / DCI and up


def needs_proxy(media, min_height: int = MIN_SOURCE_HEIGHT) -> bool:
    return media.kind == "video" and media.has_video and max(media.height, media.width * 9 // 16) >= min_height


class ProxyStore:
    def __init__(self, folder: str, height: int = PROXY_HEIGHT):
        self.folder = folder
        self.height = height
        os.makedirs(folder, exist_ok=True)

    def proxy_file(self, src: str) -> str:
        try:
            st = os.stat(src)
            sig = f"{os.path.normcase(os.path.abspath(src))}|{st.st_size}|{int(st.st_mtime)}|{self.height}"
        except OSError:
            sig = os.path.normcase(os.path.abspath(src))
        name = os.path.splitext(os.path.basename(src))[0][:40]
        return os.path.join(self.folder, f"{name}-{hashlib.sha1(sig.encode()).hexdigest()[:12]}.mp4")

    def get(self, src: str) -> Optional[str]:
        """The finished proxy for ``src``, or None."""
        p = self.proxy_file(src)
        return p if os.path.exists(p) else None

    def build(self, src: str, cancel: Optional[threading.Event] = None) -> str:
        """Transcode ``src`` to a small, short-GOP H.264 proxy (blocking)."""
        dst = self.proxy_file(src)
        if os.path.exists(dst):
            return dst
        tmp = dst + ".part.mp4"
        cmd = [find_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-i", src,
               "-map", "0:v:0", "-an", "-vf", f"scale=-2:{self.height}",
               "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-g", "12", "-pix_fmt", "yuv420p",
               tmp]
        proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, creationflags=subprocess_flags())
        try:
            while True:
                try:
                    _, err = proc.communicate(timeout=0.25)
                    break
                except subprocess.TimeoutExpired:
                    if cancel is not None and cancel.is_set():
                        proc.kill()
                        proc.communicate()
                        raise RuntimeError("cancelled")
            if proc.returncode != 0:
                raise RuntimeError((err or b"").decode(errors="replace").strip() or "ffmpeg failed")
            os.replace(tmp, dst)
            return dst
        finally:
            if os.path.exists(tmp):
                try:
                    os.remove(tmp)
                except OSError:
                    pass


class ProxyWorker:
    """Builds proxies one at a time on a background thread."""

    def __init__(self, store: ProxyStore, on_done: Callable[[str, Optional[str], Optional[str]], None]):
        self.store = store
        self.on_done = on_done  # (source path, proxy path or None, error or None), called on the worker thread
        self._queue: "queue.Queue[Optional[str]]" = queue.Queue()
        self._pending: set[str] = set()
        self._cancel = threading.Event()
        self._thread = threading.Thread(target=self._run, name="proxy-worker", daemon=True)
        self._thread.start()

    def request(self, src: str) -> bool:
        """Queue ``src`` unless its proxy exists or is already queued. True if queued."""
        key = os.path.normcase(os.path.abspath(src))
        if key in self._pending or self.store.get(src):
            return False
        self._pending.add(key)
        self._queue.put(src)
        return True

    @property
    def busy(self) -> bool:
        return bool(self._pending)

    def _run(self) -> None:
        while True:
            src = self._queue.get()
            if src is None:
                return
            try:
                path, error = self.store.build(src, self._cancel), None
            except Exception as exc:
                path, error = None, str(exc)
            self._pending.discard(os.path.normcase(os.path.abspath(src)))
            if not self._cancel.is_set():
                self.on_done(src, path, error)

    def stop(self) -> None:
        self._cancel.set()
        self._queue.put(None)
