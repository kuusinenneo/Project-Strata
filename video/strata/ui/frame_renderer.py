"""Renders monitor frames on a background thread so the UI never waits on decoding.

Only the newest request matters: while one frame renders, newer requests
replace older pending ones. A "fast" (scrubbing) request is followed by an
accurate re-render once no new request has arrived for a short moment.
"""
from __future__ import annotations

import threading
from typing import Callable, Optional

from PySide6.QtCore import QObject, Signal
from PySide6.QtGui import QImage

RenderJob = Callable[[bool], Optional[QImage]]  # fast -> image

REFINE_DELAY = 0.12  # seconds of quiet before the exact frame replaces a scrub frame


class FrameRenderer(QObject):
    frame_ready = Signal(int, QImage)  # request id, image

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self.lock = threading.Lock()  # held while a job touches decoders/compositor
        self._cond = threading.Condition()
        self._job: Optional[tuple[int, RenderJob, bool]] = None
        self._seq = 0
        self._stopped = False
        self._thread = threading.Thread(target=self._loop, name="strata-render", daemon=True)
        self._thread.start()

    def request(self, job: RenderJob, fast: bool = False) -> int:
        with self._cond:
            self._seq += 1
            self._job = (self._seq, job, fast)
            self._cond.notify()
            return self._seq

    def run_now(self, job: RenderJob, fast: bool = False) -> Optional[QImage]:
        """Render synchronously on the calling thread (still serialised with the worker)."""
        return self._run(job, fast)

    def stop(self) -> None:
        with self._cond:
            self._stopped = True
            self._job = None
            self._cond.notify()
        self._thread.join(timeout=2.0)

    def _run(self, job: RenderJob, fast: bool) -> Optional[QImage]:
        with self.lock:
            try:
                return job(fast)
            except Exception:
                return None  # e.g. a file vanished mid-render; the next request will retry

    def _loop(self) -> None:
        while True:
            with self._cond:
                while self._job is None and not self._stopped:
                    self._cond.wait()
                if self._stopped:
                    return
                req, job, fast = self._job
                self._job = None
            img = self._run(job, fast)
            if img is not None and not self._stopped:
                self.frame_ready.emit(req, img)
            if not fast:
                continue
            with self._cond:
                if self._job is None and not self._stopped:
                    self._cond.wait(REFINE_DELAY)
                if self._job is not None or self._stopped:
                    continue
            img = self._run(job, False)
            if img is not None and not self._stopped:
                self.frame_ready.emit(req, img)
