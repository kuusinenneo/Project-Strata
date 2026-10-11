"""Generate the shared Strata icon files.

    python shared/tools/make_icons.py [--preview sheet.png]

Writes
    shared/icons/<name>.svg                 UI/tool icons (from icon_sources.py)
    shared/icons/app/strata-<app>.svg       app logos
    shared/icons/app/strata-<app>.ico/.png  Windows app icons (16-256 px) + 256 px PNG

Needs PySide6 (the Strata Video venv has it) to rasterise the logos.
"""
from __future__ import annotations

import os
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SHARED = HERE.parent
ICONS = SHARED / "icons"
APP = ICONS / "app"
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]

sys.path.insert(0, str(HERE))
from icon_sources import ICONS as SOURCES  # noqa: E402

SVG_HEAD = ('<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" '
            'stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">\n')


def icon_svg(paths) -> str:
    out = [SVG_HEAD]
    for spec in paths:
        if isinstance(spec, str):
            spec = {"d": spec}
        attrs = [f'd="{spec["d"]}"']
        if spec.get("fill"):
            attrs.append('fill="currentColor" stroke="none"')
        if "width" in spec:
            attrs.append(f'stroke-width="{spec["width"]}"')
        if "opacity" in spec:
            attrs.append(f'opacity="{spec["opacity"]}"')
        out.append(f'  <path {" ".join(attrs)}/>\n')
    out.append("</svg>\n")
    return "".join(out)


# Both logos: three stacked "strata" on a dark rounded tile; the colour tells the apps apart.
LOGO_COLORS = {
    "video": ("#409cff", "#6ebeff", "#aadcff"),   # blue
    "photo": ("#ff8a3d", "#ffb06e", "#ffd6aa"),   # amber
}


def logo_svg(app: str) -> str:
    c0, c1, c2 = LOGO_COLORS[app]
    layers = []
    for i, col in enumerate((c0, c1, c2)):
        y = 15 - i * 5
        layers.append(f'    <polygon points="3,{y + 3} 12,{y - 1} 21,{y + 3} 12,{y + 7}" fill="{col}"/>\n')
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 24 24">\n'
        '  <defs><linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">'
        '<stop offset="0" stop-color="#2c303a"/><stop offset="1" stop-color="#14161b"/></linearGradient></defs>\n'
        '  <rect x="0.5" y="0.5" width="23" height="23" rx="5" fill="url(#bg)"/>\n'
        '  <g transform="translate(12 12.6) scale(0.92) translate(-12 -12)">\n'
        + "".join(layers) +
        "  </g>\n</svg>\n"
    )


def render_png(svg: str, size: int, color: str | None = None) -> bytes:
    from PySide6.QtCore import QBuffer, QByteArray, QIODevice, Qt
    from PySide6.QtGui import QImage, QPainter
    from PySide6.QtSvg import QSvgRenderer

    if color:
        svg = svg.replace("currentColor", color)
    renderer = QSvgRenderer(QByteArray(svg.encode()))
    img = QImage(size, size, QImage.Format_ARGB32)
    img.fill(Qt.transparent)
    p = QPainter(img)
    p.setRenderHint(QPainter.Antialiasing, True)
    renderer.render(p)
    p.end()
    data = QByteArray()
    buf = QBuffer(data)
    buf.open(QIODevice.WriteOnly)
    img.save(buf, "PNG")
    buf.close()
    return bytes(data)


def write_ico(path: Path, pngs: dict[int, bytes]) -> None:
    """ICO container with PNG-compressed entries (supported since Windows Vista)."""
    header = struct.pack("<HHH", 0, 1, len(pngs))
    offset = 6 + 16 * len(pngs)
    entries, blobs = b"", b""
    for size, png in sorted(pngs.items()):
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset)
        blobs += png
        offset += len(png)
    path.write_bytes(header + entries + blobs)


def write_preview(path: Path) -> None:
    """Contact sheet of every icon (for eyeballing changes)."""
    from PySide6.QtCore import QByteArray, QRectF, Qt
    from PySide6.QtGui import QColor, QImage, QPainter
    from PySide6.QtSvg import QSvgRenderer

    names = sorted(SOURCES)
    cols, cell = 10, 64
    rows = (len(names) + cols - 1) // cols
    img = QImage(cols * cell, rows * (cell + 14), QImage.Format_ARGB32)
    img.fill(QColor("#1e1f22"))
    p = QPainter(img)
    p.setRenderHint(QPainter.Antialiasing, True)
    for i, name in enumerate(names):
        x, y = (i % cols) * cell, (i // cols) * (cell + 14)
        svg = icon_svg(SOURCES[name]).replace("currentColor", "#d6d8dc")
        QSvgRenderer(QByteArray(svg.encode())).render(p, QRectF(x + 12, y + 6, 40, 40))
        p.setPen(QColor("#8c9098"))
        p.drawText(QRectF(x, y + 50, cell, 24), Qt.AlignHCenter | Qt.AlignTop, name)
    p.end()
    img.save(str(path))


def main() -> None:
    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    from PySide6.QtGui import QGuiApplication
    app = QGuiApplication.instance() or QGuiApplication([])  # QPainter needs an app

    ICONS.mkdir(parents=True, exist_ok=True)
    APP.mkdir(parents=True, exist_ok=True)
    for name, paths in SOURCES.items():
        (ICONS / f"{name}.svg").write_text(icon_svg(paths), encoding="utf-8")
    for name in LOGO_COLORS:
        svg = logo_svg(name)
        stem = APP / f"strata-{name}"
        stem.with_suffix(".svg").write_text(svg, encoding="utf-8")
        write_ico(stem.with_suffix(".ico"), {s: render_png(svg, s) for s in ICO_SIZES})
        stem.with_suffix(".png").write_bytes(render_png(svg, 256))
    print(f"wrote {len(SOURCES)} icons and {len(LOGO_COLORS)} app logos to {ICONS}")
    if "--preview" in sys.argv:
        write_preview(Path(sys.argv[sys.argv.index("--preview") + 1]))
    app.quit()


if __name__ == "__main__":
    main()
