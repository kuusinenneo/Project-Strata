"""Inspecting media files (duration, size, streams) with PyAV."""
from __future__ import annotations

import os

import av

from ..model.project import MediaItem

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"}
AUDIO_EXTS = {".wav", ".mp3", ".aac", ".m4a", ".flac", ".ogg", ".opus", ".wma", ".aiff", ".aif"}
VIDEO_EXTS = {".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv", ".flv", ".mpg",
              ".mpeg", ".ts", ".mts", ".m2ts", ".3gp", ".gif"}

ALL_EXTS = IMAGE_EXTS | AUDIO_EXTS | VIDEO_EXTS


def file_filter() -> str:
    pat = lambda exts: " ".join("*" + e for e in sorted(exts))  # noqa: E731
    return (f"Media files ({pat(ALL_EXTS)});;Video ({pat(VIDEO_EXTS)});;"
            f"Audio ({pat(AUDIO_EXTS)});;Images ({pat(IMAGE_EXTS)});;All files (*)")


class ProbeError(Exception):
    pass


def _is_cover_art(stream) -> bool:
    try:
        return bool(stream.disposition & av.stream.Disposition.attached_pic)
    except Exception:
        return False


def probe(path: str) -> MediaItem:
    """Read a file's properties and return a new MediaItem for it."""
    if not os.path.isfile(path):
        raise ProbeError(f"File not found: {path}")
    ext = os.path.splitext(path)[1].lower()
    name = os.path.basename(path)
    try:
        container = av.open(path)
    except Exception as exc:
        raise ProbeError(f"Cannot open {name}: {exc}") from exc
    try:
        vstreams = [s for s in container.streams.video if not _is_cover_art(s)]
        astreams = list(container.streams.audio)
        if ext in IMAGE_EXTS:
            if not vstreams:
                raise ProbeError(f"{name}: no image data")
            v = vstreams[0]
            return MediaItem(path=path, kind="image", name=name, width=v.codec_context.width,
                             height=v.codec_context.height, has_video=True)

        if not vstreams and not astreams:
            raise ProbeError(f"{name}: no audio or video streams")

        duration = 0.0
        if container.duration:
            duration = container.duration / av.time_base
        for s in vstreams[:1] + astreams[:1]:
            if s.duration and s.time_base:
                duration = max(duration, float(s.duration * s.time_base))
        if duration <= 0:
            duration = _measure_duration(container)

        item = MediaItem(path=path, kind="video" if vstreams else "audio", name=name,
                         duration=duration)
        if vstreams:
            v = vstreams[0]
            item.has_video = True
            item.width = v.codec_context.width
            item.height = v.codec_context.height
            rate = v.average_rate or v.guessed_rate or v.base_rate
            item.fps = float(rate) if rate else 30.0
        if astreams:
            a = astreams[0]
            item.has_audio = True
            item.sample_rate = a.codec_context.sample_rate or a.rate or 48000
            item.channels = a.codec_context.channels or 2
        return item
    finally:
        container.close()


def _measure_duration(container) -> float:
    """Fallback for files without duration metadata: demux to the end."""
    last = 0.0
    for packet in container.demux():
        if packet.pts is not None and packet.time_base:
            last = max(last, float((packet.pts + (packet.duration or 0)) * packet.time_base))
    return last
