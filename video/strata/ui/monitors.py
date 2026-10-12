"""Source and Program monitors."""
from __future__ import annotations

import time
from typing import Optional

import numpy as np
from PySide6.QtCore import QMimeData, QPointF, QRectF, Qt, QTimer, Signal
from PySide6.QtGui import QColor, QDrag, QFont, QImage, QPainter, QPen, QPolygonF
from PySide6.QtWidgets import (QHBoxLayout, QLabel, QSizePolicy, QToolButton, QVBoxLayout,
                               QWidget)

from ..media.decoder import VideoDecoder, load_image
from ..model.project import MediaItem, Project
from ..render.compositor import Compositor
from ..render.mixer import media_mix
from . import theme
from .icons import icon
from .frame_renderer import FrameRenderer, RenderJob
from .mime import MEDIA_MIME, encode_ids
from .playback import AudioOutput, Player, timecode

MAX_PREVIEW_W = 1280


# ---------------------------------------------------------------------------- widgets
class VideoView(QWidget):
    """Shows a QImage letterboxed to a given aspect ratio."""
    pressed = Signal()
    drag_started = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.image: Optional[QImage] = None
        self.aspect = 16 / 9
        self.placeholder = ""
        self.setMinimumSize(160, 90)
        self.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)
        self._press_pos = None

    def frame_rect(self) -> QRectF:
        w, h = self.width(), self.height()
        if w / max(1, h) > self.aspect:
            fw, fh = h * self.aspect, h
        else:
            fw, fh = w, w / self.aspect
        return QRectF((w - fw) / 2, (h - fh) / 2, fw, fh)

    def render_size(self) -> tuple[int, int]:
        r = self.frame_rect()
        dpr = self.devicePixelRatioF()
        w = int(min(MAX_PREVIEW_W, r.width() * dpr))
        return max(16, w // 2 * 2), max(16, int(w / self.aspect) // 2 * 2)

    def set_image(self, img: Optional[QImage]) -> None:
        self.image = img
        self.update()

    def paintEvent(self, _):
        p = QPainter(self)
        p.fillRect(self.rect(), theme.BG_DARK)
        r = self.frame_rect()
        if self.image is not None:
            p.setRenderHint(QPainter.SmoothPixmapTransform, True)
            p.drawImage(r, self.image)
        else:
            p.fillRect(r, QColor(0, 0, 0))
            if self.placeholder:
                p.setPen(theme.TEXT_DIM)
                p.drawText(r, Qt.AlignCenter, self.placeholder)
        p.end()

    def mousePressEvent(self, e):
        self.pressed.emit()
        self._press_pos = e.position()
        super().mousePressEvent(e)

    def mouseMoveEvent(self, e):
        if self._press_pos is not None and (e.buttons() & Qt.LeftButton):
            if (e.position() - self._press_pos).manhattanLength() > 12:
                self._press_pos = None
                self.drag_started.emit()

    def mouseReleaseEvent(self, e):
        self._press_pos = None


class ScrubBar(QWidget):
    """Thin time bar with in/out range and playhead; click/drag to seek."""
    seek = Signal(float)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.duration = 0.0
        self.position = 0.0
        self.mark_in: Optional[float] = None
        self.mark_out: Optional[float] = None
        self.setFixedHeight(22)
        self.setCursor(Qt.PointingHandCursor)

    def _x(self, t: float) -> float:
        return 6 + (self.width() - 12) * (t / self.duration if self.duration > 0 else 0)

    def _t(self, x: float) -> float:
        return max(0.0, min(self.duration, (x - 6) / max(1, self.width() - 12) * self.duration))

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing, True)
        p.fillRect(self.rect(), theme.BG_DARK)
        p.fillRect(QRectF(6, 9, self.width() - 12, 4), theme.BG_LIGHT)
        if self.duration > 0:
            a = self.mark_in if self.mark_in is not None else 0.0
            b = self.mark_out if self.mark_out is not None else self.duration
            if self.mark_in is not None or self.mark_out is not None:
                p.fillRect(QRectF(self._x(a), 6, self._x(b) - self._x(a), 10), QColor(64, 156, 255, 90))
                p.setPen(QPen(theme.ACCENT, 2))
                for t in (self.mark_in, self.mark_out):
                    if t is not None:
                        p.drawLine(QPointF(self._x(t), 3), QPointF(self._x(t), 19))
            x = self._x(self.position)
            p.setPen(Qt.NoPen)
            p.setBrush(theme.PLAYHEAD)
            p.drawPolygon(QPolygonF([QPointF(x - 5, 2), QPointF(x + 5, 2), QPointF(x, 10)]))
            p.fillRect(QRectF(x - 0.75, 2, 1.5, 18), theme.PLAYHEAD)
        p.end()

    def mousePressEvent(self, e):
        self.seek.emit(self._t(e.position().x()))

    def mouseMoveEvent(self, e):
        if e.buttons() & Qt.LeftButton:
            self.seek.emit(self._t(e.position().x()))


def _tool_button(name: str, tip: str, slot) -> QToolButton:
    b = QToolButton()
    b.setIcon(icon(name))
    b.setToolTip(tip)
    b.setAutoRaise(True)
    b.clicked.connect(slot)
    return b


# ---------------------------------------------------------------------------- base monitor
class MonitorPanel(QWidget):
    activated = Signal(object)

    def __init__(self, audio: AudioOutput, title: str, parent=None):
        super().__init__(parent)
        self.view = VideoView()
        self.scrub = ScrubBar()
        self.player = Player(audio, self.duration, self.mix, self)
        self.player.position_changed.connect(self._on_position)
        self.player.playing_changed.connect(self._on_playing)
        self.scrub.seek.connect(self.seek)
        self.view.pressed.connect(lambda: self.activated.emit(self))

        self.tc_label = QLabel("00:00:00:00")
        self.tc_label.setStyleSheet("color:#409cff; font: 600 13px 'Consolas';")
        self.dur_label = QLabel("00:00:00:00")
        self.dur_label.setStyleSheet("color:#8c9098; font: 13px 'Consolas';")
        self.title_label = QLabel(title)
        self.title_label.setStyleSheet("color:#8c9098;")

        self.play_btn = _tool_button("play", "Play/Pause (Space)", self.toggle_play)
        transport = QHBoxLayout()
        transport.setContentsMargins(4, 0, 4, 2)
        transport.addWidget(self.tc_label)
        transport.addStretch(1)
        transport.addWidget(_tool_button("mark_in", "Mark In (I)", self.mark_in))
        transport.addWidget(_tool_button("mark_out", "Mark Out (O)", self.mark_out))
        transport.addSpacing(8)
        transport.addWidget(_tool_button("to_start", "Go to In / start (Home)", self.go_start))
        transport.addWidget(_tool_button("step_back", "Step back one frame (Left)", lambda: self.step(-1)))
        transport.addWidget(self.play_btn)
        transport.addWidget(_tool_button("step_fwd", "Step forward one frame (Right)", lambda: self.step(1)))
        transport.addWidget(_tool_button("to_end", "Go to Out / end (End)", self.go_end))
        transport.addSpacing(8)
        self.extra = QHBoxLayout()
        transport.addLayout(self.extra)
        transport.addStretch(1)
        transport.addWidget(self.dur_label)

        top = QHBoxLayout()
        top.setContentsMargins(6, 2, 6, 0)
        top.addWidget(self.title_label)
        top.addStretch(1)

        lay = QVBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(2)
        lay.addLayout(top)
        lay.addWidget(self.view, 1)
        lay.addWidget(self.scrub)
        lay.addLayout(transport)

        self._render_pending = False
        self._last_request = 0.0
        self._shown_request = 0
        self.renderer = FrameRenderer(self)
        self.renderer.frame_ready.connect(self._frame_ready)
        self.setFocusPolicy(Qt.ClickFocus)

    # -- to override
    def duration(self) -> float:
        return 0.0

    def fps(self) -> float:
        return 30.0

    def mix(self, t0: float, n: int) -> np.ndarray:
        return np.zeros((n, 2), np.float32)

    def make_job(self, t: float, w: int, h: int) -> Optional[RenderJob]:
        """Return a callable(fast) -> QImage that renders time ``t`` off the UI thread."""
        return None

    def marks(self) -> tuple[Optional[float], Optional[float]]:
        return None, None

    def set_marks(self, mark_in: Optional[float], mark_out: Optional[float]) -> None:
        pass

    # -- common behaviour
    def mousePressEvent(self, e):
        self.activated.emit(self)
        super().mousePressEvent(e)

    def render_frame(self, t: float, fast: bool = False) -> Optional[QImage]:
        """Render synchronously (used by tests and tools); normal display is async."""
        job = self.make_job(t, *self.view.render_size())
        return self.renderer.run_now(job, fast) if job is not None else None

    def request_render(self) -> None:
        if not self._render_pending:
            self._render_pending = True
            QTimer.singleShot(0, self._do_render)

    def _do_render(self) -> None:
        self._render_pending = False
        job = self.make_job(self.player.position, *self.view.render_size())
        if job is None:
            self._shown_request = self.renderer.request(lambda fast: None)
            self.view.set_image(None)
            return
        now = time.monotonic()
        # Requests arriving in quick succession mean the user is scrubbing: show
        # approximate frames immediately and let the renderer refine afterwards.
        fast = not self.player.playing and now - self._last_request < 0.08
        self._last_request = now
        self.renderer.request(job, fast)

    def _frame_ready(self, request_id: int, img: QImage) -> None:
        if request_id >= self._shown_request:
            self._shown_request = request_id
            self.view.set_image(img)

    def shutdown(self) -> None:
        self.player.pause()
        self.renderer.stop()

    def refresh_labels(self) -> None:
        fps = self.fps()
        self.tc_label.setText(timecode(self.player.position, fps))
        a, b = self.marks()
        dur = self.duration()
        span = (b if b is not None else dur) - (a if a is not None else 0.0)
        self.dur_label.setText(timecode(max(0.0, span), fps))
        self.scrub.duration = dur
        self.scrub.position = self.player.position
        self.scrub.mark_in, self.scrub.mark_out = a, b
        self.scrub.update()

    def _on_position(self, t: float) -> None:
        self.refresh_labels()
        self.request_render()

    def _on_playing(self, playing: bool) -> None:
        self.play_btn.setIcon(icon("pause" if playing else "play"))

    def seek(self, t: float) -> None:
        self.activated.emit(self)
        self.player.seek(t)

    def toggle_play(self) -> None:
        self.activated.emit(self)
        self.player.toggle()

    def shuttle(self, direction: int) -> None:
        self.player.shuttle(direction)

    def stop(self) -> None:
        self.player.pause()

    def step(self, frames: int) -> None:
        self.player.pause()
        self.player.seek(self.player.position + frames / (self.fps() or 30.0))

    def go_start(self) -> None:
        a, _ = self.marks()
        self.player.seek(a if a is not None and self.player.position > a + 1e-6 else 0.0)

    def go_end(self) -> None:
        _, b = self.marks()
        self.player.seek(b if b is not None and self.player.position < b - 1e-6 else self.duration())

    def mark_in(self) -> None:
        a, b = self.marks()
        t = self.player.position
        if b is not None and t >= b:
            b = None
        self.set_marks(t, b)
        self.refresh_labels()

    def mark_out(self) -> None:
        a, b = self.marks()
        t = self.player.position
        if a is not None and t <= a:
            a = None
        self.set_marks(a, t)
        self.refresh_labels()

    def clear_marks(self) -> None:
        self.set_marks(None, None)
        self.refresh_labels()


# ---------------------------------------------------------------------------- source monitor
class SourceMonitor(MonitorPanel):
    insert_requested = Signal(str)
    overwrite_requested = Signal(str)

    def __init__(self, controller, audio: AudioOutput, parent=None):
        super().__init__(audio, "Source: (no clip)", parent)
        self.controller = controller
        self.media: Optional[MediaItem] = None
        self._decoder: Optional[VideoDecoder] = None
        self._still: Optional[QImage] = None
        self.view.placeholder = "Double-click a clip in the Project panel to view it here"
        self.extra.addWidget(_tool_button("insert", "Insert at playhead (,)", self._insert))
        self.extra.addWidget(_tool_button("overwrite", "Overwrite at playhead (.)", self._overwrite))
        self.view.drag_started.connect(self._start_drag)
        controller.media_changed.connect(self._media_changed)
        controller.audio_loaded.connect(lambda mid: self.request_render()
                                        if self.media and mid == self.media.id else None)

    def _close_decoder(self) -> None:
        if self._decoder is not None:
            with self.renderer.lock:  # never close while the render thread uses it
                self._decoder.close()
        self._decoder = None

    def load_media(self, media: MediaItem) -> None:
        self.player.pause()
        self._close_decoder()
        self._still = None
        self.media = media
        self.title_label.setText(f"Source: {media.name}")
        if media.kind == "video":
            try:
                self._decoder = VideoDecoder(self.controller.proxy_path(media.path) or media.path)
            except Exception:
                self._decoder = None
        elif media.kind == "image":
            try:
                rgba = load_image(media.path)
                h, w = rgba.shape[:2]
                self._still = QImage(rgba.data, w, h, rgba.strides[0], QImage.Format_RGBA8888).copy()
            except Exception:
                self._still = None
        if media.width and media.height:
            self.view.aspect = media.width / media.height
        else:
            self.view.aspect = 16 / 9
        if media.has_audio:
            self.controller.audio_store.request(media.id, media.path)
        self.player.seek(media.mark_in or 0.0)
        self.refresh_labels()
        self.request_render()

    def _media_changed(self) -> None:
        if self.media is not None and self.media.id not in self.controller.project.media:
            self.media = None
            self._close_decoder()
            self._still = None
            self.title_label.setText("Source: (no clip)")
            self.view.set_image(None)
        elif self.media is not None:
            # project may have been restored by undo: rebind to the live object
            self.media = self.controller.project.media[self.media.id]
            self.refresh_labels()

    def duration(self) -> float:
        if self.media is None:
            return 0.0
        return self.media.duration if not self.media.is_still else 5.0

    def fps(self) -> float:
        return (self.media.fps if self.media and self.media.fps else
                self.controller.project.sequence.fps)

    def marks(self):
        if self.media is None:
            return None, None
        return self.media.mark_in, self.media.mark_out

    def set_marks(self, a, b) -> None:
        if self.media is not None:
            self.controller.set_media_marks(self.media.id, a, b)

    def mix(self, t0: float, n: int) -> np.ndarray:
        if self.media is None or not self.media.has_audio:
            return np.zeros((n, 2), np.float32)
        data = self.controller.audio_store.get(self.media.id)
        if data is None:
            return np.zeros((n, 2), np.float32)
        return media_mix(data, t0, n)

    def make_job(self, t: float, w: int, h: int) -> Optional[RenderJob]:
        media, dec, still = self.media, self._decoder, self._still
        if media is None:
            return None
        if still is not None:
            return lambda fast: still
        if dec is not None:
            def job(fast: bool) -> Optional[QImage]:
                if dec is not self._decoder:  # media changed while queued
                    return None
                arr = dec.rgb_at(t, w, h, accurate=not fast)
                if arr is None:
                    return None
                arr = np.ascontiguousarray(arr)
                return QImage(arr.data, arr.shape[1], arr.shape[0], arr.strides[0],
                              QImage.Format_RGB888).copy()
            return job
        if media.kind == "audio":
            duration = self.duration()
            return lambda fast: self._waveform_image(media, t, w, h, duration)
        return None

    def _waveform_image(self, media: MediaItem, t: float, w: int, h: int, duration: float) -> QImage:
        img = QImage(w, h, QImage.Format_RGB32)
        img.fill(QColor(12, 14, 18))
        p = QPainter(img)
        data = self.controller.audio_store.get(media.id)
        if data is not None and len(data.peaks):
            peaks = data.peaks
            cols = np.linspace(0, len(peaks), w + 1).astype(int)
            p.setPen(QColor(60, 170, 110))
            for x in range(w):
                a, b = cols[x], max(cols[x] + 1, cols[x + 1])
                v = float(peaks[a:b].max()) if a < len(peaks) else 0.0
                p.drawLine(x, int(h / 2 - v * h * 0.45), x, int(h / 2 + v * h * 0.45))
            x = int(t / max(1e-6, duration) * w)
            p.setPen(QPen(theme.PLAYHEAD, 2))
            p.drawLine(x, 0, x, h)
        else:
            p.setPen(theme.TEXT_DIM)
            f = QFont()
            f.setPixelSize(max(10, h // 14))
            p.setFont(f)
            p.drawText(img.rect(), Qt.AlignCenter, "Loading audio…")
        p.end()
        return img

    def _insert(self) -> None:
        if self.media:
            self.insert_requested.emit(self.media.id)

    def _overwrite(self) -> None:
        if self.media:
            self.overwrite_requested.emit(self.media.id)

    def _start_drag(self) -> None:
        if self.media is None:
            return
        mime = QMimeData()
        mime.setData(MEDIA_MIME, encode_ids([self.media.id]))
        drag = QDrag(self)
        drag.setMimeData(mime)
        drag.exec(Qt.CopyAction)


# ---------------------------------------------------------------------------- program monitor
class ProgramMonitor(MonitorPanel):
    def __init__(self, controller, audio: AudioOutput, mixer, parent=None):
        super().__init__(audio, "Program: Sequence", parent)
        self.controller = controller
        self.mixer = mixer
        self.compositor = Compositor(controller.project, proxy_lookup=controller.proxy_path)
        controller.proxy_ready.connect(lambda _src: self.request_render())
        self._snapshot: Optional[Project] = None
        self.view.placeholder = ""
        controller.project_changed.connect(self._project_changed)
        controller.project_replaced.connect(self._project_replaced)
        controller.playhead_changed.connect(self._playhead_changed)
        self.player.position_changed.connect(controller.set_playhead)
        self._project_changed()

    def _project_replaced(self) -> None:
        self.player.pause()
        with self.renderer.lock:
            self.compositor.reset()
        self._project_changed()

    def _project_changed(self) -> None:
        # The render thread works on an immutable copy, rebuilt lazily after edits.
        self._snapshot = None
        self.mixer.project = self.controller.project
        seq = self.controller.project.sequence
        self.view.aspect = seq.width / max(1, seq.height)
        self.refresh_labels()
        self.request_render()

    def _playhead_changed(self, t: float) -> None:
        if abs(t - self.player.position) > 1e-9:
            self.player.seek(t)

    def duration(self) -> float:
        return self.controller.project.sequence.duration

    def fps(self) -> float:
        return self.controller.project.sequence.fps

    def marks(self):
        seq = self.controller.project.sequence
        return seq.mark_in, seq.mark_out

    def set_marks(self, a, b) -> None:
        self.controller.set_sequence_marks(a, b)

    def mix(self, t0: float, n: int) -> np.ndarray:
        return self.mixer.mix(t0, n)

    def snapshot(self) -> Project:
        if self._snapshot is None:
            self._snapshot = Project.from_dict(self.controller.project.to_dict())
        return self._snapshot

    def make_job(self, t: float, w: int, h: int) -> Optional[RenderJob]:
        snap = self.snapshot()
        comp = self.compositor

        def job(fast: bool) -> QImage:
            comp.project = snap
            return comp.render(t, w, h, None, fast)
        return job
