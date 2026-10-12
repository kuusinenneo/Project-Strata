"""Speech-to-text for automatic subtitles, using faster-whisper running locally.

faster-whisper is an optional dependency (``pip install -r requirements-ai.txt``). The first use of
a model size downloads it (tiny ≈ 75 MB, base ≈ 145 MB, small ≈ 480 MB) into the Hugging Face
cache; after that everything runs offline on the CPU.
"""
from __future__ import annotations

import importlib.util
import threading
from typing import Callable, Optional

import numpy as np

MODEL_SIZES = ["tiny", "base", "small", "medium"]
SAMPLE_RATE = 16000

_models: dict[str, object] = {}
_lock = threading.Lock()


def available() -> bool:
    return importlib.util.find_spec("faster_whisper") is not None


def _model(size: str):
    with _lock:
        m = _models.get(size)
        if m is None:
            from faster_whisper import WhisperModel
            m = WhisperModel(size, device="cpu", compute_type="int8")
            _models[size] = m
        return m


def transcribe_samples(audio: np.ndarray, model_size: str = "base", language: Optional[str] = None,
                       progress: Optional[Callable[[float], None]] = None,
                       cancelled: Optional[Callable[[], bool]] = None) -> list[tuple[float, float, str]]:
    """Transcribe 16 kHz mono float32 audio. Returns (start, end, text) in seconds from its start."""
    if audio.size == 0:
        return []
    model = _model(model_size)
    segments, _info = model.transcribe(audio, language=language or None, vad_filter=False,
                                       condition_on_previous_text=False)
    total = len(audio) / SAMPLE_RATE
    out = []
    for seg in segments:  # a generator: transcription happens while iterating
        text = seg.text.strip()
        if text:
            out.append((float(seg.start), float(seg.end), text))
        if progress is not None and total > 0:
            progress(min(1.0, seg.end / total))
        if cancelled is not None and cancelled():
            break
    return out


def transcribe_file(path: str, start: float, end: float, model_size: str = "base",
                    language: Optional[str] = None, progress=None, cancelled=None) -> list[tuple[float, float, str]]:
    """Transcribe ``path`` between ``start`` and ``end`` (source seconds); times are source times."""
    from .audio import decode_audio
    data = decode_audio(path, SAMPLE_RATE)
    i0, i1 = max(0, int(start * SAMPLE_RATE)), min(len(data.samples), int(end * SAMPLE_RATE))
    mono = data.samples[i0:i1].astype(np.float32).mean(axis=1) / 32768.0
    return [(start + a, start + b, t) for a, b, t in
            transcribe_samples(mono, model_size, language, progress, cancelled)]
