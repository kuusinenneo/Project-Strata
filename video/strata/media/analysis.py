"""Audio/video analysis for automatic editing: silence detection and scene-cut detection.

Scene detection uses PySceneDetect when it is installed (``pip install -r requirements-extras.txt``)
and falls back to a built-in detector (frame differences on small decoded frames) otherwise.
"""
from __future__ import annotations

from typing import Callable, Optional

import numpy as np


# --------------------------------------------------------------------------- silence
def silent_ranges(samples: np.ndarray, sample_rate: int, start: float, end: float,
                  threshold_db: float = -40.0, min_silence: float = 0.5, padding: float = 0.1,
                  window: float = 0.02) -> list[tuple[float, float]]:
    """Silent stretches of ``samples`` between ``start`` and ``end`` (source seconds).

    A window is silent when its RMS level is below ``threshold_db`` (dBFS). Runs shorter than
    ``min_silence`` are ignored; each returned range is shrunk by ``padding`` on both sides so a
    little breathing room stays around the sound.
    """
    if samples.size == 0 or end <= start:
        return []
    i0 = max(0, int(start * sample_rate))
    i1 = min(len(samples), int(end * sample_rate))
    if i1 <= i0:
        return []
    mono = samples[i0:i1].astype(np.float32)
    if mono.ndim == 2:
        mono = mono.mean(axis=1)
    mono /= 32768.0
    win = max(1, int(window * sample_rate))
    n = len(mono) // win
    if n == 0:
        return []
    frames = mono[: n * win].reshape(n, win)
    rms = np.sqrt(np.mean(frames * frames, axis=1))
    db = 20 * np.log10(rms + 1e-9)
    silent = db < threshold_db
    out: list[tuple[float, float]] = []
    t0 = start
    i = 0
    while i < n:
        if not silent[i]:
            i += 1
            continue
        j = i
        while j < n and silent[j]:
            j += 1
        a = t0 + i * win / sample_rate
        b = t0 + j * win / sample_rate if j < n else end
        if b - a >= min_silence:
            a2, b2 = a + (padding if i > 0 else 0.0), b - (padding if j < n else 0.0)
            if b2 - a2 > 1e-3:
                out.append((a2, b2))
        i = j
    return out


# --------------------------------------------------------------------------- scenes
def _pyscenedetect_cuts(path: str, start: float, end: float, threshold: float) -> Optional[list[float]]:
    try:
        from scenedetect import ContentDetector, detect  # type: ignore
    except Exception:
        return None
    scenes = detect(path, ContentDetector(threshold=threshold), start_time=start, end_time=end)
    return [s[0].get_seconds() for s in scenes[1:]]


def scene_cuts(path: str, start: float, end: float, threshold: float = 27.0, min_scene: float = 0.6,
               progress: Optional[Callable[[float], None]] = None, use_pyscenedetect: bool = True) -> list[float]:
    """Source times (seconds) of hard cuts between ``start`` and ``end``.

    ``threshold`` follows PySceneDetect's ContentDetector scale (default 27; lower = more cuts).
    """
    if use_pyscenedetect:
        cuts = _pyscenedetect_cuts(path, start, end, threshold)
        if cuts is not None:
            return [c for c in cuts if start + min_scene <= c <= end - min_scene]
    return _builtin_cuts(path, start, end, threshold, min_scene, progress)


def _builtin_cuts(path: str, start: float, end: float, threshold: float, min_scene: float,
                  progress: Optional[Callable[[float], None]]) -> list[float]:
    """Mean absolute HSV-ish difference between consecutive small frames (like ContentDetector)."""
    import av

    cuts: list[float] = []
    prev: Optional[np.ndarray] = None
    last_cut = start
    with av.open(path) as container:
        stream = container.streams.video[0]
        stream.thread_type = "AUTO"
        if start > 0:
            container.seek(int(start / stream.time_base), stream=stream, backward=True)
        for frame in container.decode(stream):
            if frame.pts is None:
                continue
            t = float(frame.pts * stream.time_base)
            if t < start - 1e-3:
                continue
            if t > end:
                break
            rgb = frame.to_ndarray(width=96, height=54, format="rgb24").astype(np.float32)
            # value + approximate saturation, like PySceneDetect's HSV comparison
            mx = rgb.max(axis=2)
            mn = rgb.min(axis=2)
            feat = np.stack([mx, (mx - mn), rgb.mean(axis=2)], axis=2)
            if prev is not None:
                score = float(np.mean(np.abs(feat - prev)))
                if score >= threshold and t - last_cut >= min_scene and end - t >= min_scene:
                    cuts.append(t)
                    last_cut = t
            prev = feat
            if progress is not None and end > start:
                progress(min(1.0, (t - start) / (end - start)))
    return cuts
