"""Project data model.

Pure Python, no Qt. Everything the editor knows about a project lives here and
round-trips through ``to_dict`` / ``from_dict`` (which is also what the JSON
project files and the undo snapshots use).

Time is always in seconds (float). A clip occupies ``[start, end)`` on the
timeline and shows source media from ``in_point`` to ``out_point``, played back
at ``speed``.
"""
from __future__ import annotations

import uuid
from dataclasses import asdict, dataclass, field, fields
from typing import Optional

EPS = 1e-6
PROJECT_VERSION = 1

DEFAULT_IMAGE_DURATION = 5.0
DEFAULT_TEXT_DURATION = 5.0

TEXT_PRESETS = {
    "title": "Centered title",
    "lower_third": "Lower third",
    "top_caption": "Top caption",
    "subtitle": "Subtitle",
}


def new_id() -> str:
    return uuid.uuid4().hex[:10]


def _load_dataclass(cls, data: dict):
    """Build a dataclass from a dict, ignoring unknown keys (forward compat)."""
    names = {f.name for f in fields(cls)}
    return cls(**{k: v for k, v in (data or {}).items() if k in names})


# --------------------------------------------------------------------------- media
@dataclass
class MediaItem:
    path: str
    kind: str  # "video" | "audio" | "image"
    name: str = ""
    id: str = field(default_factory=new_id)
    duration: float = 0.0  # 0 for still images (unbounded)
    width: int = 0
    height: int = 0
    fps: float = 0.0
    has_video: bool = False
    has_audio: bool = False
    sample_rate: int = 0
    channels: int = 0
    # Marks set in the source monitor; used when the item is placed on the timeline.
    mark_in: Optional[float] = None
    mark_out: Optional[float] = None

    @property
    def is_still(self) -> bool:
        return self.kind == "image"

    def marked_range(self) -> tuple[float, float]:
        """Source range to use when this item is added to a sequence."""
        if self.is_still:
            return 0.0, DEFAULT_IMAGE_DURATION
        start = self.mark_in if self.mark_in is not None else 0.0
        end = self.mark_out if self.mark_out is not None else self.duration
        start = max(0.0, min(start, self.duration))
        end = max(start, min(end, self.duration))
        if end - start < EPS:
            start, end = 0.0, self.duration
        return start, end

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "MediaItem":
        return _load_dataclass(cls, data)


# --------------------------------------------------------------------------- clip parts
@dataclass
class Effects:
    brightness: float = 0.0  # -100..100
    contrast: float = 0.0  # -100..100
    saturation: float = 100.0  # 0..200 (%)
    black_white: bool = False
    sepia: bool = False
    blur: float = 0.0  # radius in pixels at 1080p
    opacity: float = 100.0  # 0..100 (%)

    def is_color_neutral(self) -> bool:
        return (abs(self.brightness) < EPS and abs(self.contrast) < EPS
                and abs(self.saturation - 100.0) < EPS
                and not self.black_white and not self.sepia)


@dataclass
class Transform:
    scale: float = 100.0  # % of "fit to frame"
    pos_x: float = 0.0  # offset from center, % of frame width
    pos_y: float = 0.0  # offset from center, % of frame height
    animate: bool = False  # gradual zoom/pan from start values to *_end values
    scale_end: float = 100.0
    pos_x_end: float = 0.0
    pos_y_end: float = 0.0

    def at(self, progress: float) -> tuple[float, float, float]:
        """Return (scale%, pos_x%, pos_y%) at clip progress 0..1."""
        if not self.animate:
            return self.scale, self.pos_x, self.pos_y
        p = max(0.0, min(1.0, progress))
        lerp = lambda a, b: a + (b - a) * p  # noqa: E731
        return (lerp(self.scale, self.scale_end), lerp(self.pos_x, self.pos_x_end),
                lerp(self.pos_y, self.pos_y_end))


@dataclass
class TextProps:
    text: str = "Title"
    preset: str = "title"
    font_family: str = "Segoe UI"
    font_size: float = 72.0  # px at 1080p
    bold: bool = True
    italic: bool = False
    color: str = "#ffffff"
    bg_box: bool = False
    bg_color: str = "#000000"
    bg_opacity: float = 60.0  # %


# --------------------------------------------------------------------------- clip
@dataclass
class Clip:
    kind: str  # "video" | "audio" | "text"
    track_id: str
    start: float
    in_point: float
    out_point: float
    media_id: Optional[str] = None
    name: str = ""
    id: str = field(default_factory=new_id)
    speed: float = 1.0
    volume: float = 100.0  # % (0..200)
    fade_in: float = 0.0  # seconds
    fade_out: float = 0.0
    cross_dissolve: float = 0.0  # seconds; dissolve from the clip that ends where this starts
    link_id: Optional[str] = None
    effects: Effects = field(default_factory=Effects)
    transform: Transform = field(default_factory=Transform)
    text: Optional[TextProps] = None

    # -- timing
    @property
    def duration(self) -> float:
        return max(0.0, (self.out_point - self.in_point) / max(self.speed, EPS))

    @property
    def end(self) -> float:
        return self.start + self.duration

    def contains(self, t: float) -> bool:
        return self.start - EPS <= t < self.end - EPS

    def source_time(self, t: float) -> float:
        """Source media time shown at timeline time ``t``."""
        return self.in_point + (t - self.start) * self.speed

    def progress(self, t: float) -> float:
        d = self.duration
        return 0.0 if d <= EPS else (t - self.start) / d

    def fade_factor(self, t: float) -> float:
        """0..1 multiplier from fade in/out at timeline time ``t``."""
        f = 1.0
        if self.fade_in > EPS:
            f = min(f, (t - self.start) / self.fade_in)
        if self.fade_out > EPS:
            f = min(f, (self.end - t) / self.fade_out)
        return max(0.0, min(1.0, f))

    # -- serialization
    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "Clip":
        data = dict(data)
        effects = _load_dataclass(Effects, data.pop("effects", {}))
        transform = _load_dataclass(Transform, data.pop("transform", {}))
        text = data.pop("text", None)
        clip = _load_dataclass(cls, data)
        clip.effects = effects
        clip.transform = transform
        clip.text = _load_dataclass(TextProps, text) if text is not None else None
        return clip

    def copy(self, new_identity: bool = True) -> "Clip":
        c = Clip.from_dict(self.to_dict())
        if new_identity:
            c.id = new_id()
        return c


# --------------------------------------------------------------------------- tracks
@dataclass
class Track:
    kind: str  # "video" | "audio"
    id: str = field(default_factory=new_id)
    enabled: bool = True  # visible (video) / not muted (audio)
    locked: bool = False
    solo: bool = False

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "Track":
        return _load_dataclass(cls, data)


# --------------------------------------------------------------------------- sequence
@dataclass
class Sequence:
    width: int = 1920
    height: int = 1080
    fps: float = 30.0
    sample_rate: int = 48000
    video_tracks: list[Track] = field(default_factory=list)  # index 0 = V1 (bottom layer)
    audio_tracks: list[Track] = field(default_factory=list)  # index 0 = A1
    clips: dict[str, Clip] = field(default_factory=dict)
    mark_in: Optional[float] = None
    mark_out: Optional[float] = None
    settings_locked: bool = False  # True once size/fps were chosen (auto or by user)

    @classmethod
    def create_default(cls) -> "Sequence":
        seq = cls()
        seq.video_tracks = [Track("video") for _ in range(3)]
        seq.audio_tracks = [Track("audio") for _ in range(2)]
        return seq

    # -- tracks
    def tracks(self, kind: Optional[str] = None) -> list[Track]:
        if kind == "video":
            return list(self.video_tracks)
        if kind == "audio":
            return list(self.audio_tracks)
        return self.video_tracks + self.audio_tracks

    def track(self, track_id: str) -> Optional[Track]:
        for t in self.video_tracks + self.audio_tracks:
            if t.id == track_id:
                return t
        return None

    def track_index(self, track_id: str) -> int:
        tr = self.track(track_id)
        if tr is None:
            return -1
        return (self.video_tracks if tr.kind == "video" else self.audio_tracks).index(tr)

    def track_name(self, track_id: str) -> str:
        tr = self.track(track_id)
        if tr is None:
            return "?"
        return ("V" if tr.kind == "video" else "A") + str(self.track_index(track_id) + 1)

    def add_track(self, kind: str) -> Track:
        """Video tracks stack on top (appended); audio tracks go below (appended)."""
        tr = Track(kind)
        (self.video_tracks if kind == "video" else self.audio_tracks).append(tr)
        return tr

    def is_track_empty(self, track_id: str) -> bool:
        return not any(c.track_id == track_id for c in self.clips.values())

    def remove_track(self, track_id: str) -> bool:
        """Remove an empty track. Returns False if it has clips or is the last of its kind."""
        tr = self.track(track_id)
        if tr is None or not self.is_track_empty(track_id):
            return False
        lst = self.video_tracks if tr.kind == "video" else self.audio_tracks
        if len(lst) <= 1:
            return False
        lst.remove(tr)
        return True

    # -- clips
    def clips_on(self, track_id: str) -> list[Clip]:
        return sorted((c for c in self.clips.values() if c.track_id == track_id),
                      key=lambda c: c.start)

    def clip_at(self, track_id: str, t: float) -> Optional[Clip]:
        for c in self.clips_on(track_id):
            if c.contains(t):
                return c
        return None

    def linked(self, clip: Clip) -> list[Clip]:
        """Other clips sharing this clip's link group."""
        if not clip.link_id:
            return []
        return [c for c in self.clips.values() if c.link_id == clip.link_id and c.id != clip.id]

    def previous_adjacent(self, clip: Clip) -> Optional[Clip]:
        """The clip on the same track that ends exactly where ``clip`` starts."""
        for c in self.clips_on(clip.track_id):
            if c.id != clip.id and abs(c.end - clip.start) < 1e-3:
                return c
        return None

    def next_adjacent(self, clip: Clip) -> Optional[Clip]:
        for c in self.clips_on(clip.track_id):
            if c.id != clip.id and abs(c.start - clip.end) < 1e-3:
                return c
        return None

    @property
    def duration(self) -> float:
        return max((c.end for c in self.clips.values()), default=0.0)

    def frame_duration(self) -> float:
        return 1.0 / (self.fps or 30.0)

    # -- serialization
    def to_dict(self) -> dict:
        return {
            "width": self.width, "height": self.height, "fps": self.fps,
            "sample_rate": self.sample_rate,
            "video_tracks": [t.to_dict() for t in self.video_tracks],
            "audio_tracks": [t.to_dict() for t in self.audio_tracks],
            "clips": [c.to_dict() for c in self.clips.values()],
            "mark_in": self.mark_in, "mark_out": self.mark_out,
            "settings_locked": self.settings_locked,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Sequence":
        seq = cls(width=int(data.get("width", 1920)), height=int(data.get("height", 1080)),
                  fps=float(data.get("fps", 30.0)),
                  sample_rate=int(data.get("sample_rate", 48000)))
        seq.video_tracks = [Track.from_dict(t) for t in data.get("video_tracks", [])]
        seq.audio_tracks = [Track.from_dict(t) for t in data.get("audio_tracks", [])]
        if not seq.video_tracks:
            seq.video_tracks = [Track("video")]
        if not seq.audio_tracks:
            seq.audio_tracks = [Track("audio")]
        for cd in data.get("clips", []):
            c = Clip.from_dict(cd)
            if seq.track(c.track_id) is not None:
                seq.clips[c.id] = c
        seq.mark_in = data.get("mark_in")
        seq.mark_out = data.get("mark_out")
        seq.settings_locked = bool(data.get("settings_locked", False))
        return seq


# --------------------------------------------------------------------------- project
@dataclass
class Project:
    media: dict[str, MediaItem] = field(default_factory=dict)
    sequence: Sequence = field(default_factory=Sequence.create_default)
    path: Optional[str] = None  # where it was last saved; not serialized

    def add_media(self, item: MediaItem) -> MediaItem:
        self.media[item.id] = item
        return item

    def remove_media(self, media_id: str) -> None:
        """Remove a media item and every clip that uses it."""
        self.media.pop(media_id, None)
        for cid in [c.id for c in self.sequence.clips.values() if c.media_id == media_id]:
            del self.sequence.clips[cid]

    def media_for(self, clip: Clip) -> Optional[MediaItem]:
        return self.media.get(clip.media_id) if clip.media_id else None

    def to_dict(self) -> dict:
        return {
            "app": "Strata Video",
            "version": PROJECT_VERSION,
            "media": [m.to_dict() for m in self.media.values()],
            "sequence": self.sequence.to_dict(),
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Project":
        if not isinstance(data, dict) or "sequence" not in data:
            raise ValueError("Not a Strata Video project file")
        proj = cls()
        for md in data.get("media", []):
            m = MediaItem.from_dict(md)
            proj.media[m.id] = m
        proj.sequence = Sequence.from_dict(data["sequence"])
        return proj

    def restore(self, data: dict) -> None:
        """Replace this project's contents in place (used by undo/redo)."""
        other = Project.from_dict(data)
        self.media = other.media
        self.sequence = other.sequence
