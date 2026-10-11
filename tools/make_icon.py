"""Generate assets/strata.ico (16-256 px) from Strata's own logo drawing.

Usage: python tools/make_icon.py
"""
from __future__ import annotations

import os
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

SIZES = [16, 24, 32, 48, 64, 128, 256]


def render_png(size: int) -> bytes:
    from PySide6.QtCore import QBuffer, QByteArray, QIODevice, QRectF, Qt
    from PySide6.QtGui import QColor, QImage, QLinearGradient, QPainter

    from strata.ui.icons import _draw

    img = QImage(size, size, QImage.Format_ARGB32)
    img.fill(Qt.transparent)
    p = QPainter(img)
    p.setRenderHint(QPainter.Antialiasing, True)
    p.scale(size / 24.0, size / 24.0)
    grad = QLinearGradient(0, 0, 0, 24)
    grad.setColorAt(0.0, QColor(44, 48, 58))
    grad.setColorAt(1.0, QColor(20, 22, 27))
    p.setPen(Qt.NoPen)
    p.setBrush(grad)
    p.drawRoundedRect(QRectF(0.5, 0.5, 23, 23), 5, 5)
    p.translate(12, 12.6)
    p.scale(0.92, 0.92)
    p.translate(-12, -12)
    _draw("logo", p, QColor(64, 156, 255))
    p.end()
    data = QByteArray()
    buf = QBuffer(data)
    buf.open(QIODevice.WriteOnly)
    img.save(buf, "PNG")
    buf.close()
    return bytes(data)


def write_ico(path: Path, pngs: dict[int, bytes]) -> None:
    """ICO container with PNG-compressed entries (supported since Windows Vista)."""
    count = len(pngs)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries, blobs = b"", b""
    for size, png in sorted(pngs.items()):
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset)
        blobs += png
        offset += len(png)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + entries + blobs)


def main() -> None:
    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    from PySide6.QtGui import QGuiApplication
    app = QGuiApplication.instance() or QGuiApplication([])  # needed for QPainter
    out = ROOT / "assets" / "strata.ico"
    write_ico(out, {s: render_png(s) for s in SIZES})
    (ROOT / "assets" / "strata.png").write_bytes(render_png(256))
    print(f"wrote {out}")
    app.quit()


if __name__ == "__main__":
    main()
