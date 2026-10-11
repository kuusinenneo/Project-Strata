"""Stereo audio level meters with dB scale and peak hold."""
from __future__ import annotations

import math

from PySide6.QtCore import QElapsedTimer, QRectF, Qt, QTimer
from PySide6.QtGui import QColor, QFont, QLinearGradient, QPainter
from PySide6.QtWidgets import QSizePolicy, QWidget

from . import theme

MIN_DB = -60.0


def to_db(v: float) -> float:
    return MIN_DB if v <= 1e-6 else max(MIN_DB, 20 * math.log10(v))


class AudioMeters(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.level = [MIN_DB, MIN_DB]
        self.peak = [MIN_DB, MIN_DB]
        self.clip = [False, False]
        self._peak_age = [0.0, 0.0]
        self.setMinimumWidth(56)
        self.setSizePolicy(QSizePolicy.Preferred, QSizePolicy.Expanding)
        self.font_ = QFont()
        self.font_.setPixelSize(9)
        self._clock = QElapsedTimer()
        self._clock.start()
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._decay)
        self._timer.start(33)
        self.setToolTip("Audio levels (click to reset clip indicators)")

    def set_levels(self, left: float, right: float) -> None:
        for i, v in enumerate((left, right)):
            db = to_db(v)
            if db >= self.level[i]:
                self.level[i] = db
            if db >= self.peak[i]:
                self.peak[i] = db
                self._peak_age[i] = 0.0
            if v >= 1.0:
                self.clip[i] = True

    def _decay(self) -> None:
        dt = self._clock.restart() / 1000.0
        changed = False
        for i in range(2):
            if self.level[i] > MIN_DB:
                self.level[i] = max(MIN_DB, self.level[i] - 30.0 * dt)  # 30 dB/s fall
                changed = True
            self._peak_age[i] += dt
            if self._peak_age[i] > 1.2 and self.peak[i] > MIN_DB:
                self.peak[i] = max(MIN_DB, self.peak[i] - 20.0 * dt)
                changed = True
        if changed:
            self.update()

    def mousePressEvent(self, _):
        self.clip = [False, False]
        self.update()

    def _y(self, db: float, top: float, height: float) -> float:
        return top + height * (1 - (db - MIN_DB) / -MIN_DB)

    def paintEvent(self, _):
        p = QPainter(self)
        p.fillRect(self.rect(), theme.BG_PANEL)
        top, bottom = 14.0, self.height() - 6.0
        height = bottom - top
        scale_w = 22
        bar_w = max(6.0, (self.width() - scale_w - 12) / 2)
        p.setFont(self.font_)
        p.setPen(theme.TEXT_DIM)
        for db in (0, -6, -12, -18, -24, -36, -48, -60):
            y = self._y(db, top, height)
            p.drawText(QRectF(0, y - 6, scale_w - 3, 12), Qt.AlignRight | Qt.AlignVCenter, str(db))
        grad = QLinearGradient(0, top, 0, bottom)
        grad.setColorAt(0.0, QColor(255, 70, 70))
        grad.setColorAt(0.1, QColor(255, 210, 60))
        grad.setColorAt(0.3, QColor(90, 220, 110))
        grad.setColorAt(1.0, QColor(40, 150, 80))
        for i in range(2):
            x = scale_w + 4 + i * (bar_w + 4)
            p.fillRect(QRectF(x, top, bar_w, height), QColor(16, 17, 19))
            y = self._y(self.level[i], top, height)
            p.fillRect(QRectF(x, y, bar_w, bottom - y), grad)
            if self.peak[i] > MIN_DB:
                py = self._y(self.peak[i], top, height)
                p.fillRect(QRectF(x, py - 1, bar_w, 2), QColor(240, 240, 240))
            p.fillRect(QRectF(x, 2, bar_w, 8), QColor(255, 60, 60) if self.clip[i] else QColor(50, 30, 30))
            p.setPen(theme.TEXT_DIM)
        p.end()
