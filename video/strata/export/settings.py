"""Export options and how they map onto FFmpeg arguments."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

from ..model.project import Project

FRAME_RATES = ["Match source", "24", "25", "30", "50", "60"]
RESOLUTIONS = {"Match sequence": None, "720p": 720, "1080p": 1080, "1440p": 1440, "4K (2160p)": 2160}
QUALITY_CRF = {"Low": 28, "Medium": 23, "High": 19, "Very high": 16}
CUSTOM_BITRATE = "Custom bitrate"
SPEED_PRESETS = {"Fast": "veryfast", "Balanced": "medium", "Best": "slow"}
AUDIO_BITRATES = [128, 192, 320]

# Rough bits per pixel per frame that x264 produces at each CRF for typical footage;
# used only for the size estimate shown before export.
_BPP = {28: 0.035, 23: 0.07, 19: 0.12, 16: 0.18}


@dataclass
class ExportSettings:
    output_path: str = ""
    frame_rate: str = "Match source"
    resolution: str = "Match sequence"
    quality: str = "High"  # key of QUALITY_CRF or CUSTOM_BITRATE
    custom_bitrate_kbps: int = 8000
    speed: str = "Balanced"
    audio_bitrate_kbps: int = 192
    range_start: float = 0.0
    range_end: Optional[float] = None  # None = end of sequence


def source_fps(project: Project) -> float:
    """Frame rate of the first video placed in the sequence, else the sequence rate."""
    seq = project.sequence
    for c in sorted(seq.clips.values(), key=lambda c: c.start):
        m = project.media_for(c)
        if m is not None and m.kind == "video" and m.fps:
            return float(m.fps)
    return float(seq.fps or 30.0)


def resolve_fps(project: Project, s: ExportSettings) -> float:
    if s.frame_rate == "Match source":
        fps = source_fps(project)
        return fps if 1 <= fps <= 240 else 30.0
    return float(s.frame_rate)


def resolve_size(project: Project, s: ExportSettings) -> tuple[int, int]:
    seq = project.sequence
    h = RESOLUTIONS.get(s.resolution)
    if h is None:
        w, h = seq.width, seq.height
    else:
        aspect = seq.width / max(1, seq.height)
        w = h * aspect
    return int(round(w / 2)) * 2, int(round(h / 2)) * 2


def export_range(project: Project, s: ExportSettings) -> tuple[float, float]:
    end = project.sequence.duration if s.range_end is None else s.range_end
    start = max(0.0, min(s.range_start, end))
    return start, max(start, end)


def video_args(s: ExportSettings) -> list[str]:
    args = ["-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-preset", SPEED_PRESETS.get(s.speed, "medium")]
    if s.quality == CUSTOM_BITRATE:
        kb = max(100, int(s.custom_bitrate_kbps))
        args += ["-b:v", f"{kb}k", "-maxrate", f"{int(kb * 1.5)}k", "-bufsize", f"{kb * 2}k"]
    else:
        args += ["-crf", str(QUALITY_CRF.get(s.quality, 19))]
    return args


def estimate_size_bytes(project: Project, s: ExportSettings) -> int:
    start, end = export_range(project, s)
    dur = end - start
    w, h = resolve_size(project, s)
    fps = resolve_fps(project, s)
    if s.quality == CUSTOM_BITRATE:
        video_bits = s.custom_bitrate_kbps * 1000 * dur
    else:
        bpp = _BPP.get(QUALITY_CRF.get(s.quality, 19), 0.1)
        # bits/pixel shrinks a little as resolution grows (more redundancy)
        video_bits = bpp * w * h * fps * dur * (1080 * 1920 / max(1, w * h)) ** 0.15
    audio_bits = s.audio_bitrate_kbps * 1000 * dur
    return int((video_bits + audio_bits) / 8 * 1.01)


def format_size(n: int) -> str:
    size = float(n)
    for unit in ("B", "KB", "MB", "GB"):
        if size < 1024 or unit == "GB":
            return f"{size:.0f} {unit}" if unit in ("B", "KB") else f"{size:.1f} {unit}"
        size /= 1024
    return f"{n} B"
