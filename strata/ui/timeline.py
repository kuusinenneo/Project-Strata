"""Timeline panel: ruler, track headers, clip canvas, zoom and scrolling.

Video tracks are shown top-down from the highest (V3 ... V1), then audio
tracks A1, A2 ... below a divider, like a classic NLE.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Optional

from PySide6.QtCore import QPointF, QRect, QRectF, Qt, QTimer, Signal
from PySide6.QtGui import QBrush, QColor, QFont, QPainter, QPainterPath, QPen, QPolygonF
from PySide6.QtWidgets import (QHBoxLayout, QLabel, QMenu, QScrollBar, QSizePolicy, QSlider,
                               QToolButton, QVBoxLayout, QWidget)

from ..model import edits
from ..model.project import EPS, Clip, Track
from . import theme
from .icons import icon
from .mime import EFFECT_MIME, MEDIA_MIME, decode_ids
from .playback import timecode

RULER_H = 30
TRACK_H = 52
SEP_H = 8
HEADER_W = 168
EDGE_PX = 7
SNAP_PX = 9
MIN_PPS, MAX_PPS = 1.0, 800.0


@dataclass
class Row:
    track: Track
    y: float  # content y (without ruler / scroll)
    h: float


def build_rows(seq) -> list[Row]:
    rows, y = [], 0.0
    for tr in reversed(seq.video_tracks):
        rows.append(Row(tr, y, TRACK_H))
        y += TRACK_H
    y += SEP_H
    for tr in seq.audio_tracks:
        rows.append(Row(tr, y, TRACK_H))
        y += TRACK_H
    return rows


def content_height(seq) -> float:
    return (len(seq.video_tracks) + len(seq.audio_tracks)) * TRACK_H + SEP_H


# ============================================================================ headers
class TrackHeader(QWidget):
    def __init__(self, controller, track: Track, parent=None):
        super().__init__(parent)
        self.controller = controller
        self.track_id = track.id
        self.setAutoFillBackground(False)
        self.name = QLabel()
        self.name.setFixedWidth(30)
        self.name.setAlignment(Qt.AlignCenter)
        self.enabled_btn = self._btn("Toggle track output (visibility / mute)")
        self.lock_btn = self._btn("Lock track")
        self.solo_btn = self._btn("Solo track")
        self.add_btn = self._btn("Add a track of this type", checkable=False)
        self.add_btn.setIcon(icon("plus"))
        self.del_btn = self._btn("Remove this track (only when empty)", checkable=False)
        self.del_btn.setIcon(icon("close"))
        self.enabled_btn.clicked.connect(lambda v: controller.set_track_flag(self.track_id, "enabled", v))
        self.lock_btn.clicked.connect(lambda v: controller.set_track_flag(self.track_id, "locked", v))
        self.solo_btn.clicked.connect(lambda v: controller.set_track_flag(self.track_id, "solo", v))
        self.add_btn.clicked.connect(lambda: controller.add_track(self.kind))
        self.del_btn.clicked.connect(lambda: controller.remove_track(self.track_id))
        lay = QHBoxLayout(self)
        lay.setContentsMargins(4, 0, 4, 0)
        lay.setSpacing(1)
        lay.addWidget(self.name)
        for b in (self.enabled_btn, self.lock_btn, self.solo_btn):
            lay.addWidget(b)
        lay.addStretch(1)
        lay.addWidget(self.add_btn)
        lay.addWidget(self.del_btn)
        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._menu)
        self.kind = track.kind
        self.sync()

    def _btn(self, tip: str, checkable: bool = True) -> QToolButton:
        b = QToolButton()
        b.setToolTip(tip)
        b.setCheckable(checkable)
        b.setAutoRaise(True)
        b.setFixedSize(24, 24)
        return b

    def sync(self) -> None:
        seq = self.controller.project.sequence
        tr = seq.track(self.track_id)
        if tr is None:
            return
        self.kind = tr.kind
        video = tr.kind == "video"
        self.name.setText(seq.track_name(tr.id))
        color = "#7fa8ff" if video else "#6fd39a"
        self.name.setStyleSheet(f"font-weight:700; color:{color};")
        self.enabled_btn.setChecked(not tr.enabled)
        self.enabled_btn.setIcon(icon(("eye" if tr.enabled else "eye_off") if video
                                      else ("speaker" if tr.enabled else "speaker_off")))
        self.enabled_btn.setToolTip(("Hide track" if tr.enabled else "Show track") if video
                                    else ("Mute track" if tr.enabled else "Unmute track"))
        self.lock_btn.setChecked(tr.locked)
        self.lock_btn.setIcon(icon("lock" if tr.locked else "unlock"))
        self.solo_btn.setChecked(tr.solo)
        self.solo_btn.setIcon(icon("solo", "#ffd34d" if tr.solo else None))
        lst = seq.video_tracks if video else seq.audio_tracks
        self.del_btn.setEnabled(seq.is_track_empty(tr.id) and len(lst) > 1)
        self.add_btn.setToolTip("Add video track (stacks on top)" if video else "Add audio track (goes below)")

    def _menu(self, pos) -> None:
        m = QMenu(self)
        m.addAction(icon("plus"), "Add video track", lambda: self.controller.add_track("video"))
        m.addAction(icon("plus"), "Add audio track", lambda: self.controller.add_track("audio"))
        act = m.addAction(icon("close"), "Remove track", lambda: self.controller.remove_track(self.track_id))
        act.setEnabled(self.del_btn.isEnabled())
        m.exec(self.mapToGlobal(pos))

    def paintEvent(self, _):
        p = QPainter(self)
        tr = self.controller.project.sequence.track(self.track_id)
        base = QColor(40, 42, 47) if (tr and tr.kind == "video") else QColor(37, 42, 40)
        p.fillRect(self.rect(), base)
        p.setPen(theme.BORDER)
        p.drawLine(0, self.height() - 1, self.width(), self.height() - 1)
        p.end()


class HeaderColumn(QWidget):
    """Left column holding one TrackHeader per row, scrolled with the canvas."""

    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.controller = controller
        self.headers: dict[str, TrackHeader] = {}
        self.vscroll = 0.0
        self.setFixedWidth(HEADER_W)
        self.tc = QLabel("00:00:00:00", self)
        self.tc.setStyleSheet("color:#409cff; font: 600 15px 'Consolas'; background: transparent;")
        self.tc.setGeometry(8, 4, HEADER_W - 12, RULER_H - 6)

    def rebuild(self) -> None:
        seq = self.controller.project.sequence
        ids = {t.id for t in seq.tracks()}
        for tid in list(self.headers):
            if tid not in ids:
                self.headers.pop(tid).deleteLater()
        for tr in seq.tracks():
            if tr.id not in self.headers:
                h = TrackHeader(self.controller, tr, self)
                self.headers[tr.id] = h
                h.show()
        self.relayout()

    def relayout(self) -> None:
        seq = self.controller.project.sequence
        for row in build_rows(seq):
            h = self.headers.get(row.track.id)
            if h is None:
                continue
            y = RULER_H + row.y - self.vscroll
            h.setGeometry(0, int(y), HEADER_W, int(row.h))
            h.setVisible(y + row.h > RULER_H and y < self.height())
            h.sync()
        self.update()

    def resizeEvent(self, e):
        self.relayout()

    def paintEvent(self, _):
        p = QPainter(self)
        p.fillRect(self.rect(), theme.BG_PANEL)
        p.fillRect(QRectF(0, 0, self.width(), RULER_H), theme.BG_DARK)
        seq = self.controller.project.sequence
        sep_y = RULER_H + len(seq.video_tracks) * TRACK_H - self.vscroll
        p.fillRect(QRectF(0, sep_y, self.width(), SEP_H), theme.BG_DARK)
        p.end()


# ============================================================================ canvas
class TimelineCanvas(QWidget):
    view_changed = Signal()
    clip_activated = Signal(str)  # double-click

    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.c = controller
        self.pps = 40.0  # pixels per second
        self.hscroll = 0.0  # px
        self.vscroll = 0.0  # px
        self.setMouseTracking(True)
        self.setAcceptDrops(True)
        self.setFocusPolicy(Qt.StrongFocus)
        self.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)
        self.setMinimumHeight(120)
        self._mode: Optional[str] = None
        self._press = None
        self._drag: dict = {}
        self._snap_t: Optional[float] = None
        self._hover_t: Optional[float] = None
        self._drop_ghosts: list[tuple[str, float, float, str]] = []  # (track_id, start, end, kind)
        self._drop_clip: Optional[str] = None
        self._band: Optional[QRectF] = None
        self._dirty = QRectF()
        self.small_font = QFont()
        self.small_font.setPixelSize(11)
        self.tiny_font = QFont()
        self.tiny_font.setPixelSize(10)

    # ------------------------------------------------------------------ geometry
    @property
    def seq(self):
        return self.c.project.sequence

    def x_of(self, t: float) -> float:
        return t * self.pps - self.hscroll

    def t_of(self, x: float) -> float:
        return max(0.0, (x + self.hscroll) / self.pps)

    def rows(self) -> list[Row]:
        return build_rows(self.seq)

    def row_rect(self, row: Row) -> QRectF:
        return QRectF(0, RULER_H + row.y - self.vscroll, self.width(), row.h)

    def row_at(self, y: float) -> Optional[Row]:
        for r in self.rows():
            top = RULER_H + r.y - self.vscroll
            if top <= y < top + r.h:
                return r
        return None

    def nearest_row(self, y: float, kind: Optional[str] = None) -> Optional[Row]:
        rows = [r for r in self.rows() if kind is None or r.track.kind == kind]
        if not rows:
            return None
        return min(rows, key=lambda r: abs(RULER_H + r.y - self.vscroll + r.h / 2 - y))

    def clip_rect(self, clip: Clip, start: Optional[float] = None, end: Optional[float] = None,
                  track_id: Optional[str] = None) -> Optional[QRectF]:
        tid = track_id or clip.track_id
        for r in self.rows():
            if r.track.id == tid:
                s = clip.start if start is None else start
                e = clip.end if end is None else end
                top = RULER_H + r.y - self.vscroll
                return QRectF(self.x_of(s), top + 3, max(2.0, (e - s) * self.pps), r.h - 6)
        return None

    def clip_at(self, pos: QPointF) -> Optional[Clip]:
        row = self.row_at(pos.y())
        if row is None:
            return None
        for c in self.seq.clips_on(row.track.id):
            r = self.clip_rect(c)
            if r is not None and r.left() - 1 <= pos.x() <= r.right() + 1:
                return c
        return None

    def content_width(self) -> float:
        return (self.seq.duration + 60.0) * self.pps

    def content_h(self) -> float:
        return content_height(self.seq)

    # ------------------------------------------------------------------ zoom/scroll
    def set_pps(self, pps: float, anchor_x: Optional[float] = None) -> None:
        pps = max(MIN_PPS, min(MAX_PPS, pps))
        if anchor_x is None:
            anchor_x = self.x_of(self.c.playhead)
            if not 0 <= anchor_x <= self.width():
                anchor_x = self.width() / 2
        t = self.t_of(anchor_x)
        self.pps = pps
        self.hscroll = max(0.0, t * pps - anchor_x)
        self.view_changed.emit()
        self.update()

    def zoom(self, factor: float, anchor_x: Optional[float] = None) -> None:
        self.set_pps(self.pps * factor, anchor_x)

    def zoom_to_fit(self) -> None:
        dur = max(5.0, self.seq.duration)
        self.pps = max(MIN_PPS, min(MAX_PPS, (self.width() - 40) / dur))
        self.hscroll = 0.0
        self.view_changed.emit()
        self.update()

    def ensure_playhead_visible(self) -> bool:
        """Scroll so the playhead is on screen. Returns True if the view scrolled."""
        x = self.x_of(self.c.playhead)
        if x > self.width() - 20 or x < 0:
            self.hscroll = max(0.0, self.c.playhead * self.pps - self.width() * 0.15)
            self.view_changed.emit()
            return True
        return False

    def update_playhead(self, old_t: float, new_t: float) -> None:
        """Repaint only the strips covered by the old and new playhead."""
        for t in (old_t, new_t):
            x = int(self.x_of(t))
            self.update(QRect(x - 8, 0, 16, self.height()))

    # ------------------------------------------------------------------ snapping
    def _snap(self, t: float, exclude=(), points=None) -> float:
        if not self.c.snapping:
            self._snap_t = None
            return t
        pts = points if points is not None else self.c.snap_points(exclude)
        v, hit = edits.snap(t, pts, SNAP_PX / self.pps)
        self._snap_t = hit
        return v

    # ------------------------------------------------------------------ painting
    def paintEvent(self, e):
        p = QPainter(self)
        self._dirty = QRectF(e.rect())
        try:
            self._paint(p)
        finally:
            p.end()

    def _paint(self, p: QPainter) -> None:
        p.setRenderHint(QPainter.Antialiasing, False)
        w, h = self.width(), self.height()
        p.fillRect(self.rect(), theme.BG)
        seq = self.seq
        rows = self.rows()

        # track backgrounds
        p.save()
        p.setClipRect(QRectF(0, RULER_H, w, h - RULER_H))
        for r in rows:
            rr = self.row_rect(r)
            base = QColor(34, 35, 39) if r.track.kind == "video" else QColor(32, 36, 34)
            p.fillRect(rr, base)
            p.setPen(QColor(44, 46, 51))
            p.drawLine(QPointF(0, rr.bottom()), QPointF(w, rr.bottom()))
            if r.track.locked:
                p.fillRect(rr, QBrush(QColor(255, 255, 255, 14), Qt.BDiagPattern))
        sep_y = RULER_H + len(seq.video_tracks) * TRACK_H - self.vscroll
        p.fillRect(QRectF(0, sep_y, w, SEP_H), theme.BG_DARK)

        # sequence in/out shading
        if seq.mark_in is not None or seq.mark_out is not None:
            a = seq.mark_in or 0.0
            b = seq.mark_out if seq.mark_out is not None else seq.duration
            p.fillRect(QRectF(self.x_of(a), RULER_H, (b - a) * self.pps, h), QColor(64, 156, 255, 18))

        dragging_ids = set(self._drag.get("ids", ())) if self._mode in ("move", "trim") else set()
        dirty = self._dirty
        for clip in seq.clips.values():
            r = self.clip_rect(clip)
            if r is None or not r.adjusted(-2, -2, 2, 2).intersects(dirty):
                continue
            ghost = clip.id in dragging_ids
            self._paint_clip(p, clip, r, faded=ghost)
        # drag previews
        if self._mode == "move":
            for cid, (start, tid) in self._drag.get("preview", {}).items():
                clip = seq.clips.get(cid)
                if clip is None:
                    continue
                r = self.clip_rect(clip, start, start + clip.duration, tid)
                if r is not None:
                    self._paint_clip(p, clip, r, preview=True)
        elif self._mode == "trim":
            pv = self._drag.get("preview")
            if pv is not None:
                clip, s, e = pv
                r = self.clip_rect(clip, s, e)
                if r is not None:
                    self._paint_clip(p, clip, r, preview=True)
        for tid, s, e, kind in self._drop_ghosts:
            for row in rows:
                if row.track.id == tid:
                    rr = self.row_rect(row)
                    g = QRectF(self.x_of(s), rr.top() + 3, (e - s) * self.pps, rr.height() - 6)
                    col = theme.CLIP_AUDIO if kind == "audio" else theme.CLIP_VIDEO
                    p.fillRect(g, QColor(col.red(), col.green(), col.blue(), 120))
                    p.setPen(QPen(QColor(255, 255, 255, 180), 1, Qt.DashLine))
                    p.drawRect(g)
        if self._band is not None:
            p.fillRect(self._band, QColor(64, 156, 255, 40))
            p.setPen(QPen(theme.ACCENT, 1))
            p.drawRect(self._band)
        p.restore()

        self._paint_ruler(p)

        # snap indicator
        if self._snap_t is not None and self._mode in ("move", "trim", "drop", "razor"):
            x = self.x_of(self._snap_t)
            p.setPen(QPen(QColor(255, 220, 80), 1))
            p.drawLine(QPointF(x, RULER_H), QPointF(x, h))
        # razor hover line
        if self.c.tool == "razor" and self._hover_t is not None and self._mode is None:
            x = self.x_of(self._hover_t)
            p.setPen(QPen(QColor(255, 255, 255, 170), 1, Qt.DashLine))
            p.drawLine(QPointF(x, RULER_H), QPointF(x, h))
        # playhead
        x = self.x_of(self.c.playhead)
        if -10 <= x <= w + 10:
            p.setRenderHint(QPainter.Antialiasing, True)
            p.setPen(QPen(theme.PLAYHEAD, 1.2))
            p.drawLine(QPointF(x, RULER_H - 8), QPointF(x, h))
            p.setBrush(theme.PLAYHEAD)
            p.setPen(Qt.NoPen)
            p.drawPolygon(QPolygonF([QPointF(x - 6, RULER_H - 14), QPointF(x + 6, RULER_H - 14),
                                     QPointF(x + 6, RULER_H - 7), QPointF(x, RULER_H - 1),
                                     QPointF(x - 6, RULER_H - 7)]))

    def _paint_ruler(self, p: QPainter) -> None:
        w = self.width()
        p.fillRect(QRectF(0, 0, w, RULER_H), theme.BG_DARK)
        seq = self.seq
        fps = seq.fps or 30.0
        candidates = [1 / fps, 2 / fps, 5 / fps, 10 / fps, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300,
                      600, 900, 1800, 3600]
        major = next((c for c in candidates if c * self.pps >= 90), candidates[-1])
        minor = major / 5 if major * self.pps / 5 >= 7 else major / 2
        t0 = self.t_of(0)
        t1 = self.t_of(w)
        p.setFont(self.tiny_font)
        k = math.floor(t0 / minor)
        while k * minor <= t1 + minor:
            t = k * minor
            x = self.x_of(t)
            is_major = abs(t / major - round(t / major)) < 1e-6
            p.setPen(QColor(120, 124, 132) if is_major else QColor(72, 75, 82))
            p.drawLine(QPointF(x, RULER_H - (12 if is_major else 5)), QPointF(x, RULER_H - 1))
            if is_major:
                p.setPen(theme.TEXT_DIM)
                p.drawText(QPointF(x + 3, 12), timecode(t, fps))
            k += 1
        if seq.mark_in is not None or seq.mark_out is not None:
            a = seq.mark_in or 0.0
            b = seq.mark_out if seq.mark_out is not None else seq.duration
            p.fillRect(QRectF(self.x_of(a), RULER_H - 5, (b - a) * self.pps, 5), QColor(64, 156, 255, 160))
        p.setPen(theme.BORDER)
        p.drawLine(QPointF(0, RULER_H - 0.5), QPointF(w, RULER_H - 0.5))

    def _clip_color(self, clip: Clip) -> QColor:
        if clip.kind == "text":
            return theme.CLIP_TEXT
        if clip.kind == "audio":
            return theme.CLIP_AUDIO
        m = self.c.project.media_for(clip)
        return theme.CLIP_IMAGE if (m and m.is_still) else theme.CLIP_VIDEO

    def _paint_clip(self, p: QPainter, clip: Clip, r: QRectF, faded: bool = False,
                    preview: bool = False) -> None:
        tr = self.seq.track(clip.track_id)
        col = QColor(self._clip_color(clip))
        selected = clip.id in self.c.selection
        if selected:
            col = col.lighter(130)
        alpha = 255
        if faded:
            alpha = 70
        elif preview:
            alpha = 200
        elif tr is not None and not tr.enabled:
            alpha = 110
        col.setAlpha(alpha)
        p.save()
        visible = r.intersected(self._dirty.adjusted(-2, 0, 2, 0))
        p.setClipRect(r.adjusted(0, 0, 1, 1))
        p.fillRect(r, col)
        # title strip
        strip = QRectF(r.left(), r.top(), r.width(), 15)
        p.fillRect(strip, QColor(0, 0, 0, 55))
        # waveform for audio
        if clip.kind == "audio" and r.height() > 20:
            self._paint_waveform(p, clip, r, visible)
        # fades
        p.setRenderHint(QPainter.Antialiasing, True)
        shade = QColor(0, 0, 0, 90)
        if clip.fade_in > EPS:
            fw = clip.fade_in * self.pps
            path = QPainterPath(QPointF(r.left(), r.top()))
            path.lineTo(r.left() + fw, r.top())
            path.lineTo(r.left(), r.bottom())
            path.closeSubpath()
            p.fillPath(path, shade)
            p.setPen(QPen(QColor(255, 255, 255, 150), 1))
            p.drawLine(QPointF(r.left(), r.bottom()), QPointF(r.left() + fw, r.top()))
        if clip.fade_out > EPS:
            fw = clip.fade_out * self.pps
            path = QPainterPath(QPointF(r.right(), r.top()))
            path.lineTo(r.right() - fw, r.top())
            path.lineTo(r.right(), r.bottom())
            path.closeSubpath()
            p.fillPath(path, shade)
            p.setPen(QPen(QColor(255, 255, 255, 150), 1))
            p.drawLine(QPointF(r.right(), r.bottom()), QPointF(r.right() - fw, r.top()))
        if clip.cross_dissolve > EPS:
            dw = min(clip.cross_dissolve, clip.duration) * self.pps
            has_prev = self.seq.previous_adjacent(clip) is not None
            dr = QRectF(r.left(), r.top() + 15, max(4.0, dw / 2 if has_prev else dw), r.height() - 15)
            p.fillRect(dr, QBrush(QColor(255, 255, 255, 70), Qt.FDiagPattern))
        p.setRenderHint(QPainter.Antialiasing, False)
        # label
        p.setFont(self.small_font)
        p.setPen(QColor(240, 242, 246, alpha))
        label = clip.name or clip.kind
        if clip.kind == "text" and clip.text is not None:
            label = "T  " + (clip.text.text.splitlines()[0] if clip.text.text else "")
        if abs(clip.speed - 1.0) > 1e-6:
            label += f"  [{clip.speed:g}×]"
        if clip.kind != "audio" and (not clip.effects.is_color_neutral() or clip.effects.blur > 0
                                     or clip.effects.opacity < 100 or clip.transform.animate
                                     or abs(clip.transform.scale - 100) > EPS):
            label = "fx  " + label
        tx = max(r.left(), 0) + 4
        p.drawText(QRectF(tx, r.top(), r.right() - tx - 2, 15), Qt.AlignVCenter | Qt.AlignLeft, label)
        if clip.link_id:
            p.setPen(QColor(255, 255, 255, 90))
            p.drawLine(QPointF(r.left(), r.bottom() - 1), QPointF(r.right(), r.bottom() - 1))
        p.restore()
        # border (outside clip-rect so it is fully visible)
        p.setPen(QPen(QColor(255, 255, 255) if selected else QColor(0, 0, 0, 120), 2 if selected else 1))
        p.setBrush(Qt.NoBrush)
        p.drawRect(r.adjusted(0.5, 0.5, -0.5, -0.5))

    def _paint_waveform(self, p: QPainter, clip: Clip, r: QRectF, visible: QRectF) -> None:
        media = self.c.project.media_for(clip)
        if media is None:
            return
        data = self.c.audio_store.get(media.id)
        if data is None:
            self.c.audio_store.request(media.id, media.path)
            return
        peaks = data.peaks
        if len(peaks) == 0:
            return
        from ..media.audio import PEAKS_PER_SECOND
        mid = r.top() + 15 + (r.height() - 15) / 2
        amp = (r.height() - 17) / 2
        x0 = int(max(r.left(), visible.left()))
        x1 = int(min(r.right(), visible.right()))
        gain = clip.volume / 100.0
        p.setPen(QColor(180, 240, 200, 150))
        step = 1
        for x in range(x0, x1, step):
            t_a = self.t_of(x)
            t_b = self.t_of(x + step)
            sa = clip.source_time(t_a) * PEAKS_PER_SECOND
            sb = clip.source_time(t_b) * PEAKS_PER_SECOND
            ia = int(max(0.0, sa))
            ib = min(len(peaks), max(ia + 1, int(sb)))
            if ia >= len(peaks):
                break
            v = float(peaks[ia:ib].max()) * gain
            v = min(1.0, v)
            p.drawLine(QPointF(x, mid - v * amp), QPointF(x, mid + v * amp))

    # ------------------------------------------------------------------ mouse
    def _edge_hit(self, pos: QPointF) -> tuple[Optional[Clip], Optional[str]]:
        clip = self.clip_at(pos)
        candidates = [clip] if clip else []
        row = self.row_at(pos.y())
        if row is not None:  # edges slightly outside a clip still count
            for c in self.seq.clips_on(row.track.id):
                if c not in candidates:
                    candidates.append(c)
        for c in candidates:
            r = self.clip_rect(c)
            if r is None or not (r.top() <= pos.y() <= r.bottom()):
                continue
            if r.width() < EDGE_PX * 3:
                continue
            if abs(pos.x() - r.left()) <= EDGE_PX:
                return c, "left"
            if abs(pos.x() - r.right()) <= EDGE_PX:
                return c, "right"
        return None, None

    def mousePressEvent(self, e):
        self.setFocus()
        pos = e.position()
        self._press = pos
        if e.button() == Qt.RightButton:
            return
        if e.button() == Qt.MiddleButton:
            self._mode = "pan"
            self._drag = {"x": pos.x(), "h": self.hscroll}
            return
        if pos.y() < RULER_H:
            self._mode = "playhead"
            self.c.set_playhead(self.t_of(pos.x()))
            return
        tool = self.c.tool
        t = self.t_of(pos.x())
        row = self.row_at(pos.y())
        clip = self.clip_at(pos)
        mods = e.modifiers()
        if tool == "razor":
            if clip is not None:
                tt = self._snap(t)
                if mods & Qt.ShiftModifier:
                    self.c.cut_at(tt, all_tracks=True)
                else:
                    self.c.razor(clip.id, tt)
            return
        alt = bool(mods & Qt.AltModifier)
        if tool == "ripple":
            if clip is not None:
                targets = [clip] if alt else self.c.with_linked([clip])
                self.c.delete_clips([x.id for x in targets], ripple=True)
            elif row is not None:
                if not self.c.remove_gap(row.track.id, t):
                    self.c.message.emit("Click a clip to ripple delete it, or a gap to close it")
            return
        if tool == "text":
            if row is not None and row.track.kind == "video":
                self.c.add_text(row.track.id, self._snap(t))
                self.c.set_tool("select")
            else:
                self.c.message.emit("Click on a video track to add text")
            return
        # selection tool
        eclip, edge = self._edge_hit(pos)
        if eclip is not None:
            if eclip.id not in self.c.selection or alt:
                self.c.select([eclip.id], single=alt)
            self._mode = "trim"
            self._drag = {"id": eclip.id, "edge": edge, "ids": [eclip.id], "preview": None,
                          "solo": alt}
            return
        if clip is not None:
            # Alt+click picks only the audio or only the video part of a linked clip
            if mods & Qt.ControlModifier:
                self.c.select([clip.id], toggle=True, single=alt)
            elif mods & Qt.ShiftModifier:
                self.c.select([clip.id], add=True, single=alt)
            elif alt or clip.id not in self.c.selection:
                self.c.select([clip.id], single=alt)
            self._mode = "pending_move"
            # A plain click (no drag) on an already-selected clip narrows the
            # selection to it on release; dragging moves the whole selection.
            plain = not (mods & (Qt.ControlModifier | Qt.ShiftModifier | Qt.AltModifier))
            self._drag = {"anchor_t": t, "anchor_clip": clip.id,
                          "reselect": clip.id if plain else None}
            return
        if not (mods & (Qt.ControlModifier | Qt.ShiftModifier)):
            self.c.clear_selection()
        self._mode = "band"
        self._band = QRectF(pos, pos)

    def mouseMoveEvent(self, e):
        pos = e.position()
        t = self.t_of(pos.x())
        if self._mode is None:
            self._update_cursor(pos)
            if self.c.tool == "razor":
                self._hover_t = self._snap(t) if self.clip_at(pos) else None
                self.update()
            return
        if self._mode == "pan":
            self.hscroll = max(0.0, self._drag["h"] - (pos.x() - self._drag["x"]))
            self.view_changed.emit()
            self.update()
        elif self._mode == "playhead":
            self.c.set_playhead(t)
        elif self._mode == "pending_move":
            if (pos - self._press).manhattanLength() > 4:
                self._begin_move()
                self._update_move(pos)
        elif self._mode == "move":
            self._update_move(pos)
        elif self._mode == "trim":
            self._update_trim(pos)
        elif self._mode == "band":
            self._band = QRectF(self._press, pos).normalized()
            self.update()
        self._autoscroll(pos)

    def mouseReleaseEvent(self, e):
        mode = self._mode
        self._mode = None
        if mode == "move":
            preview = self._drag.get("preview", {})
            changed = {cid: v for cid, v in preview.items()
                       if cid in self.seq.clips and (abs(v[0] - self.seq.clips[cid].start) > 1e-9
                                                     or v[1] != self.seq.clips[cid].track_id)}
            if changed:
                self.c.move_clips(preview)
        elif mode == "trim":
            pv = self._drag.get("preview")
            if pv is not None:
                clip, s, en = pv
                solo = self._drag.get("solo", False)
                if self._drag["edge"] == "left":
                    self.c.trim_clip(clip.id, "left", s, solo)
                else:
                    self.c.trim_clip(clip.id, "right", en, solo)
        elif mode == "pending_move" and self._drag.get("reselect"):
            self.c.select([self._drag["reselect"]])
        elif mode == "band" and self._band is not None:
            band = self._band
            ids = [c.id for c in self.seq.clips.values()
                   if (r := self.clip_rect(c)) is not None and r.intersects(band)]
            if ids:
                self.c.select(ids, add=True)
        self._drag = {}
        self._band = None
        self._snap_t = None
        self.update()

    def mouseDoubleClickEvent(self, e):
        clip = self.clip_at(e.position())
        if clip is not None and self.c.tool == "select":
            self.c.select([clip.id])
            self.clip_activated.emit(clip.id)

    def leaveEvent(self, _):
        self._hover_t = None
        self.update()

    def _update_cursor(self, pos: QPointF) -> None:
        tool = self.c.tool
        if pos.y() < RULER_H:
            self.setCursor(Qt.ArrowCursor)
        elif tool == "razor":
            self.setCursor(Qt.CrossCursor)
        elif tool == "ripple":
            self.setCursor(Qt.PointingHandCursor)
        elif tool == "text":
            self.setCursor(Qt.IBeamCursor)
        else:
            clip, edge = self._edge_hit(pos)
            self.setCursor(Qt.SizeHorCursor if edge else Qt.ArrowCursor)

    def _autoscroll(self, pos: QPointF) -> None:
        if self._mode not in ("move", "trim", "band", "playhead"):
            return
        if pos.x() > self.width() - 20:
            self.hscroll += 12
        elif pos.x() < 20 and self.hscroll > 0:
            self.hscroll = max(0.0, self.hscroll - 12)
        else:
            return
        self.view_changed.emit()
        self.update()

    # -- move
    def _begin_move(self) -> None:
        ids = [cid for cid in self.c.selection if cid in self.seq.clips
               and not self.c.is_locked(self.seq.clips[cid].track_id)]
        if not ids:
            self._mode = None
            return
        self._mode = "move"
        anchor = self.seq.clips.get(self._drag["anchor_clip"])
        self._drag.update({
            "ids": ids,
            "orig": {cid: (self.seq.clips[cid].start, self.seq.clips[cid].track_id) for cid in ids},
            "anchor_track": anchor.track_id if anchor else None,
            "points": self.c.snap_points(exclude=ids),
            "preview": {},
        })

    def _update_move(self, pos: QPointF) -> None:
        d = self._drag
        seq = self.seq
        t = self.t_of(pos.x())
        dt = t - d["anchor_t"]
        orig = d["orig"]
        # keep everything at >= 0
        min_start = min(s for s, _ in orig.values())
        dt = max(dt, -min_start)
        # snapping: test starts and ends of moving clips
        self._snap_t = None
        if self.c.snapping:
            best, best_d = None, SNAP_PX / self.pps
            for cid in d["ids"]:
                c = seq.clips[cid]
                for edge in (orig[cid][0], orig[cid][0] + c.duration):
                    for ptt in d["points"]:
                        dist = abs(edge + dt - ptt)
                        if dist < best_d:
                            best, best_d = (ptt - edge, ptt), dist
            if best is not None:
                dt, self._snap_t = best[0], best[1]
                dt = max(dt, -min_start)
        # vertical: track delta within the anchor clip's kind
        delta = 0
        anchor_track = seq.track(d["anchor_track"]) if d["anchor_track"] else None
        if anchor_track is not None:
            row = self.nearest_row(pos.y(), anchor_track.kind)
            if row is not None:
                lst = seq.video_tracks if anchor_track.kind == "video" else seq.audio_tracks
                delta = lst.index(row.track) - lst.index(anchor_track)
                for cid in d["ids"]:
                    tr = seq.track(orig[cid][1])
                    if tr.kind != anchor_track.kind:
                        continue
                    i = lst.index(tr) + delta
                    if i < 0:
                        delta -= i
                    elif i >= len(lst):
                        delta -= i - len(lst) + 1
        preview = {}
        for cid in d["ids"]:
            s, tid = orig[cid]
            tr = seq.track(tid)
            if anchor_track is not None and tr.kind == anchor_track.kind and delta:
                lst = seq.video_tracks if tr.kind == "video" else seq.audio_tracks
                tid = lst[lst.index(tr) + delta].id
            preview[cid] = (s + dt, tid)
        d["preview"] = preview
        self.update()

    # -- trim
    def _update_trim(self, pos: QPointF) -> None:
        d = self._drag
        clip = self.seq.clips.get(d["id"])
        if clip is None:
            return
        t = self._snap(self.t_of(pos.x()), exclude=[clip.id])
        tmp = clip.copy(new_identity=False)
        if d["edge"] == "left":
            edits.trim_start(tmp, t, self.c.project)
        else:
            edits.trim_end(tmp, t, self.c.project)
        d["preview"] = (clip, tmp.start, tmp.end)
        self.update()

    # ------------------------------------------------------------------ context menu
    def contextMenuEvent(self, e):
        pos = QPointF(e.pos())
        clip = self.clip_at(pos)
        row = self.row_at(pos.y())
        m = QMenu(self)
        if clip is not None:
            if clip.id not in self.c.selection:
                self.c.select([clip.id])
            m.addAction("Cut at playhead", lambda: self.c.cut_at(self.c.playhead))
            m.addAction("Delete", lambda: self.c.delete_selection())
            m.addAction("Ripple delete", lambda: self.c.delete_selection(ripple=True))
            if clip.link_id:
                part = "audio" if clip.kind == "audio" else "video"
                m.addAction(f"Delete {part} only (keep the linked part)",
                            lambda: self.c.delete_clips([clip.id]))
            m.addSeparator()
            m.addAction("Join selected clips (close gaps)", self.c.join_selected)
            if any(self.c.clip(cid) and self.c.clip(cid).link_id for cid in self.c.selection):
                m.addAction(icon("unlink"), "Unlink audio and video\tCtrl+L",
                            lambda: self.c.link_selected(False))
            elif len(self.c.selection) > 1:
                m.addAction(icon("link"), "Link selected clips\tCtrl+L",
                            lambda: self.c.link_selected(True))
            m.addSeparator()
            m.addAction("Effect controls…", lambda: self.clip_activated.emit(clip.id))
        elif row is not None:
            t = self.t_of(pos.x())
            gap = edits.gap_at(self.seq, row.track.id, t)
            act = m.addAction("Close gap", lambda: self.c.remove_gap(row.track.id, t))
            act.setEnabled(gap is not None)
            if row.track.kind == "video":
                m.addAction("Add text here", lambda: self.c.add_text(row.track.id, t))
            m.addSeparator()
            m.addAction("Add video track", lambda: self.c.add_track("video"))
            m.addAction("Add audio track", lambda: self.c.add_track("audio"))
        else:
            return
        m.exec(e.globalPos())

    # ------------------------------------------------------------------ wheel
    def wheelEvent(self, e):
        delta = e.angleDelta()
        dy = delta.y() or delta.x()
        if e.modifiers() & Qt.ControlModifier:
            self.zoom(1.15 if dy > 0 else 1 / 1.15, e.position().x())
        elif e.modifiers() & Qt.ShiftModifier or abs(delta.x()) > abs(delta.y()):
            self.hscroll = max(0.0, self.hscroll - dy)
            self.view_changed.emit()
            self.update()
        else:
            max_v = max(0.0, self.content_h() + RULER_H - self.height())
            if max_v > 0:
                self.vscroll = min(max_v, max(0.0, self.vscroll - dy / 2))
            else:
                self.hscroll = max(0.0, self.hscroll - dy)
            self.view_changed.emit()
            self.update()
        e.accept()

    # ------------------------------------------------------------------ drag & drop
    def dragEnterEvent(self, e):
        md = e.mimeData()
        if md.hasFormat(MEDIA_MIME) or md.hasFormat(EFFECT_MIME) or md.hasUrls():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dragMoveEvent(self, e):
        md = e.mimeData()
        pos = e.position()
        self._drop_ghosts = []
        self._drop_clip = None
        t = self.t_of(pos.x())
        if md.hasFormat(MEDIA_MIME):
            ids = decode_ids(md.data(MEDIA_MIME))
            self._mode = "drop"
            row = self.row_at(pos.y()) or self.nearest_row(pos.y())
            t = self._snap(t)
            cursor = t
            for mid in ids:
                m = self.c.project.media.get(mid)
                if m is None:
                    continue
                v_id, a_id = self.c._target_tracks(row.track.id if row else None, m)
                a, b = m.marked_range()
                dur = b - a
                if v_id:
                    self._drop_ghosts.append((v_id, cursor, cursor + dur, "video"))
                if a_id:
                    self._drop_ghosts.append((a_id, cursor, cursor + dur, "audio"))
                cursor += dur
            e.acceptProposedAction()
        elif md.hasFormat(EFFECT_MIME):
            key = bytes(md.data(EFFECT_MIME)).decode("utf-8")
            if key.startswith("text:"):
                row = self.row_at(pos.y())
                if row is not None and row.track.kind == "video":
                    self._drop_ghosts.append((row.track.id, self._snap(t), self._snap(t) + 5.0, "video"))
                    e.acceptProposedAction()
                else:
                    e.ignore()
            else:
                clip = self.clip_at(pos)
                if clip is not None:
                    self._drop_clip = clip.id
                    e.acceptProposedAction()
                else:
                    e.ignore()
        elif md.hasUrls():
            e.acceptProposedAction()
        self.update()

    def dragLeaveEvent(self, e):
        self._drop_ghosts = []
        self._drop_clip = None
        self._mode = None
        self._snap_t = None
        self.update()

    def dropEvent(self, e):
        md = e.mimeData()
        pos = e.position()
        t = self._snap(self.t_of(pos.x()))
        row = self.row_at(pos.y()) or self.nearest_row(pos.y())
        insert = bool(e.modifiers() & Qt.ControlModifier)
        if md.hasFormat(MEDIA_MIME):
            ids = decode_ids(md.data(MEDIA_MIME))
            self.c.add_media_to_timeline(ids, row.track.id if row else None, t, insert=insert)
        elif md.hasFormat(EFFECT_MIME):
            key = bytes(md.data(EFFECT_MIME)).decode("utf-8")
            if key.startswith("text:"):
                if row is not None and row.track.kind == "video":
                    self.c.add_text(row.track.id, t, key.split(":", 1)[1])
            else:
                clip = self.clip_at(pos)
                if clip is not None:
                    self.c.apply_effect(clip.id, key, self.t_of(pos.x()))
                    self.clip_activated.emit(clip.id)
        elif md.hasUrls():
            paths = [u.toLocalFile() for u in md.urls() if u.isLocalFile()]
            added, errors = self.c.import_files(paths)
            if added:
                self.c.add_media_to_timeline([m.id for m in added], row.track.id if row else None, t, insert)
            if errors:
                self.c.message.emit("; ".join(errors))
        e.acceptProposedAction()
        self.dragLeaveEvent(None)


# ============================================================================ panel
def _pps_to_slider(pps: float) -> int:
    return int(round(1000 * math.log(pps / MIN_PPS) / math.log(MAX_PPS / MIN_PPS)))


def _slider_to_pps(v: int) -> float:
    return MIN_PPS * (MAX_PPS / MIN_PPS) ** (v / 1000.0)


class TimelinePanel(QWidget):
    clip_activated = Signal(str)

    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.c = controller
        self._last_playhead = controller.playhead
        self.canvas = TimelineCanvas(controller)
        self.headers = HeaderColumn(controller)
        self.hbar = QScrollBar(Qt.Horizontal)
        self.vbar = QScrollBar(Qt.Vertical)
        self.zoom_slider = QSlider(Qt.Horizontal)
        self.zoom_slider.setRange(0, 1000)
        self.zoom_slider.setFixedWidth(160)
        self.zoom_slider.setToolTip("Timeline zoom (+ / -)")
        self.snap_btn = QToolButton()
        self.snap_btn.setIcon(icon("magnet"))
        self.snap_btn.setCheckable(True)
        self.snap_btn.setChecked(controller.snapping)
        self.snap_btn.setToolTip("Snapping (S)")
        self.snap_btn.toggled.connect(controller.set_snapping)
        controller.snapping_changed.connect(self.snap_btn.setChecked)
        self.link_btn = QToolButton()
        self.link_btn.setIcon(icon("link"))
        self.link_btn.setCheckable(True)
        self.link_btn.setChecked(controller.linked_selection)
        self.link_btn.setToolTip("Linked selection: when on, clicking a clip also selects its linked "
                                 "audio/video.\nTurn off (or Alt+click) to edit audio and video separately.")
        self.link_btn.toggled.connect(controller.set_linked_selection)
        controller.linked_selection_changed.connect(self._sync_link_btn)

        def tb(name, tip, slot):
            b = QToolButton()
            b.setIcon(icon(name))
            b.setToolTip(tip)
            b.setAutoRaise(True)
            b.clicked.connect(slot)
            return b

        top = QHBoxLayout()
        top.setContentsMargins(6, 2, 6, 2)
        self.seq_label = QLabel()
        self.seq_label.setStyleSheet("color:#8c9098;")
        top.addWidget(self.seq_label)
        top.addStretch(1)
        top.addWidget(self.link_btn)
        top.addWidget(self.snap_btn)
        top.addSpacing(10)
        top.addWidget(tb("minus", "Zoom out (-)", lambda: self.canvas.zoom(1 / 1.25)))
        top.addWidget(self.zoom_slider)
        top.addWidget(tb("plus", "Zoom in (+)", lambda: self.canvas.zoom(1.25)))
        top.addWidget(tb("fit", "Zoom to fit (\\)", self.canvas.zoom_to_fit))

        grid = QHBoxLayout()
        grid.setContentsMargins(0, 0, 0, 0)
        grid.setSpacing(0)
        grid.addWidget(self.headers)
        grid.addWidget(self.canvas, 1)
        grid.addWidget(self.vbar)

        bottom = QHBoxLayout()
        bottom.setContentsMargins(HEADER_W, 0, self.vbar.sizeHint().width(), 0)
        bottom.addWidget(self.hbar)

        lay = QVBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(0)
        lay.addLayout(top)
        lay.addLayout(grid, 1)
        lay.addLayout(bottom)

        self.zoom_slider.valueChanged.connect(self._slider_zoom)
        self.hbar.valueChanged.connect(self._hbar_moved)
        self.vbar.valueChanged.connect(self._vbar_moved)
        self.canvas.view_changed.connect(self._sync_bars)
        self.canvas.clip_activated.connect(self.clip_activated)

        controller.project_changed.connect(self._project_changed)
        controller.project_replaced.connect(self._project_replaced)
        controller.selection_changed.connect(self.canvas.update)
        controller.playhead_changed.connect(self._playhead_changed)
        controller.tool_changed.connect(lambda _: self.canvas.update())
        controller.audio_loaded.connect(lambda _: self.canvas.update())
        self._project_changed()
        QTimer.singleShot(0, self._sync_bars)

    # ------------------------------------------------------------------ sync
    def _sync_link_btn(self, on: bool) -> None:
        self.link_btn.blockSignals(True)
        self.link_btn.setChecked(on)
        self.link_btn.blockSignals(False)

    def _project_changed(self) -> None:
        self.headers.rebuild()
        seq = self.c.project.sequence
        self.seq_label.setText(f"Sequence  {seq.width}×{seq.height}  {seq.fps:g} fps  ·  "
                               f"{timecode(seq.duration, seq.fps)}")
        self._sync_bars()
        self.canvas.update()

    def _project_replaced(self) -> None:
        self.canvas.hscroll = 0.0
        self.canvas.vscroll = 0.0
        self._project_changed()

    def _playhead_changed(self, t: float) -> None:
        seq = self.c.project.sequence
        self.headers.tc.setText(timecode(t, seq.fps))
        old = self._last_playhead
        self._last_playhead = t
        if self.canvas.ensure_playhead_visible():
            self.canvas.update()
        else:
            self.canvas.update_playhead(old, t)

    def _sync_bars(self) -> None:
        cv = self.canvas
        self.hbar.blockSignals(True)
        self.hbar.setRange(0, int(max(0.0, cv.content_width() - cv.width())))
        self.hbar.setPageStep(max(1, cv.width()))
        self.hbar.setValue(int(cv.hscroll))
        self.hbar.blockSignals(False)
        max_v = int(max(0.0, cv.content_h() + RULER_H - cv.height()))
        cv.vscroll = min(cv.vscroll, max_v)
        self.vbar.blockSignals(True)
        self.vbar.setRange(0, max_v)
        self.vbar.setPageStep(max(1, cv.height()))
        self.vbar.setValue(int(cv.vscroll))
        self.vbar.blockSignals(False)
        self.vbar.setVisible(max_v > 0)
        self.zoom_slider.blockSignals(True)
        self.zoom_slider.setValue(_pps_to_slider(cv.pps))
        self.zoom_slider.blockSignals(False)
        self.headers.vscroll = cv.vscroll
        self.headers.relayout()

    def _slider_zoom(self, v: int) -> None:
        self.canvas.set_pps(_slider_to_pps(v))

    def _hbar_moved(self, v: int) -> None:
        self.canvas.hscroll = float(v)
        self.canvas.update()

    def _vbar_moved(self, v: int) -> None:
        self.canvas.vscroll = float(v)
        self.headers.vscroll = float(v)
        self.headers.relayout()
        self.canvas.update()

    def resizeEvent(self, e):
        super().resizeEvent(e)
        QTimer.singleShot(0, self._sync_bars)

    # ------------------------------------------------------------------ public
    def zoom_in(self) -> None:
        self.canvas.zoom(1.25)

    def zoom_out(self) -> None:
        self.canvas.zoom(1 / 1.25)

    def zoom_to_fit(self) -> None:
        self.canvas.zoom_to_fit()

