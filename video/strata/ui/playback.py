"""Playback clock and audio output.

``Player`` advances a position from a wall clock (so video never drifts) and
asks the shared ``AudioOutput`` to keep its small buffer filled with freshly
mixed audio. Only one player owns the audio output at a time (source or
program monitor).
"""
from __future__ import annotations

from typing import Callable, Optional

import numpy as np
from PySide6.QtCore import QElapsedTimer, QObject, Qt, QTimer, Signal
from PySide6.QtMultimedia import QAudioFormat, QAudioSink, QMediaDevices

MixFn = Callable[[float, int], np.ndarray]  # (t0, n_samples) -> (n, 2) float32

BUFFER_SECONDS = 0.20


def timecode(t: float, fps: float) -> str:
    fps = fps or 30.0
    t = max(0.0, t)
    frames_per_sec = int(round(fps))
    total_frames = int(round(t * fps))
    ff = total_frames % frames_per_sec
    secs = int(total_frames // frames_per_sec)
    return f"{secs // 3600:02d}:{secs // 60 % 60:02d}:{secs % 60:02d}:{ff:02d}"


class AudioOutput(QObject):
    levels = Signal(float, float)  # peak L, R (linear 0..1+)

    def __init__(self, sample_rate: int = 48000, parent: Optional[QObject] = None):
        super().__init__(parent)
        self.sample_rate = sample_rate
        self.fmt = QAudioFormat()
        self.fmt.setSampleRate(sample_rate)
        self.fmt.setChannelCount(2)
        self.fmt.setSampleFormat(QAudioFormat.Int16)
        self.sink: Optional[QAudioSink] = None
        self.io = None
        self.owner: Optional[object] = None
        self.mix_fn: Optional[MixFn] = None
        self.t = 0.0  # timeline time of the next sample to write
        self.volume = 1.0

    @property
    def available(self) -> bool:
        try:
            return not QMediaDevices.defaultAudioOutput().isNull()
        except Exception:
            return False

    def begin(self, owner: object, mix_fn: MixFn, t: float) -> None:
        self.stop()
        self.owner, self.mix_fn, self.t = owner, mix_fn, t
        if not self.available:
            return
        try:
            self.sink = QAudioSink(QMediaDevices.defaultAudioOutput(), self.fmt, self)
            self.sink.setBufferSize(int(self.sample_rate * BUFFER_SECONDS) * 4)
            self.sink.setVolume(self.volume)
            self.io = self.sink.start()
        except Exception:
            self.sink, self.io = None, None
        self.feed(t)

    def release(self, owner: object) -> None:
        if self.owner is owner:
            self.stop()

    def stop(self) -> None:
        if self.sink is not None:
            try:
                self.sink.stop()
            except Exception:
                pass
            self.sink.deleteLater()
        self.sink, self.io, self.owner, self.mix_fn = None, None, None, None
        self.levels.emit(0.0, 0.0)

    def feed(self, position: float) -> None:
        """Top up the device buffer. ``position`` is the current playback time."""
        if self.mix_fn is None:
            return
        sr = self.sample_rate
        if self.io is not None and self.sink is not None:
            frames = self.sink.bytesFree() // 4
        else:
            # No audio device: still mix (for the meters) to keep pace with the clock.
            frames = int((position + 0.03 - self.t) * sr)
        if frames < sr // 200:
            return
        frames = min(frames, int(sr * BUFFER_SECONDS))
        buf = self.mix_fn(self.t, frames)
        self.t += frames / sr
        if len(buf):
            peaks = np.abs(buf).max(axis=0)
            self.levels.emit(float(peaks[0]), float(peaks[1]))
        if self.io is not None:
            pcm = (np.clip(buf, -1.0, 1.0) * 32767).astype("<i2").tobytes()
            self.io.write(pcm)


class Player(QObject):
    position_changed = Signal(float)
    playing_changed = Signal(bool)

    def __init__(self, audio: AudioOutput, duration_fn: Callable[[], float], mix_fn: MixFn,
                 parent: Optional[QObject] = None):
        super().__init__(parent)
        self.audio = audio
        self.duration_fn = duration_fn
        self.mix_fn = mix_fn
        self.position = 0.0
        self.rate = 0.0
        self._start_pos = 0.0
        self.clock = QElapsedTimer()
        self.timer = QTimer(self)
        self.timer.setTimerType(Qt.PreciseTimer)
        self.timer.setInterval(10)
        self.timer.timeout.connect(self._tick)

    @property
    def playing(self) -> bool:
        return self.rate != 0.0

    def play(self, rate: float = 1.0) -> None:
        dur = self.duration_fn()
        if dur <= 0:
            return
        if rate > 0 and self.position >= dur - 1e-3:
            self.position = 0.0
            self.position_changed.emit(0.0)
        if rate < 0 and self.position <= 1e-3:
            return
        was = self.playing
        self.rate = rate
        self._start_pos = self.position
        self.clock.restart()
        if rate == 1.0:
            self.audio.begin(self, self.mix_fn, self.position)
        else:
            self.audio.release(self)  # shuttle speeds and reverse play silently
        self.timer.start()
        if not was:
            self.playing_changed.emit(True)

    def pause(self) -> None:
        if not self.playing:
            return
        self.rate = 0.0
        self.timer.stop()
        self.audio.release(self)
        self.playing_changed.emit(False)

    def toggle(self) -> None:
        if self.playing:
            self.pause()
        else:
            self.play(1.0)

    def shuttle(self, direction: int) -> None:
        """J (-1) / L (+1): start playing that way, or speed up when already going that way."""
        if direction > 0:
            rate = 1.0 if self.rate <= 0 else min(self.rate * 2, 8.0)
        else:
            rate = -1.0 if self.rate >= 0 else max(self.rate * 2, -8.0)
        self.play(rate)

    def seek(self, t: float) -> None:
        dur = self.duration_fn()
        t = max(0.0, min(t, dur if dur > 0 else t))
        self.position = t
        if self.playing:
            rate = self.rate
            self._start_pos = t
            self.clock.restart()
            if rate == 1.0:
                self.audio.begin(self, self.mix_fn, t)
        self.position_changed.emit(t)

    def _tick(self) -> None:
        pos = self._start_pos + self.clock.elapsed() / 1000.0 * self.rate
        dur = self.duration_fn()
        stop = False
        if self.rate > 0 and pos >= dur:
            pos, stop = dur, True
        elif self.rate < 0 and pos <= 0:
            pos, stop = 0.0, True
        self.position = pos
        self.position_changed.emit(pos)
        if stop:
            self.pause()
        elif self.rate == 1.0 and self.audio.owner is self:
            self.audio.feed(pos)
