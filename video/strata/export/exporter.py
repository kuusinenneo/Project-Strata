"""Renders a sequence to MP4 (H.264 + AAC) through FFmpeg.

1. The audio mix is rendered to a temporary 16-bit WAV.
2. Every video frame is rendered by the Compositor and piped to FFmpeg as raw
   BGRA, which encodes it with libx264 and muxes the WAV as AAC.

This class is UI independent; ``ExportJob`` in the UI runs it on a thread.
"""
from __future__ import annotations

import json
import math
import os
import subprocess
import tempfile
import threading
import time
import wave
from typing import Callable, Optional

import numpy as np

from ..media.audio import AudioStore
from ..media.ffmpeg import find_ffmpeg, subprocess_flags
from ..model.project import Project
from ..render.compositor import Compositor
from ..render.mixer import AudioMixer
from .settings import (ExportSettings, export_range, resolve_fps, resolve_size, video_args)

ProgressFn = Callable[[int, int, float], None]  # (frames_done, frames_total, eta_seconds)


class ExportError(Exception):
    pass


class ExportCancelled(Exception):
    pass


class Exporter:
    def __init__(self, project: Project, settings: ExportSettings,
                 audio_store: Optional[AudioStore] = None):
        # Work on a snapshot so edits made during export don't affect the output.
        self.project = Project.from_dict(json.loads(json.dumps(project.to_dict())))
        self.settings = settings
        self.store = audio_store or AudioStore()
        self._cancel = threading.Event()
        self.ffmpeg = find_ffmpeg()

    def cancel(self) -> None:
        self._cancel.set()

    @property
    def cancelled(self) -> bool:
        return self._cancel.is_set()

    # ----------------------------------------------------------------- audio
    def _write_audio(self, path: str, start: float, end: float) -> None:
        mixer = AudioMixer(self.project, self.store)
        sr = self.store.sample_rate
        total = int(math.ceil((end - start) * sr))
        chunk = sr  # one second at a time
        with wave.open(path, "wb") as w:
            w.setnchannels(2)
            w.setsampwidth(2)
            w.setframerate(sr)
            pos = 0
            while pos < total:
                if self.cancelled:
                    raise ExportCancelled()
                n = min(chunk, total - pos)
                buf = mixer.mix(start + pos / sr, n, wait=True)
                w.writeframes((np.clip(buf, -1.0, 1.0) * 32767).astype("<i2").tobytes())
                pos += n

    # ----------------------------------------------------------------- run
    def build_command(self, width: int, height: int, fps: float, wav: str) -> list[str]:
        s = self.settings
        return [self.ffmpeg, "-hide_banner", "-y", "-loglevel", "error",
                "-f", "rawvideo", "-pix_fmt", "bgra", "-s", f"{width}x{height}",
                "-r", f"{fps:.6g}", "-i", "pipe:0",
                "-i", wav,
                "-map", "0:v:0", "-map", "1:a:0",
                *video_args(s),
                "-r", f"{fps:.6g}",
                "-c:a", "aac", "-b:a", f"{int(s.audio_bitrate_kbps)}k",
                "-movflags", "+faststart", "-shortest",
                s.output_path]

    def run(self, progress: Optional[ProgressFn] = None) -> str:
        s = self.settings
        if not s.output_path:
            raise ExportError("No output file chosen")
        start, end = export_range(self.project, s)
        if end - start <= 1e-3:
            raise ExportError("Nothing to export: the sequence is empty")
        width, height = resolve_size(self.project, s)
        fps = resolve_fps(self.project, s)
        total = max(1, int(round((end - start) * fps)))

        tmpdir = tempfile.mkdtemp(prefix="strata_export_")
        wav = os.path.join(tmpdir, "audio.wav")
        log_path = os.path.join(tmpdir, "ffmpeg.log")
        proc = None
        try:
            self._write_audio(wav, start, end)
            with open(log_path, "wb") as log:
                proc = subprocess.Popen(self.build_command(width, height, fps, wav),
                                        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                        stderr=log, creationflags=subprocess_flags())
                comp = Compositor(self.project, decoder_capacity=24)
                image = None
                t0 = time.monotonic()
                for i in range(total):
                    if self.cancelled:
                        raise ExportCancelled()
                    image = comp.render(start + i / fps, width, height, image)
                    try:
                        proc.stdin.write(image.constBits())
                    except (BrokenPipeError, OSError):
                        break  # ffmpeg died; reported below
                    if progress is not None and (i % 5 == 0 or i == total - 1):
                        elapsed = time.monotonic() - t0
                        eta = elapsed / (i + 1) * (total - i - 1)
                        progress(i + 1, total, eta)
                comp.reset()
                try:
                    proc.stdin.close()
                except OSError:
                    pass
                code = proc.wait()
            if code != 0:
                with open(log_path, "r", encoding="utf-8", errors="replace") as f:
                    msg = f.read().strip()[-2000:]
                raise ExportError(f"FFmpeg failed (exit code {code}):\n{msg}")
            return s.output_path
        except ExportCancelled:
            self._kill(proc)
            self._remove_partial()
            raise
        except Exception:
            self._kill(proc)
            raise
        finally:
            for p in (wav, log_path):
                try:
                    os.remove(p)
                except OSError:
                    pass
            try:
                os.rmdir(tmpdir)
            except OSError:
                pass

    @staticmethod
    def _kill(proc) -> None:
        if proc is not None and proc.poll() is None:
            try:
                proc.kill()
                proc.wait(timeout=5)
            except Exception:
                pass

    def _remove_partial(self) -> None:
        try:
            if os.path.exists(self.settings.output_path):
                os.remove(self.settings.output_path)
        except OSError:
            pass
