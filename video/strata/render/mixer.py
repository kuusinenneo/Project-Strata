"""Mixes all audible audio clips of a sequence into one stereo buffer.

Overlapping clips on different tracks are summed. Per-clip volume, fade in/out
and cross dissolves (equal-power crossfades) are applied. The same mixer feeds
playback, the level meters and the export.
"""
from __future__ import annotations

import numpy as np

from ..media.audio import AudioData, AudioStore
from ..model.project import EPS, Clip, Project, Sequence
from .compositor import audible_audio_tracks, dissolve_duration


def _clip_audio_span(seq: Sequence, clip: Clip) -> tuple[float, float, float, float]:
    """(start, end, dissolve_in, dissolve_out) with dissolve handles extending the clip."""
    d_in, prev = dissolve_duration(seq, clip)
    d_in = d_in if prev is not None else 0.0
    d_out = 0.0
    nxt = seq.next_adjacent(clip)
    if nxt is not None:
        d, p = dissolve_duration(seq, nxt)
        if p is not None and p.id == clip.id:
            d_out = d
    return clip.start - d_in / 2, clip.end + d_out / 2, d_in, d_out


def _envelope(clip: Clip, ts: np.ndarray, d_in: float, d_out: float) -> np.ndarray:
    g = np.full(ts.shape, clip.volume / 100.0, dtype=np.float32)
    if clip.fade_in > EPS:
        g *= np.clip((ts - clip.start) / clip.fade_in, 0.0, 1.0)
    if clip.fade_out > EPS:
        g *= np.clip((clip.end - ts) / clip.fade_out, 0.0, 1.0)
    if d_in > EPS:
        x = np.clip((ts - (clip.start - d_in / 2)) / d_in, 0.0, 1.0)
        g *= np.sin(x * np.pi / 2)  # equal power
    if d_out > EPS:
        x = np.clip((ts - (clip.end - d_out / 2)) / d_out, 0.0, 1.0)
        g *= np.cos(x * np.pi / 2)
    return g


class AudioMixer:
    def __init__(self, project: Project, store: AudioStore):
        self.project = project
        self.store = store

    @property
    def sample_rate(self) -> int:
        return self.store.sample_rate

    def _audio(self, clip: Clip, wait: bool) -> AudioData | None:
        media = self.project.media_for(clip)
        if media is None or not media.has_audio:
            return None
        data = self.store.get(media.id)
        if data is None:
            if wait:
                data = self.store.load_now(media.id, media.path)
            else:
                self.store.request(media.id, media.path)
        return data

    def mix(self, t0: float, n: int, wait: bool = False) -> np.ndarray:
        """Return ``n`` stereo float32 samples of the mix starting at timeline time ``t0``."""
        sr = self.sample_rate
        out = np.zeros((n, 2), dtype=np.float32)
        if n <= 0:
            return out
        seq = self.project.sequence
        t1 = t0 + n / sr
        tracks = {t.id for t in audible_audio_tracks(seq)}
        for clip in list(seq.clips.values()):
            if clip.kind != "audio" or clip.track_id not in tracks or clip.volume <= 0:
                continue
            start, end, d_in, d_out = _clip_audio_span(seq, clip)
            if end <= t0 or start >= t1:
                continue
            data = self._audio(clip, wait)
            if data is None or len(data.samples) == 0:
                continue
            i0 = max(0, int(np.ceil((start - t0) * sr)))
            i1 = min(n, int(np.floor((end - t0) * sr)))
            if i1 <= i0:
                continue
            ts = t0 + np.arange(i0, i1, dtype=np.float64) / sr
            src = clip.in_point + (ts - clip.start) * clip.speed
            idx = np.round(src * data.sample_rate).astype(np.int64)
            valid = (idx >= 0) & (idx < len(data.samples))
            if not valid.any():
                continue
            seg = np.zeros((i1 - i0, 2), dtype=np.float32)
            seg[valid] = data.samples[idx[valid]].astype(np.float32) / 32768.0
            seg *= _envelope(clip, ts, d_in, d_out)[:, None]
            out[i0:i1] += seg
        return out


def media_mix(data: AudioData, t0: float, n: int, volume: float = 1.0) -> np.ndarray:
    """Plain playback of one media item's audio (source monitor)."""
    out = np.zeros((n, 2), dtype=np.float32)
    i0 = int(round(t0 * data.sample_rate))
    if i0 >= len(data.samples) or i0 + n <= 0:
        return out
    a, b = max(0, i0), min(len(data.samples), i0 + n)
    out[a - i0:b - i0] = data.samples[a:b].astype(np.float32) / 32768.0 * volume
    return out
