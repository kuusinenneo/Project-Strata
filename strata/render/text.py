"""Text overlay rendering with layout presets.

Font sizes are stored in pixels at 1080p and scaled to the output height, so
the preview (small) and the export (full size) look the same.
"""
from __future__ import annotations

from PySide6.QtCore import QRectF, Qt
from PySide6.QtGui import QColor, QFont, QFontMetricsF, QPainter, QPainterPath

from ..model.project import TextProps

REFERENCE_HEIGHT = 1080.0


def text_layout(props: TextProps, width: int, height: int, offset_x: float = 0.0,
                offset_y: float = 0.0, scale: float = 1.0):
    """Compute (font, text_rect, box_rect, alignment) for a text overlay."""
    s = height / REFERENCE_HEIGHT * max(scale, 0.01)
    font = QFont(props.font_family or "Segoe UI")
    font.setPixelSize(max(1, int(round(props.font_size * s))))
    font.setBold(bool(props.bold))
    font.setItalic(bool(props.italic))
    fm = QFontMetricsF(font)

    preset = props.preset
    align = Qt.AlignLeft if preset == "lower_third" else Qt.AlignHCenter
    max_w = width * (0.6 if preset == "lower_third" else 0.9)
    flags = int(align) | int(Qt.TextWordWrap)
    br = fm.boundingRect(QRectF(0, 0, max_w, height * 4), flags, props.text or " ")
    tw, th = br.width(), br.height()
    pad = font.pixelSize() * 0.35

    if preset == "lower_third":
        x = width * 0.06 + pad
        y = height * 0.80 - th
    elif preset == "top_caption":
        x = (width - tw) / 2
        y = height * 0.07 + pad
    elif preset == "subtitle":
        x = (width - tw) / 2
        y = height * 0.92 - th - pad
    else:  # centered title
        x = (width - tw) / 2
        y = (height - th) / 2
    x += offset_x / 100.0 * width
    y += offset_y / 100.0 * height
    text_rect = QRectF(x, y, tw, th)
    box_rect = text_rect.adjusted(-pad, -pad * 0.6, pad, pad * 0.6)
    return font, text_rect, box_rect, align


def draw_text(p: QPainter, props: TextProps, width: int, height: int, opacity: float = 1.0,
              offset_x: float = 0.0, offset_y: float = 0.0, scale: float = 1.0) -> None:
    if opacity <= 0.0 or not props.text:
        return
    font, text_rect, box_rect, align = text_layout(props, width, height, offset_x, offset_y, scale)
    p.save()
    p.setOpacity(max(0.0, min(1.0, opacity)))
    p.setRenderHint(QPainter.Antialiasing, True)
    p.setRenderHint(QPainter.TextAntialiasing, True)
    flags = int(align) | int(Qt.TextWordWrap)
    if props.bg_box:
        bg = QColor(props.bg_color or "#000000")
        bg.setAlphaF(max(0.0, min(1.0, props.bg_opacity / 100.0)))
        path = QPainterPath()
        radius = font.pixelSize() * 0.12
        path.addRoundedRect(box_rect, radius, radius)
        p.fillPath(path, bg)
    else:
        # soft shadow for legibility over bright footage
        shadow = QColor(0, 0, 0, 150)
        p.setFont(font)
        p.setPen(shadow)
        d = max(1.0, font.pixelSize() / 30.0)
        p.drawText(text_rect.translated(d, d), flags, props.text)
    p.setFont(font)
    p.setPen(QColor(props.color or "#ffffff"))
    p.drawText(text_rect, flags, props.text)
    p.restore()
