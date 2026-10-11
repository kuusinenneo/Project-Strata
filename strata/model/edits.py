"""Editing operations on a Sequence.

These are plain functions over the model so they can be unit tested without a
UI. Lock checks and undo snapshots are the caller's job.
"""
from __future__ import annotations

from typing import Iterable, Optional

from .project import (DEFAULT_TEXT_DURATION, EPS, Clip, MediaItem, Project, Sequence,
                      TextProps, new_id)

MIN_CLIP_DURATION = 1.0 / 120.0


# --------------------------------------------------------------------------- creation
def adopt_media_settings(seq: Sequence, media: MediaItem) -> bool:
    """Match the sequence size/fps to the first video placed in it (once)."""
    if seq.settings_locked or media.kind != "video" or not media.width:
        return False
    seq.width = int(media.width) // 2 * 2
    seq.height = int(media.height) // 2 * 2
    if media.fps and 1 <= media.fps <= 240:
        seq.fps = round(media.fps, 3)
    seq.settings_locked = True
    return True


def clips_from_media(media: MediaItem, start: float, video_track_id: Optional[str],
                     audio_track_id: Optional[str]) -> list[Clip]:
    """Build (linked) clips for a media item. Returns [video?, audio?]."""
    src_in, src_out = media.marked_range()
    link = new_id() if (media.has_audio and media.has_video) else None
    out: list[Clip] = []
    if (media.has_video or media.is_still) and video_track_id:
        out.append(Clip("video", video_track_id, start, src_in, src_out,
                        media_id=media.id, name=media.name, link_id=link))
    if media.has_audio and audio_track_id:
        out.append(Clip("audio", audio_track_id, start, src_in, src_out,
                        media_id=media.id, name=media.name, link_id=link))
    if len(out) == 1:
        out[0].link_id = None
    return out


def make_text_clip(track_id: str, start: float, text: str = "Title",
                   preset: str = "title", duration: float = DEFAULT_TEXT_DURATION) -> Clip:
    props = TextProps(text=text, preset=preset)
    if preset in ("lower_third", "subtitle", "top_caption"):
        props.font_size = 48.0
        props.bg_box = preset in ("lower_third", "subtitle")
    return Clip("text", track_id, start, 0.0, duration, name="Text", text=props)


def is_unbounded(clip: Clip, project: Optional[Project]) -> bool:
    """Text and still-image clips can be extended without running out of media."""
    if clip.kind == "text":
        return True
    if project is None:
        return False
    m = project.media_for(clip)
    return m is not None and m.is_still


def source_limit(clip: Clip, project: Optional[Project]) -> float:
    if is_unbounded(clip, project) or project is None:
        return float("inf")
    m = project.media_for(clip)
    return m.duration if m and m.duration > 0 else float("inf")


# --------------------------------------------------------------------------- trimming
def trim_start(clip: Clip, new_start: float, project: Optional[Project] = None) -> None:
    """Move the left edge of ``clip`` to ``new_start`` (content stays anchored)."""
    new_start = max(0.0, min(new_start, clip.end - MIN_CLIP_DURATION))
    new_in = clip.in_point + (new_start - clip.start) * clip.speed
    if new_in < 0 and not is_unbounded(clip, project):
        new_in = 0.0
        new_start = clip.start - clip.in_point / clip.speed
    if is_unbounded(clip, project) and new_in < 0:
        # still/text: shift the whole source window instead of going negative
        clip.out_point += -new_in
        new_in = 0.0
    clip.start = new_start
    clip.in_point = new_in


def trim_end(clip: Clip, new_end: float, project: Optional[Project] = None) -> None:
    new_end = max(new_end, clip.start + MIN_CLIP_DURATION)
    new_out = clip.in_point + (new_end - clip.start) * clip.speed
    clip.out_point = min(new_out, source_limit(clip, project))


def set_speed(clip: Clip, speed: float) -> None:
    """Change playback speed; the clip keeps its source range so its length changes."""
    clip.speed = max(0.1, min(16.0, float(speed)))


# --------------------------------------------------------------------------- splitting
def _split_transform(left: Clip, right: Clip, t: float) -> None:
    tr = left.transform
    if not tr.animate:
        return
    s, x, y = tr.at(left.progress(t))
    left.transform.scale_end, left.transform.pos_x_end, left.transform.pos_y_end = s, x, y
    right.transform.scale, right.transform.pos_x, right.transform.pos_y = s, x, y


def split_clip(seq: Sequence, clip: Clip, t: float) -> Optional[Clip]:
    """Cut ``clip`` at timeline time ``t``. Returns the new right-hand clip."""
    if not (clip.start + MIN_CLIP_DURATION < t < clip.end - MIN_CLIP_DURATION):
        return None
    right = clip.copy()
    src_t = clip.source_time(t)
    _split_transform(clip, right, t)
    clip.out_point = src_t
    clip.fade_out = 0.0
    right.start = t
    right.in_point = src_t
    right.fade_in = 0.0
    right.cross_dissolve = 0.0
    seq.clips[right.id] = right
    return right


def split_at(seq: Sequence, t: float, clips: Iterable[Clip]) -> list[Clip]:
    """Split several clips at ``t``; right halves of linked clips stay linked."""
    new_links: dict[str, str] = {}
    created = []
    for c in list(clips):
        right = split_clip(seq, c, t)
        if right is None:
            continue
        if c.link_id:
            right.link_id = new_links.setdefault(c.link_id, new_id())
        created.append(right)
    # A half whose partner did not get split must not stay linked to the wrong piece.
    for right in created:
        if right.link_id and not any(o.link_id == right.link_id and o.id != right.id
                                     for o in created):
            right.link_id = None
    return created


# --------------------------------------------------------------------------- overwrite
def clear_range(seq: Sequence, track_id: str, start: float, end: float,
                exclude: Iterable[str] = ()) -> None:
    """Overwrite edit: remove/trim/split whatever occupies [start, end) on a track."""
    excl = set(exclude)
    if end - start < EPS:
        return
    for c in seq.clips_on(track_id):
        if c.id in excl or c.end <= start + EPS or c.start >= end - EPS:
            continue
        if c.start >= start - EPS and c.end <= end + EPS:
            del seq.clips[c.id]  # fully covered
        elif c.start < start and c.end > end:
            right = split_clip(seq, c, end)
            c.out_point = c.source_time(start)
            c.fade_out = 0.0
            if right is not None:
                right.link_id = None
        elif c.start < start:
            c.out_point = c.source_time(start)
            c.fade_out = 0.0
        else:
            new_in = c.source_time(end)
            c.in_point = new_in
            c.start = end
            c.fade_in = 0.0
            c.cross_dissolve = 0.0


def place_clips(seq: Sequence, clips: Iterable[Clip]) -> None:
    """Add clips to the sequence, overwriting anything underneath them."""
    clips = list(clips)
    ids = {c.id for c in clips}
    for c in clips:
        c.start = max(0.0, c.start)
        clear_range(seq, c.track_id, c.start, c.end, exclude=ids)
        seq.clips[c.id] = c


def move_clips(seq: Sequence, moves: dict[str, tuple[float, str]]) -> None:
    """Apply {clip_id: (new_start, new_track_id)} then resolve overlaps (overwrite)."""
    moved = []
    for cid, (start, track_id) in moves.items():
        c = seq.clips.get(cid)
        if c is None:
            continue
        c.start = max(0.0, start)
        c.track_id = track_id
        moved.append(c)
    ids = set(moves)
    for c in moved:
        if c.id in seq.clips:
            clear_range(seq, c.track_id, c.start, c.end, exclude=ids)


# --------------------------------------------------------------------------- ripple
def _shift_after(seq: Sequence, track_id: str, t: float, delta: float,
                 exclude: Iterable[str] = ()) -> None:
    excl = set(exclude)
    for c in seq.clips_on(track_id):
        if c.id not in excl and c.start >= t - EPS:
            c.start = max(0.0, c.start + delta)


def ripple_delete(seq: Sequence, clip_ids: Iterable[str]) -> None:
    """Delete clips and close the gaps they leave on their tracks."""
    removed = [seq.clips[cid] for cid in clip_ids if cid in seq.clips]
    for c in removed:
        del seq.clips[c.id]
    # Process right-to-left so earlier shifts don't disturb later ranges.
    for c in sorted(removed, key=lambda c: c.start, reverse=True):
        _ripple_close(seq, c.track_id, c.start, c.end)


def _ripple_close(seq: Sequence, track_id: str, start: float, end: float) -> None:
    # Clips on one track never overlap, so the freed span is exactly [start, end).
    gap = end - start
    if gap > EPS:
        _shift_after(seq, track_id, end, -gap)


def gap_at(seq: Sequence, track_id: str, t: float) -> Optional[tuple[float, float]]:
    """Empty range on a track containing ``t`` that has a clip after it."""
    clips = seq.clips_on(track_id)
    if seq.clip_at(track_id, t) is not None:
        return None
    before = max((c.end for c in clips if c.end <= t + EPS), default=0.0)
    after = min((c.start for c in clips if c.start >= t - EPS), default=None)
    if after is None or after - before < EPS:
        return None
    return before, after


def remove_gap(seq: Sequence, track_id: str, t: float) -> bool:
    g = gap_at(seq, track_id, t)
    if g is None:
        return False
    _shift_after(seq, track_id, g[1], -(g[1] - g[0]))
    return True


def ripple_insert(seq: Sequence, clips: list[Clip], t: float) -> None:
    """Insert edit: push everything at/after ``t`` on the target tracks to the right."""
    if not clips:
        return
    dur = max(c.duration for c in clips)
    for track_id in {c.track_id for c in clips}:
        for c in list(seq.clips_on(track_id)):
            if c.contains(t) and c.start < t - EPS:
                right = split_clip(seq, c, t)
                if right is not None:
                    right.link_id = None
        _shift_after(seq, track_id, t, dur)
    for c in clips:
        c.start = t
        seq.clips[c.id] = c


def track_end(seq: Sequence, track_id: str) -> float:
    return max((c.end for c in seq.clips_on(track_id)), default=0.0)


# --------------------------------------------------------------------------- join
def join_clips(seq: Sequence, clip_ids: Iterable[str]) -> int:
    """Close the gaps between the given clips on each track so they play back to back.

    Linked partners follow their clip. Returns the number of clips moved.
    """
    selected = [seq.clips[cid] for cid in clip_ids if cid in seq.clips]
    by_track: dict[str, list[Clip]] = {}
    for c in selected:
        by_track.setdefault(c.track_id, []).append(c)
    moved_ids: set[str] = set()
    moves = 0
    for track_id, clips in by_track.items():
        clips.sort(key=lambda c: c.start)
        cursor = clips[0].end
        for c in clips[1:]:
            if c.id in moved_ids:
                cursor = c.end
                continue
            delta = cursor - c.start
            if abs(delta) > EPS:
                c.start += delta
                moves += 1
                for partner in seq.linked(c):
                    if partner.id not in {s.id for s in selected}:
                        partner.start += delta
                        moved_ids.add(partner.id)
            moved_ids.add(c.id)
            cursor = c.end
    ids = {c.id for c in selected} | moved_ids
    for cid in ids:
        c = seq.clips.get(cid)
        if c is not None:
            clear_range(seq, c.track_id, c.start, c.end, exclude=ids)
    return moves


# --------------------------------------------------------------------------- snapping
def snap_points(seq: Sequence, exclude: Iterable[str] = (),
                playhead: Optional[float] = None) -> list[float]:
    excl = set(exclude)
    pts = {0.0}
    for c in seq.clips.values():
        if c.id not in excl:
            pts.add(c.start)
            pts.add(c.end)
    if playhead is not None:
        pts.add(playhead)
    return sorted(pts)


def snap(value: float, points: Iterable[float], threshold: float) -> tuple[float, Optional[float]]:
    """Snap ``value`` to the nearest point within ``threshold``. Returns (value, snapped_to)."""
    best, best_d = None, threshold
    for p in points:
        d = abs(p - value)
        if d <= best_d:
            best, best_d = p, d
    return (best, best) if best is not None else (value, None)
