"""Per-clip image effects on numpy RGB(A) arrays.

Brightness/contrast are applied through a 256-entry lookup table; saturation,
black & white and sepia are all linear colour transforms and get folded into a
single 3x3 matrix, so any combination costs one LUT pass plus one matmul.
"""
from __future__ import annotations

import numpy as np

from ..model.project import Effects

LUMA = np.array([0.299, 0.587, 0.114], dtype=np.float32)
SEPIA = np.array([[0.393, 0.769, 0.189],
                  [0.349, 0.686, 0.168],
                  [0.272, 0.534, 0.131]], dtype=np.float32)


def _brightness_contrast_lut(brightness: float, contrast: float) -> np.ndarray:
    x = np.arange(256, dtype=np.float32) / 255.0
    if abs(contrast) > 1e-6:
        factor = 1.0 + contrast / 100.0 if contrast < 0 else 1.0 + contrast / 50.0
        x = (x - 0.5) * factor + 0.5
    if abs(brightness) > 1e-6:
        x = x + brightness / 200.0
    return (np.clip(x, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def color_matrix(fx: Effects) -> np.ndarray | None:
    m = np.eye(3, dtype=np.float32)
    if abs(fx.saturation - 100.0) > 1e-6:
        s = fx.saturation / 100.0
        gray = np.tile(LUMA, (3, 1))
        m = (gray + (np.eye(3, dtype=np.float32) - gray) * s) @ m
    if fx.black_white:
        m = np.tile(LUMA, (3, 1)) @ m
    if fx.sepia:
        m = SEPIA @ m
    if np.allclose(m, np.eye(3)):
        return None
    return m


def apply_color(arr: np.ndarray, fx: Effects) -> np.ndarray:
    """Return a new array with colour effects applied (input may be RGB or RGBA)."""
    if fx.is_color_neutral():
        return arr
    rgb = arr[..., :3]
    if abs(fx.brightness) > 1e-6 or abs(fx.contrast) > 1e-6:
        rgb = _brightness_contrast_lut(fx.brightness, fx.contrast)[rgb]
    m = color_matrix(fx)
    if m is not None:
        h, w = rgb.shape[:2]
        flat = rgb.reshape(-1, 3).astype(np.float32) @ m.T
        rgb = np.clip(flat, 0, 255).astype(np.uint8).reshape(h, w, 3)
    if arr.shape[2] == 4:
        out = np.empty_like(arr)
        out[..., :3] = rgb
        out[..., 3] = arr[..., 3]
        return out
    return np.ascontiguousarray(rgb)


def _box_blur_axis(a: np.ndarray, r: int, axis: int) -> np.ndarray:
    if r <= 0:
        return a
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r + 1, r)
    p = np.pad(a, pad, mode="edge")
    c = np.cumsum(p, axis=axis, dtype=np.float32)
    n = a.shape[axis]
    hi = np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=axis)
    lo = np.take(c, np.arange(0, n), axis=axis)
    return (hi - lo) / (2 * r + 1)


def blur(arr: np.ndarray, radius: float) -> np.ndarray:
    """Approximate Gaussian blur.

    Large radii are handled by area-downsampling first; the caller scales the
    (smaller) result back up when drawing, which also smooths it. The returned
    array may therefore be smaller than the input.
    """
    if radius < 0.5:
        return arr
    h, w = arr.shape[:2]
    k = max(1, int(radius / 3))
    if k > 1 and h >= 2 * k and w >= 2 * k:
        h2, w2 = h // k * k, w // k * k
        c = arr.shape[2]
        arr = arr[:h2, :w2].reshape(h2 // k, k, w2 // k, k, c).mean(axis=(1, 3))
    r = max(1, int(round(radius / k / 2)))
    a = arr.astype(np.float32)
    for _ in range(2):  # two box passes ~ gaussian
        a = _box_blur_axis(a, r, 0)
        a = _box_blur_axis(a, r, 1)
    return np.clip(a, 0, 255).astype(np.uint8)
