"""Decoding audio tracks into memory for playback, mixing and waveforms.

Audio is decoded once per media item to 16-bit stereo at the project sample
rate, which keeps mixing simple (slicing numpy arrays) and memory reasonable
(~11 MB per minute).
"""
from __future__ import annotations

import threading
from dataclasses import dataclass
from typing import Callable, Optional

import av
import numpy as np

SAMPLE_RATE = 48000
PEAKS_PER_SECOND = 100


@dataclass
class AudioData:
    samples: np.ndarray  # (N, 2) int16
    sample_rate: int
    peaks: np.ndarray  # (M,) float32 0..1, PEAKS_PER_SECOND per second

    @property
    def duration(self) -> float:
        return len(self.samples) / self.sample_rate


def decode_audio(path: str, sample_rate: int = SAMPLE_RATE) -> AudioData:
    chunks = []
    with av.open(path) as container:
        stream = container.streams.audio[0]
        stream.thread_type = "AUTO"
        resampler = av.AudioResampler(format="s16", layout="stereo", rate=sample_rate)
        for frame in container.decode(stream):
            frame.pts = None  # let the resampler run continuously
            for out in resampler.resample(frame):
                chunks.append(out.to_ndarray().reshape(-1, 2))
        for out in resampler.resample(None):
            chunks.append(out.to_ndarray().reshape(-1, 2))
    samples = np.concatenate(chunks) if chunks else np.zeros((0, 2), np.int16)
    return AudioData(np.ascontiguousarray(samples, dtype=np.int16), sample_rate,
                     compute_peaks(samples, sample_rate))


def compute_peaks(samples: np.ndarray, sample_rate: int) -> np.ndarray:
    block = max(1, sample_rate // PEAKS_PER_SECOND)
    n = len(samples) // block
    if n == 0:
        return np.zeros(0, np.float32)
    mono = np.abs(samples[: n * block].astype(np.int32)).max(axis=1)
    return (mono.reshape(n, block).max(axis=1) / 32768.0).astype(np.float32)


class AudioStore:
    """Thread-backed cache of decoded audio keyed by media id."""

    def __init__(self, sample_rate: int = SAMPLE_RATE):
        self.sample_rate = sample_rate
        self._data: dict[str, AudioData] = {}
        self._loading: set[str] = set()
        self._failed: set[str] = set()
        self._lock = threading.Lock()
        self.listeners: list[Callable[[str], None]] = []

    def get(self, media_id: str) -> Optional[AudioData]:
        return self._data.get(media_id)

    def failed(self, media_id: str) -> bool:
        return media_id in self._failed

    def request(self, media_id: str, path: str) -> None:
        """Start decoding in the background if not cached yet."""
        with self._lock:
            if media_id in self._data or media_id in self._loading or media_id in self._failed:
                return
            self._loading.add(media_id)
        threading.Thread(target=self._load, args=(media_id, path), daemon=True).start()

    def load_now(self, media_id: str, path: str) -> Optional[AudioData]:
        """Synchronous variant (used by export)."""
        if media_id in self._data:
            return self._data[media_id]
        try:
            data = decode_audio(path, self.sample_rate)
        except Exception:
            self._failed.add(media_id)
            return None
        self._data[media_id] = data
        return data

    def _load(self, media_id: str, path: str) -> None:
        try:
            data = decode_audio(path, self.sample_rate)
        except Exception:
            data = None
        with self._lock:
            self._loading.discard(media_id)
            if data is None:
                self._failed.add(media_id)
            else:
                self._data[media_id] = data
        for cb in list(self.listeners):
            try:
                cb(media_id)
            except Exception:
                pass

    def is_loading(self) -> bool:
        return bool(self._loading)

    def forget(self, media_id: str) -> None:
        self._data.pop(media_id, None)
        self._failed.discard(media_id)
