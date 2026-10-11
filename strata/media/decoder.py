"""Frame-accurate video/image decoding with PyAV.

``VideoDecoder.frame_at(t)`` returns the frame displayed at source time ``t``.
Sequential access (playback, export) decodes forward cheaply; jumps seek to the
nearest keyframe and decode up to the target.
"""
from __future__ import annotations

from collections import OrderedDict
from typing import Optional

import av
import numpy as np

FORWARD_DECODE_LIMIT = 2.5  # seconds; beyond this a seek is cheaper than decoding forward
FAST_FORWARD_LIMIT = 0.5  # scrubbing: seek sooner
FAST_FRAME_BUDGET = 4  # scrubbing: decode at most this many frames per request


class VideoDecoder:
    def __init__(self, path: str):
        self.path = path
        self.container = av.open(path)
        self.stream = self.container.streams.video[0]
        self.stream.thread_type = "AUTO"
        self.tb = float(self.stream.time_base)
        rate = self.stream.average_rate or self.stream.guessed_rate
        self.fps = float(rate) if rate else 30.0
        st = self.stream.start_time
        self.start_offset = st * self.tb if st is not None else 0.0
        self.width = self.stream.codec_context.width
        self.height = self.stream.codec_context.height
        self._iter = None
        self._cur = None
        self._cur_t = -1.0
        self._pending = None
        self._pending_t = 0.0
        self._eof = False
        self._conv_key = None
        self._conv = None

    def close(self) -> None:
        try:
            self.container.close()
        except Exception:
            pass

    # -- internals
    def _frame_time(self, frame) -> float:
        if frame.pts is not None:
            return frame.pts * self.tb - self.start_offset
        return float(frame.time or 0.0) - self.start_offset

    def _seek(self, t: float) -> None:
        target = int((max(0.0, t) + self.start_offset) / self.tb)
        try:
            self.container.seek(target, stream=self.stream, backward=True, any_frame=False)
        except Exception:
            self.container.seek(0, stream=self.stream)
        self._iter = self.container.decode(self.stream)
        self._cur = None
        self._pending = None
        self._eof = False

    def _next_frame(self):
        if self._iter is None:
            self._iter = self.container.decode(self.stream)
        try:
            return next(self._iter)
        except StopIteration:
            self._eof = True
            return None
        except Exception:
            # Corrupt packet: treat as end of stream rather than crashing playback.
            self._eof = True
            return None

    # -- public
    def frame_at(self, t: float, accurate: bool = True):
        """Return the av.VideoFrame shown at source time ``t`` (None if undecodable).

        With ``accurate=False`` (used while scrubbing) only a few frames are decoded
        after a seek, so the result may be the nearest keyframe before ``t``.
        """
        t = max(0.0, t)
        tol = 0.5 / self.fps
        limit = FORWARD_DECODE_LIMIT if accurate else FAST_FORWARD_LIMIT
        if self._cur is not None and self._cur_t - tol <= t:
            if self._pending is not None and self._pending_t > t + tol:
                return self._cur
            if self._eof:
                return self._cur
            if t - self._cur_t > limit:
                self._seek(t)
        else:
            self._seek(t)
        budget = None if accurate else FAST_FRAME_BUDGET
        while True:
            if budget is not None and self._cur is not None:
                if budget <= 0:
                    return self._cur
                budget -= 1
            if self._pending is None:
                f = self._next_frame()
                if f is None:
                    return self._cur
                self._pending, self._pending_t = f, self._frame_time(f)
            if self._cur is None or self._pending_t <= t + tol:
                self._cur, self._cur_t = self._pending, self._pending_t
                self._pending = None
            else:
                return self._cur

    def rgb_at(self, t: float, width: Optional[int] = None,
               height: Optional[int] = None, accurate: bool = True) -> Optional[np.ndarray]:
        """HxWx3 uint8 RGB array of the frame at ``t``, scaled to width x height."""
        frame = self.frame_at(t, accurate)
        if frame is None:
            return None
        w = int(width or frame.width)
        h = int(height or frame.height)
        key = (id(frame), frame.pts, w, h)
        if key != self._conv_key:
            self._conv = frame.to_ndarray(format="rgb24", width=w, height=h)
            self._conv_key = key
        return self._conv


def load_image(path: str) -> np.ndarray:
    """Decode a still image to an HxWx4 RGBA uint8 array."""
    with av.open(path) as c:
        for frame in c.decode(video=0):
            return np.ascontiguousarray(frame.to_ndarray(format="rgba"))
    raise ValueError(f"Could not decode image {path}")


class DecoderPool:
    """LRU cache of open decoders keyed by an arbitrary key (usually clip id).

    Each clip gets its own decoder so two clips of the same file (e.g. during a
    cross dissolve) don't thrash one decoder with seeks.
    """

    def __init__(self, capacity: int = 12):
        self.capacity = capacity
        self._items: "OrderedDict[str, VideoDecoder]" = OrderedDict()
        self._failed: set[str] = set()

    def get(self, key: str, path: str) -> Optional[VideoDecoder]:
        full_key = f"{key}|{path}"
        dec = self._items.get(full_key)
        if dec is not None:
            self._items.move_to_end(full_key)
            return dec
        if path in self._failed:
            return None
        try:
            dec = VideoDecoder(path)
        except Exception:
            self._failed.add(path)
            return None
        self._items[full_key] = dec
        while len(self._items) > self.capacity:
            _, old = self._items.popitem(last=False)
            old.close()
        return dec

    def clear(self) -> None:
        for d in self._items.values():
            d.close()
        self._items.clear()
        self._failed.clear()


def thumbnail(path: str, t: float = 0.0, max_w: int = 160) -> Optional[np.ndarray]:
    """Small RGB preview frame for the project panel."""
    try:
        dec = VideoDecoder(path)
    except Exception:
        return None
    try:
        w = min(max_w, dec.width or max_w)
        h = max(2, int(round(w * (dec.height or 9) / max(1, dec.width or 16))))
        return dec.rgb_at(t, w, h)
    finally:
        dec.close()
