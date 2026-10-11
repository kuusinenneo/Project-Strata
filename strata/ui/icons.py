"""Strata's own icon set, drawn with QPainter (no external image files)."""
from __future__ import annotations

import functools

from PySide6.QtCore import QPointF, QRectF, Qt
from PySide6.QtGui import QColor, QFont, QIcon, QPainter, QPainterPath, QPen, QPixmap, QPolygonF

FG = QColor(214, 216, 220)
FG_ACTIVE = QColor(255, 255, 255)
ACCENT = QColor(64, 156, 255)


def _poly(p: QPainter, pts, color=FG):
    p.setBrush(color)
    p.setPen(Qt.NoPen)
    p.drawPolygon(QPolygonF([QPointF(x, y) for x, y in pts]))


def _pen(color=FG, w=1.6):
    pen = QPen(color, w)
    pen.setCapStyle(Qt.RoundCap)
    pen.setJoinStyle(Qt.RoundJoin)
    return pen


def _draw(name: str, p: QPainter, c: QColor) -> None:  # noqa: C901 - one branch per icon
    """Draw icon ``name`` in a 24x24 coordinate space."""
    if name == "play":
        _poly(p, [(7, 5), (19, 12), (7, 19)], c)
    elif name == "pause":
        p.fillRect(QRectF(7, 5, 3.5, 14), c)
        p.fillRect(QRectF(13.5, 5, 3.5, 14), c)
    elif name == "stop":
        p.fillRect(QRectF(7, 7, 10, 10), c)
    elif name == "step_back":
        p.fillRect(QRectF(6, 6, 2.2, 12), c)
        _poly(p, [(18, 6), (9, 12), (18, 18)], c)
    elif name == "step_fwd":
        p.fillRect(QRectF(15.8, 6, 2.2, 12), c)
        _poly(p, [(6, 6), (15, 12), (6, 18)], c)
    elif name == "to_start":
        p.fillRect(QRectF(5, 6, 2.2, 12), c)
        _poly(p, [(13, 6), (7.5, 12), (13, 18)], c)
        _poly(p, [(19, 6), (13.5, 12), (19, 18)], c)
    elif name == "to_end":
        p.fillRect(QRectF(16.8, 6, 2.2, 12), c)
        _poly(p, [(5, 6), (10.5, 12), (5, 18)], c)
        _poly(p, [(11, 6), (16.5, 12), (11, 18)], c)
    elif name == "mark_in":
        p.setPen(_pen(c, 2))
        p.drawPolyline(QPolygonF([QPointF(15, 5), QPointF(9, 5), QPointF(9, 19), QPointF(15, 19)]))
    elif name == "mark_out":
        p.setPen(_pen(c, 2))
        p.drawPolyline(QPolygonF([QPointF(9, 5), QPointF(15, 5), QPointF(15, 19), QPointF(9, 19)]))
    elif name == "select":
        _poly(p, [(7, 3), (7, 19), (11, 15.5), (14, 21), (16.5, 20), (13.5, 14.5), (19, 14)], c)
    elif name == "razor":
        p.setPen(_pen(c, 1.6))
        p.setBrush(Qt.NoBrush)
        p.drawEllipse(QPointF(7, 17), 3, 3)
        p.drawEllipse(QPointF(17, 17), 3, 3)
        p.drawLine(QPointF(9, 15), QPointF(17, 4))
        p.drawLine(QPointF(15, 15), QPointF(7, 4))
    elif name == "ripple":
        p.setPen(_pen(c, 1.8))
        p.drawLine(QPointF(4, 6), QPointF(4, 18))
        p.drawLine(QPointF(20, 6), QPointF(20, 18))
        _poly(p, [(16, 12), (11, 8), (11, 16)], c)
        p.drawLine(QPointF(7, 12), QPointF(12, 12))
    elif name == "text":
        f = QFont("Segoe UI", 1)
        f.setPixelSize(19)
        f.setBold(True)
        p.setFont(f)
        p.setPen(c)
        p.drawText(QRectF(0, 0, 24, 24), Qt.AlignCenter, "T")
    elif name == "eye":
        p.setPen(_pen(c, 1.5))
        p.setBrush(Qt.NoBrush)
        path = QPainterPath(QPointF(3, 12))
        path.quadTo(12, 3, 21, 12)
        path.quadTo(12, 21, 3, 12)
        p.drawPath(path)
        p.setBrush(c)
        p.drawEllipse(QPointF(12, 12), 2.6, 2.6)
    elif name == "eye_off":
        _draw("eye", p, QColor(c.red(), c.green(), c.blue(), 90))
        p.setPen(_pen(c, 1.8))
        p.drawLine(QPointF(5, 19), QPointF(19, 5))
    elif name in ("speaker", "speaker_off"):
        _poly(p, [(4, 9), (8, 9), (13, 5), (13, 19), (8, 15), (4, 15)], c)
        p.setPen(_pen(c, 1.5))
        p.setBrush(Qt.NoBrush)
        if name == "speaker":
            p.drawArc(QRectF(11, 8, 6, 8), -60 * 16, 120 * 16)
            p.drawArc(QRectF(11, 5, 10, 14), -60 * 16, 120 * 16)
        else:
            p.drawLine(QPointF(16, 9), QPointF(21, 15))
            p.drawLine(QPointF(21, 9), QPointF(16, 15))
    elif name in ("lock", "unlock"):
        p.setPen(_pen(c, 1.8))
        p.setBrush(Qt.NoBrush)
        if name == "lock":
            p.drawArc(QRectF(8, 4, 8, 10), 0, 180 * 16)
            p.drawLine(QPointF(8, 9), QPointF(8, 11))
            p.drawLine(QPointF(16, 9), QPointF(16, 11))
        else:
            p.drawArc(QRectF(12, 3, 8, 10), 0, 180 * 16)
            p.drawLine(QPointF(20, 8), QPointF(20, 10))
        p.setPen(Qt.NoPen)
        p.setBrush(c)
        p.drawRoundedRect(QRectF(6, 11, 12, 9), 1.5, 1.5)
    elif name == "solo":
        f = QFont("Segoe UI", 1)
        f.setPixelSize(15)
        f.setBold(True)
        p.setFont(f)
        p.setPen(c)
        p.drawText(QRectF(0, 0, 24, 24), Qt.AlignCenter, "S")
    elif name == "plus":
        p.setPen(_pen(c, 2))
        p.drawLine(QPointF(12, 5), QPointF(12, 19))
        p.drawLine(QPointF(5, 12), QPointF(19, 12))
    elif name == "minus":
        p.setPen(_pen(c, 2))
        p.drawLine(QPointF(5, 12), QPointF(19, 12))
    elif name == "close":
        p.setPen(_pen(c, 2))
        p.drawLine(QPointF(7, 7), QPointF(17, 17))
        p.drawLine(QPointF(17, 7), QPointF(7, 17))
    elif name in ("link", "unlink"):
        p.setPen(_pen(c, 1.8))
        p.setBrush(Qt.NoBrush)
        if name == "link":
            p.drawRoundedRect(QRectF(3, 9, 10, 6), 3, 3)
            p.drawRoundedRect(QRectF(11, 9, 10, 6), 3, 3)
        else:
            p.drawRoundedRect(QRectF(2, 9, 9, 6), 3, 3)
            p.drawRoundedRect(QRectF(13, 9, 9, 6), 3, 3)
            p.drawLine(QPointF(12, 4), QPointF(12, 7))
            p.drawLine(QPointF(12, 17), QPointF(12, 20))
    elif name == "magnet":
        p.setPen(_pen(c, 3))
        p.setBrush(Qt.NoBrush)
        p.drawArc(QRectF(6, 6, 12, 12), 180 * 16, 180 * 16)
        p.drawLine(QPointF(6, 12), QPointF(6, 5))
        p.drawLine(QPointF(18, 12), QPointF(18, 5))
    elif name == "import":
        p.setPen(_pen(c, 1.6))
        p.setBrush(Qt.NoBrush)
        p.drawPolyline(QPolygonF([QPointF(4, 13), QPointF(4, 20), QPointF(20, 20), QPointF(20, 13)]))
        p.drawLine(QPointF(12, 3), QPointF(12, 15))
        p.drawPolyline(QPolygonF([QPointF(8, 11), QPointF(12, 15), QPointF(16, 11)]))
    elif name == "trash":
        p.setPen(_pen(c, 1.6))
        p.setBrush(Qt.NoBrush)
        p.drawLine(QPointF(5, 7), QPointF(19, 7))
        p.drawLine(QPointF(10, 4), QPointF(14, 4))
        p.drawPolyline(QPolygonF([QPointF(7, 7), QPointF(8, 20), QPointF(16, 20), QPointF(17, 7)]))
    elif name == "film":
        p.setPen(_pen(c, 1.5))
        p.setBrush(Qt.NoBrush)
        p.drawRoundedRect(QRectF(4, 5, 16, 14), 2, 2)
        for y in (7.5, 11, 14.5):
            p.fillRect(QRectF(5.5, y, 2, 1.8), c)
            p.fillRect(QRectF(16.5, y, 2, 1.8), c)
    elif name == "audio":
        p.setPen(_pen(c, 1.6))
        for i, h in enumerate([4, 9, 14, 8, 11, 5]):
            x = 5 + i * 2.8
            p.drawLine(QPointF(x, 12 - h / 2), QPointF(x, 12 + h / 2))
    elif name == "image":
        p.setPen(_pen(c, 1.5))
        p.setBrush(Qt.NoBrush)
        p.drawRoundedRect(QRectF(4, 5, 16, 14), 2, 2)
        _poly(p, [(6, 17), (11, 11), (14, 14), (16, 12), (18, 17)], c)
        p.setBrush(c)
        p.drawEllipse(QPointF(16, 8.5), 1.5, 1.5)
    elif name == "fx":
        f = QFont("Segoe UI", 1)
        f.setPixelSize(14)
        f.setItalic(True)
        f.setBold(True)
        p.setFont(f)
        p.setPen(c)
        p.drawText(QRectF(0, 0, 24, 24), Qt.AlignCenter, "fx")
    elif name == "transition":
        _poly(p, [(4, 6), (14, 6), (10, 18), (4, 18)], QColor(c.red(), c.green(), c.blue(), 110))
        _poly(p, [(14, 6), (20, 6), (20, 18), (10, 18)], c)
    elif name == "logo":
        # three stacked strata
        for i, col in enumerate([QColor(64, 156, 255), QColor(110, 190, 255), QColor(170, 220, 255)]):
            y = 15 - i * 5
            _poly(p, [(3, y + 3), (12, y - 1), (21, y + 3), (12, y + 7)], col)
    elif name == "export":
        p.setPen(_pen(c, 1.6))
        p.setBrush(Qt.NoBrush)
        p.drawPolyline(QPolygonF([QPointF(4, 13), QPointF(4, 20), QPointF(20, 20), QPointF(20, 13)]))
        p.drawLine(QPointF(12, 15), QPointF(12, 3))
        p.drawPolyline(QPolygonF([QPointF(8, 7), QPointF(12, 3), QPointF(16, 7)]))
    elif name == "fit":
        p.setPen(_pen(c, 1.6))
        p.drawLine(QPointF(4, 12), QPointF(20, 12))
        _poly(p, [(4, 12), (8, 8.5), (8, 15.5)], c)
        _poly(p, [(20, 12), (16, 8.5), (16, 15.5)], c)
    elif name == "insert":
        p.fillRect(QRectF(4, 8, 16, 8), QColor(c.red(), c.green(), c.blue(), 80))
        _poly(p, [(12, 15), (8, 4), (16, 4)], c)
    elif name == "overwrite":
        p.fillRect(QRectF(4, 12, 16, 8), QColor(c.red(), c.green(), c.blue(), 80))
        _poly(p, [(12, 13), (8, 3), (16, 3)], c)
    else:  # unknown: small circle
        p.setBrush(c)
        p.setPen(Qt.NoPen)
        p.drawEllipse(QPointF(12, 12), 4, 4)


@functools.lru_cache(maxsize=None)
def icon(name: str, color: str | None = None) -> QIcon:
    base = QColor(color) if color else FG
    ic = QIcon()
    for size in (16, 24, 32, 48):
        for mode, col in ((QIcon.Normal, base), (QIcon.Disabled, QColor(100, 103, 110)),
                          (QIcon.Active, FG_ACTIVE if not color else base)):
            pm = QPixmap(size, size)
            pm.fill(Qt.transparent)
            p = QPainter(pm)
            p.setRenderHint(QPainter.Antialiasing, True)
            p.setRenderHint(QPainter.TextAntialiasing, True)
            p.scale(size / 24.0, size / 24.0)
            _draw(name, p, col)
            p.end()
            ic.addPixmap(pm, mode, QIcon.Off)
    return ic


def pixmap(name: str, size: int = 24, color: str | None = None) -> QPixmap:
    return icon(name, color).pixmap(size, size)

