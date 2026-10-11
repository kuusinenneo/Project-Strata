"""Drag-and-drop MIME types shared by panels."""
from __future__ import annotations

from PySide6.QtCore import QByteArray

MEDIA_MIME = "application/x-strata-media"
EFFECT_MIME = "application/x-strata-effect"


def encode_ids(ids) -> QByteArray:
    return QByteArray(",".join(ids).encode("utf-8"))


def decode_ids(data: QByteArray) -> list[str]:
    raw = bytes(data).decode("utf-8")
    return [x for x in raw.split(",") if x]
