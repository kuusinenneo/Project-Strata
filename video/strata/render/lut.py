"""3D colour lookup tables (.cube files, as exported by Resolve, Premiere, etc.)."""
from __future__ import annotations

import os
import threading
from typing import Optional

import numpy as np


class CubeLUT:
    def __init__(self, table: np.ndarray, domain_min=(0.0, 0.0, 0.0), domain_max=(1.0, 1.0, 1.0),
                 title: str = ""):
        self.size = table.shape[0]
        self.table = table.astype(np.float32)  # [b][g][r] -> rgb, values in output range 0..1
        self.flat = self.table.reshape(-1, 3)
        self.domain_min = np.array(domain_min, np.float32)
        self.domain_max = np.array(domain_max, np.float32)
        self.title = title

    def apply(self, rgb: np.ndarray, strength: float = 1.0) -> np.ndarray:
        """Apply to an HxWx3 uint8 array with trilinear interpolation."""
        h, w = rgb.shape[:2]
        n = self.size
        src = rgb.reshape(-1, 3).astype(np.float32) / 255.0
        x = (src - self.domain_min) / np.maximum(self.domain_max - self.domain_min, 1e-6)
        x = np.clip(x, 0.0, 1.0) * (n - 1)
        i0 = np.minimum(x.astype(np.int32), n - 2) if n > 1 else np.zeros_like(x, np.int32)
        f = x - i0
        r0, g0, b0 = i0[:, 0], i0[:, 1], i0[:, 2]
        fr, fg, fb = f[:, 0:1], f[:, 1:2], f[:, 2:3]

        def at(dr, dg, db):
            return self.flat[((b0 + db) * n + (g0 + dg)) * n + (r0 + dr)]

        c00 = at(0, 0, 0) * (1 - fr) + at(1, 0, 0) * fr
        c10 = at(0, 1, 0) * (1 - fr) + at(1, 1, 0) * fr
        c01 = at(0, 0, 1) * (1 - fr) + at(1, 0, 1) * fr
        c11 = at(0, 1, 1) * (1 - fr) + at(1, 1, 1) * fr
        c0 = c00 * (1 - fg) + c10 * fg
        c1 = c01 * (1 - fg) + c11 * fg
        out = c0 * (1 - fb) + c1 * fb
        if strength < 0.999:
            out = src + (out - src) * strength
        return (np.clip(out, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8).reshape(h, w, 3)


def parse_cube(text: str) -> CubeLUT:
    size = 0
    dmin, dmax = (0.0, 0.0, 0.0), (1.0, 1.0, 1.0)
    title = ""
    values: list[list[float]] = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        key = line.split()[0].upper()
        if key == "TITLE":
            title = line[5:].strip().strip('"')
        elif key == "LUT_3D_SIZE":
            size = int(line.split()[1])
        elif key == "LUT_1D_SIZE":
            raise ValueError("1D LUTs are not supported; use a 3D .cube LUT")
        elif key == "DOMAIN_MIN":
            dmin = tuple(float(v) for v in line.split()[1:4])
        elif key == "DOMAIN_MAX":
            dmax = tuple(float(v) for v in line.split()[1:4])
        elif key[0].isdigit() or key[0] in "-.":
            parts = line.split()
            if len(parts) >= 3:
                values.append([float(parts[0]), float(parts[1]), float(parts[2])])
    if size < 2:
        raise ValueError("Not a 3D .cube LUT (LUT_3D_SIZE missing)")
    if len(values) != size ** 3:
        raise ValueError(f"Expected {size ** 3} LUT entries, found {len(values)}")
    table = np.array(values, np.float32).reshape(size, size, size, 3)  # red changes fastest
    return CubeLUT(table, dmin, dmax, title)


_cache: dict[tuple[str, float], CubeLUT] = {}
_lock = threading.Lock()


def load_lut(path: str) -> Optional[CubeLUT]:
    """Load (and cache) a .cube file; None if it is missing or invalid."""
    try:
        key = (os.path.abspath(path), os.path.getmtime(path))
    except OSError:
        return None
    with _lock:
        lut = _cache.get(key)
    if lut is not None:
        return lut
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            lut = parse_cube(f.read())
    except (OSError, ValueError):
        return None
    with _lock:
        _cache[key] = lut
    return lut
