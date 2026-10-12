"""Builds the sequence image at a given time by stacking video tracks.

V1 is drawn first and higher tracks on top. Each clip is decoded at roughly the
size it will be drawn at, colour effects and blur are applied, then it is drawn
with its scale/position transform and opacity (effects opacity x fades x
transitions).

The same code renders the Program monitor preview (small size) and the export
(full size), so what you see is what you export.
"""
from __future__ import annotations

from collections import OrderedDict
from typing import Optional

import numpy as np
from PySide6.QtCore import QRectF, Qt
from PySide6.QtGui import QColor, QFont, QImage, QPainter

from ..media.decoder import DecoderPool, load_image
from ..model.project import EPS, Clip, Project, Sequence, Track
from . import effects as fx
from .text import REFERENCE_HEIGHT, draw_text


def visible_video_tracks(seq: Sequence) -> list[Track]:
    soloed = [t for t in seq.video_tracks if t.solo]
    tracks = soloed if soloed else seq.video_tracks
    return [t for t in tracks if t.enabled]


def audible_audio_tracks(seq: Sequence) -> list[Track]:
    soloed = [t for t in seq.audio_tracks if t.solo]
    tracks = soloed if soloed else seq.audio_tracks
    return [t for t in tracks if t.enabled]


def dissolve_duration(seq: Sequence, clip: Clip) -> tuple[float, Optional[Clip]]:
    """Effective cross-dissolve length into ``clip`` and the clip it dissolves from."""
    if clip.cross_dissolve <= EPS:
        return 0.0, None
    prev = seq.previous_adjacent(clip)
    if prev is None:
        return min(clip.cross_dissolve, clip.duration), None
    d = min(clip.cross_dissolve, clip.duration, prev.duration)
    return d, prev


def transition_progress(seq: Sequence, clip: Clip, t: float) -> Optional[float]:
    """0..1 progress of the transition into ``clip`` at ``t`` (None outside it)."""
    if clip.cross_dissolve <= EPS:
        return None
    d, a = dissolve_duration(seq, clip)
    if a is None or not (clip.start - d / 2 <= t < clip.start + d / 2):
        return None
    return (t - (clip.start - d / 2)) / d


def wipe_fraction(seq: Sequence, clip: Clip, t: float) -> float:
    """Visible fraction (from the left) of a clip that is wiping in; 1.0 when not wiping."""
    if clip.transition != "wipe":
        return 1.0
    u = transition_progress(seq, clip, t)
    return 1.0 if u is None else max(0.0, min(1.0, u))


def track_layers(seq: Sequence, track_id: str, t: float) -> list[tuple[Clip, float]]:
    """Clips to draw for one track at time ``t`` as (clip, alpha) bottom-to-top.

    Transitions: a dissolve blends the two clips, a dip to black fades the outgoing clip
    out and the incoming one in, and a wipe draws both fully opaque while the incoming
    clip is revealed from the left (see :func:`wipe_fraction`).
    """
    clips = seq.clips_on(track_id)
    for b in clips:
        u = transition_progress(seq, b, t)
        if u is None:
            continue
        d, a = dissolve_duration(seq, b)
        if b.transition == "dip_black":
            return [(a, 1.0 - 2 * u)] if u < 0.5 else [(b, 2 * u - 1.0)]
        if b.transition == "wipe":
            return [(a, 1.0), (b, 1.0)]
        return [(a, 1.0), (b, u)]
    c = seq.clip_at(track_id, t)
    if c is None:
        return []
    alpha = 1.0
    d, prev = dissolve_duration(seq, c)
    if d > EPS and prev is None:  # dissolve from nothing = fade in from transparent
        alpha = min(1.0, (t - c.start) / d)
    return [(c, alpha)]


def _clip_fade(clip: Clip, t: float) -> float:
    tt = min(max(t, clip.start), clip.end - 1e-4)
    return clip.fade_factor(tt)


def _qimage_from_array(arr: np.ndarray) -> QImage:
    h, w, c = arr.shape
    arr = np.ascontiguousarray(arr)
    fmt = QImage.Format_RGBA8888 if c == 4 else QImage.Format_RGB888
    img = QImage(arr.data, w, h, arr.strides[0], fmt)
    img._keepalive = arr  # the QImage does not own the buffer
    return img


def _array_from_qimage(img: QImage) -> np.ndarray:
    img = img.convertToFormat(QImage.Format_RGBA8888)
    w, h, bpl = img.width(), img.height(), img.bytesPerLine()
    buf = np.frombuffer(img.constBits(), np.uint8, count=bpl * h).reshape(h, bpl)
    return buf[:, : w * 4].reshape(h, w, 4).copy()


class Compositor:
    def __init__(self, project: Project, decoder_capacity: int = 12):
        self.project = project
        self.pool = DecoderPool(decoder_capacity)
        self._images: dict[str, Optional[QImage]] = {}
        self._scaled: "OrderedDict[tuple, np.ndarray]" = OrderedDict()

    def reset(self) -> None:
        """Drop decoders/caches (after loading a new project)."""
        self.pool.clear()
        self._images.clear()
        self._scaled.clear()

    # ----------------------------------------------------------------- sources
    def _still(self, media_id: str, path: str, w: int, h: int) -> Optional[np.ndarray]:
        key = (media_id, w, h)
        arr = self._scaled.get(key)
        if arr is not None:
            self._scaled.move_to_end(key)
            return arr
        if media_id not in self._images:
            try:
                rgba = load_image(path)
                img = _qimage_from_array(rgba).copy()
            except Exception:
                img = None
            self._images[media_id] = img
        img = self._images[media_id]
        if img is None:
            return None
        arr = _array_from_qimage(img.scaled(w, h, Qt.IgnoreAspectRatio, Qt.SmoothTransformation))
        self._scaled[key] = arr
        while len(self._scaled) > 24:
            self._scaled.popitem(last=False)
        return arr

    def _source_array(self, clip: Clip, t: float, w: int, h: int,
                      fast: bool = False) -> Optional[np.ndarray]:
        media = self.project.media_for(clip)
        if media is None:
            return None
        if media.is_still:
            return self._still(media.id, media.path, w, h)
        dec = self.pool.get(clip.id, media.path)
        if dec is None:
            return None
        src_t = clip.source_time(t)
        if media.duration > 0:
            src_t = min(max(0.0, src_t), max(0.0, media.duration - 1e-3))
        return dec.rgb_at(src_t, w, h, accurate=not fast)

    # ----------------------------------------------------------------- drawing
    def _draw_clip(self, p: QPainter, clip: Clip, t: float, alpha: float,
                   out_w: int, out_h: int, fast: bool = False) -> None:
        wipe = wipe_fraction(self.project.sequence, clip, t)
        if wipe < 1.0:
            if wipe <= 0.0:
                return
            p.save()
            p.setClipRect(QRectF(0, 0, out_w * wipe, out_h))
            try:
                self._draw_clip_inner(p, clip, t, alpha, out_w, out_h, fast)
            finally:
                p.restore()
            return
        self._draw_clip_inner(p, clip, t, alpha, out_w, out_h, fast)

    def _draw_clip_inner(self, p: QPainter, clip: Clip, t: float, alpha: float,
                         out_w: int, out_h: int, fast: bool = False) -> None:
        alpha *= max(0.0, min(1.0, clip.effects.opacity / 100.0)) * _clip_fade(clip, t)
        if alpha <= 1e-3:
            return
        scale, px, py = clip.transform.at(clip.progress(t))
        if clip.kind == "text":
            if clip.text is not None:
                draw_text(p, clip.text, out_w, out_h, alpha, px, py, scale / 100.0)
            return

        media = self.project.media_for(clip)
        if media is None or not media.width or not media.height:
            return
        fit = min(out_w / media.width, out_h / media.height)
        fw = media.width * fit * scale / 100.0
        fh = media.height * fit * scale / 100.0
        if fw < 1 or fh < 1:
            return
        cx = out_w / 2 + px / 100.0 * out_w
        cy = out_h / 2 + py / 100.0 * out_h
        target = QRectF(cx - fw / 2, cy - fh / 2, fw, fh)
        if not target.intersects(QRectF(0, 0, out_w, out_h)):
            return
        # decode no larger than the source, no larger than drawn
        dw = max(2, min(media.width, int(round(fw))))
        dh = max(2, min(media.height, int(round(fh))))
        arr = self._source_array(clip, t, dw, dh, fast)
        if arr is None:
            self._draw_offline(p, target, media.name)
            return
        if not clip.effects.is_color_neutral():
            arr = fx.apply_color(arr, clip.effects)
        if clip.effects.blur > 0.4:
            # radius is defined at 1080p output; convert to decoded-pixel units
            radius = clip.effects.blur * (out_h / REFERENCE_HEIGHT) * (dw / fw)
            arr = fx.blur(arr, radius)
        img = _qimage_from_array(arr)
        p.setOpacity(min(1.0, alpha))
        p.drawImage(target, img)
        p.setOpacity(1.0)

    @staticmethod
    def _draw_offline(p: QPainter, rect: QRectF, name: str) -> None:
        p.save()
        p.fillRect(rect, QColor(90, 20, 30))
        f = QFont()
        f.setPixelSize(max(8, int(rect.height() / 14)))
        p.setFont(f)
        p.setPen(QColor(240, 200, 200))
        p.drawText(rect, Qt.AlignCenter, f"Media offline\n{name}")
        p.restore()

    def render(self, t: float, out_w: int, out_h: int, image: Optional[QImage] = None,
               fast: bool = False) -> QImage:
        """Render the sequence at time ``t`` into an out_w x out_h RGB32 image.

        ``fast`` trades frame accuracy for speed while scrubbing.
        """
        if image is None or image.width() != out_w or image.height() != out_h:
            image = QImage(out_w, out_h, QImage.Format_RGB32)
        image.fill(QColor(0, 0, 0))
        seq = self.project.sequence
        p = QPainter(image)
        p.setRenderHint(QPainter.SmoothPixmapTransform, True)
        p.setRenderHint(QPainter.Antialiasing, True)
        try:
            for track in visible_video_tracks(seq):
                for clip, alpha in track_layers(seq, track.id, t):
                    self._draw_clip(p, clip, t, alpha, out_w, out_h, fast)
        finally:
            p.end()
        return image
