"""EditorController: the single place where the UI changes the project.

Panels call methods here instead of touching the model directly. Every edit
takes an undo snapshot first, respects track locks, and then emits signals the
panels listen to.
"""
from __future__ import annotations

import os
from typing import Callable, Iterable, Optional

from PySide6.QtCore import QObject, Signal

from ..media.audio import AudioStore
from ..media.probe import ProbeError, probe
from ..model import edits
from ..model.io import load_project, save_project
from ..model.project import EPS, Clip, MediaItem, Project, Track
from ..model.undo import UndoStack

TOOLS = ("select", "razor", "ripple", "text")


class EditorController(QObject):
    project_changed = Signal()  # clips/tracks/properties changed
    project_replaced = Signal()  # new/opened project
    media_changed = Signal()  # project bin contents changed
    selection_changed = Signal()
    playhead_changed = Signal(float)
    tool_changed = Signal(str)
    snapping_changed = Signal(bool)
    linked_selection_changed = Signal(bool)
    audio_loaded = Signal(str)  # media id (emitted from worker threads; queued)
    history_changed = Signal()
    message = Signal(str)

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self.project = Project()
        self.undo = UndoStack(self.project)
        self.selection: set[str] = set()
        self.playhead = 0.0
        self.tool = "select"
        self.snapping = True
        # When on, clicking a clip also selects its linked audio/video partner.
        self.linked_selection = True
        self.audio_store = AudioStore()
        self.audio_store.listeners.append(self.audio_loaded.emit)

    # ------------------------------------------------------------------ helpers
    @property
    def seq(self):
        return self.project.sequence

    def _push(self, label: str, merge_key: Optional[str] = None) -> None:
        self.undo.push(label, merge_key)

    def _done(self, structure: bool = True) -> None:
        edits.prune_links(self.seq)
        self._prune_selection()
        if structure:
            self.project_changed.emit()
        self.history_changed.emit()

    def _prune_selection(self) -> None:
        before = set(self.selection)
        self.selection &= set(self.seq.clips)
        if before != self.selection:
            self.selection_changed.emit()

    def is_locked(self, track_id: str) -> bool:
        tr = self.seq.track(track_id)
        return tr is None or tr.locked

    def clip(self, clip_id: str) -> Optional[Clip]:
        return self.seq.clips.get(clip_id)

    def with_linked(self, clips: Iterable[Clip], force: bool = False) -> list[Clip]:
        """Add linked partners (only while linked selection is on, unless ``force``)."""
        out: dict[str, Clip] = {}
        for c in clips:
            out[c.id] = c
            if self.linked_selection or force:
                for o in self.seq.linked(c):
                    out[o.id] = o
        return list(out.values())

    def editable(self, clips: Iterable[Clip]) -> list[Clip]:
        return [c for c in clips if not self.is_locked(c.track_id)]

    # ------------------------------------------------------------------ project files
    def new_project(self) -> None:
        self._replace(Project())

    def open_project(self, path: str) -> list[str]:
        proj = load_project(path)
        self._replace(proj)
        for m in proj.media.values():
            if m.has_audio and os.path.exists(m.path):
                self.audio_store.request(m.id, m.path)
        from ..model.io import missing_media
        return missing_media(proj)

    def recover_project(self, autosave_path: str, original_path) -> list[str]:
        """Load an autosaved copy after a crash; it stays unsaved until the user saves it."""
        missing = self.open_project(autosave_path)
        self.project.path = original_path
        self.undo.clean_state = -1  # recovered work counts as unsaved
        self.project_replaced.emit()
        self.history_changed.emit()
        return missing

    def save_project(self, path: str) -> None:
        save_project(self.project, path)
        self.undo.mark_clean()
        self.history_changed.emit()

    def _replace(self, proj: Project) -> None:
        self.project = proj
        self.undo = UndoStack(proj)
        self.selection.clear()
        self.playhead = 0.0
        self.project_replaced.emit()
        self.media_changed.emit()
        self.project_changed.emit()
        self.selection_changed.emit()
        self.playhead_changed.emit(0.0)
        self.history_changed.emit()

    # ------------------------------------------------------------------ undo
    def do_undo(self) -> None:
        if self.undo.undo():
            self.message.emit("Undo")
            self.media_changed.emit()
            self._done()

    def do_redo(self) -> None:
        if self.undo.redo():
            self.message.emit("Redo")
            self.media_changed.emit()
            self._done()

    # ------------------------------------------------------------------ playhead/tool/selection
    def set_playhead(self, t: float) -> None:
        t = max(0.0, t)
        if abs(t - self.playhead) > 1e-9:
            self.playhead = t
            self.playhead_changed.emit(t)

    def set_tool(self, tool: str) -> None:
        if tool in TOOLS and tool != self.tool:
            self.tool = tool
            self.tool_changed.emit(tool)

    def set_snapping(self, on: bool) -> None:
        self.snapping = bool(on)
        self.snapping_changed.emit(self.snapping)

    def set_linked_selection(self, on: bool) -> None:
        self.linked_selection = bool(on)
        self.linked_selection_changed.emit(self.linked_selection)
        self.message.emit("Linked selection on: audio and video are selected together" if on else
                          "Linked selection off: audio and video can be selected separately")

    def select(self, clip_ids: Iterable[str], add: bool = False, toggle: bool = False,
               single: bool = False) -> None:
        """Select clips. Linked partners are included unless ``single`` (Alt+click)
        or linked selection is turned off."""
        ids = set()
        for cid in clip_ids:
            c = self.clip(cid)
            if c is not None:
                ids.add(c.id)
                if self.linked_selection and not single:
                    ids.update(o.id for o in self.seq.linked(c))
        if toggle:
            new = self.selection ^ ids
        elif add:
            new = self.selection | ids
        else:
            new = ids
        if new != self.selection:
            self.selection = new
            self.selection_changed.emit()

    def clear_selection(self) -> None:
        self.select([])

    def select_all(self) -> None:
        self.select(list(self.seq.clips))

    def selected_clips(self) -> list[Clip]:
        return [self.seq.clips[c] for c in self.selection if c in self.seq.clips]

    def primary_selected(self) -> Optional[Clip]:
        """The clip shown in Effect Controls: prefer video/text over linked audio."""
        clips = self.selected_clips()
        if not clips:
            return None
        clips.sort(key=lambda c: (c.kind == "audio", c.start))
        return clips[0]

    # ------------------------------------------------------------------ media bin
    def import_files(self, paths: Iterable[str]) -> tuple[list[MediaItem], list[str]]:
        added, errors = [], []
        existing = {os.path.normcase(os.path.abspath(m.path)): m for m in self.project.media.values()}
        items = []
        for path in paths:
            key = os.path.normcase(os.path.abspath(path))
            if key in existing:
                added.append(existing[key])
                continue
            try:
                items.append(probe(path))
            except ProbeError as exc:
                errors.append(str(exc))
            except Exception as exc:  # pragma: no cover - unexpected decoder errors
                errors.append(f"{os.path.basename(path)}: {exc}")
        if items:
            self._push("Import media")
            for m in items:
                self.project.add_media(m)
                if m.has_audio:
                    self.audio_store.request(m.id, m.path)
            added.extend(items)
            self.media_changed.emit()
            self.history_changed.emit()
        return added, errors

    def remove_media(self, media_ids: Iterable[str]) -> None:
        ids = [m for m in media_ids if m in self.project.media]
        if not ids:
            return
        self._push("Remove media")
        for mid in ids:
            self.project.remove_media(mid)
        self.media_changed.emit()
        self._done()

    def set_media_marks(self, media_id: str, mark_in: Optional[float], mark_out: Optional[float]) -> None:
        m = self.project.media.get(media_id)
        if m is None:
            return
        m.mark_in, m.mark_out = mark_in, mark_out
        self.media_changed.emit()

    # ------------------------------------------------------------------ placing media
    def _target_tracks(self, track_id: Optional[str], media: MediaItem) -> tuple[Optional[str], Optional[str]]:
        """Pick the (video, audio) tracks for a media drop on ``track_id``."""
        seq = self.seq
        tr = seq.track(track_id) if track_id else None
        idx = seq.track_index(tr.id) if tr else 0
        if tr is not None and tr.kind == "audio":
            a = tr
            v = seq.video_tracks[min(idx, len(seq.video_tracks) - 1)]
        else:
            v = tr or seq.video_tracks[0]
            a = seq.audio_tracks[min(idx, len(seq.audio_tracks) - 1)]
        v_id = v.id if (media.has_video or media.is_still) else None
        a_id = a.id if media.has_audio else None
        if v_id and self.is_locked(v_id):
            v_id = None
        if a_id and self.is_locked(a_id):
            a_id = None
        return v_id, a_id

    def add_media_to_timeline(self, media_ids: Iterable[str], track_id: Optional[str], t: float,
                              insert: bool = False) -> list[Clip]:
        """Place one or more media items end to end starting at ``t``."""
        medias = [self.project.media[m] for m in media_ids if m in self.project.media]
        if not medias:
            return []
        self._push("Insert clips" if insert else "Add clips")
        placed: list[Clip] = []
        cursor = max(0.0, t)
        for m in medias:
            if not self.seq.clips:
                edits.adopt_media_settings(self.seq, m)
            v_id, a_id = self._target_tracks(track_id, m)
            clips = edits.clips_from_media(m, cursor, v_id, a_id)
            if not clips:
                continue
            if insert:
                edits.ripple_insert(self.seq, clips, cursor)
            else:
                edits.place_clips(self.seq, clips)
            placed.extend(clips)
            cursor = max(c.end for c in clips)
        if placed:
            self.select([c.id for c in placed])
            self.message.emit(f"Added {len(medias)} clip(s)")
        self._done()
        return placed

    def append_to_sequence(self, media_ids: Iterable[str]) -> list[Clip]:
        """Add media one after another at the end of V1/A1 (joins them into one sequence)."""
        v1, a1 = self.seq.video_tracks[0].id, self.seq.audio_tracks[0].id
        t = max(edits.track_end(self.seq, v1), edits.track_end(self.seq, a1))
        return self.add_media_to_timeline(media_ids, v1, t)

    def add_text(self, track_id: Optional[str], t: float, preset: str = "title",
                 text: str = "Your title") -> Optional[Clip]:
        seq = self.seq
        tr = seq.track(track_id) if track_id else None
        if tr is None or tr.kind != "video":
            tr = None
        dur = 5.0
        if tr is None or self.is_locked(tr.id) or self._occupied(tr.id, t, t + dur):
            # find a free unlocked video track (topmost first), or make one
            tr = next((v for v in reversed(seq.video_tracks)
                       if not v.locked and not self._occupied(v.id, t, t + dur)), None)
        self._push("Add text")
        if tr is None:
            tr = seq.add_track("video")
        clip = edits.make_text_clip(tr.id, max(0.0, t), text, preset, dur)
        edits.place_clips(seq, [clip])
        self.select([clip.id])
        self._done()
        return clip

    def _occupied(self, track_id: str, start: float, end: float) -> bool:
        return any(c.start < end - EPS and c.end > start + EPS for c in self.seq.clips_on(track_id))

    # ------------------------------------------------------------------ clip edits
    def move_clips(self, moves: dict[str, tuple[float, str]]) -> None:
        moves = {cid: mv for cid, mv in moves.items()
                 if cid in self.seq.clips and not self.is_locked(self.seq.clips[cid].track_id)
                 and not self.is_locked(mv[1])}
        if not moves:
            return
        self._push("Move clips")
        edits.move_clips(self.seq, moves)
        self._done()

    def trim_clip(self, clip_id: str, edge: str, new_time: float, solo: bool = False) -> None:
        """Trim one edge. Linked partners sharing that edge follow unless ``solo``."""
        clip = self.clip(clip_id)
        if clip is None or self.is_locked(clip.track_id):
            return
        self._push("Trim clip")
        old = clip.start if edge == "left" else clip.end
        delta = new_time - old
        partners = self.editable(self.seq.linked(clip)) if (self.linked_selection and not solo) else []
        for c in [clip] + partners:
            # Linked partners follow if they share the edge being trimmed.
            if c is not clip and abs((c.start if edge == "left" else c.end) - old) > 1e-3:
                continue
            if edge == "left":
                edits.trim_start(c, c.start + delta, self.project)
            else:
                edits.trim_end(c, c.end + delta, self.project)
            edits.clear_range(self.seq, c.track_id, c.start, c.end, exclude={c.id})
        self._done()

    def razor(self, clip_id: str, t: float) -> None:
        clip = self.clip(clip_id)
        if clip is None or self.is_locked(clip.track_id):
            return
        targets = self.editable(self.with_linked([clip]))
        if not any(c.start + 1e-3 < t < c.end - 1e-3 for c in targets):
            return
        self._push("Razor cut")
        edits.split_at(self.seq, t, targets)
        self._done()

    def cut_at(self, t: float, all_tracks: bool = False) -> int:
        """Cut at ``t``: selected clips if any (unless ``all_tracks``), else every clip under t."""
        pool = self.selected_clips() if (self.selection and not all_tracks) else list(self.seq.clips.values())
        targets = [c for c in self.editable(self.with_linked(pool))
                   if c.start + 1e-3 < t < c.end - 1e-3]
        if not targets:
            return 0
        self._push("Cut at playhead")
        created = edits.split_at(self.seq, t, targets)
        self._done()
        return len(created)

    def delete_clips(self, clip_ids: Iterable[str], ripple: bool = False) -> None:
        """Delete exactly these clips (callers decide whether linked partners are included)."""
        clips = self.editable(c for c in map(self.clip, clip_ids) if c)
        if not clips:
            return
        self._push("Ripple delete" if ripple else "Delete clips")
        ids = [c.id for c in clips]
        if ripple:
            edits.ripple_delete(self.seq, ids)
        else:
            for cid in ids:
                self.seq.clips.pop(cid, None)
        self._done()

    def delete_selection(self, ripple: bool = False) -> None:
        self.delete_clips(list(self.selection), ripple)

    def remove_gap(self, track_id: str, t: float) -> bool:
        if self.is_locked(track_id) or edits.gap_at(self.seq, track_id, t) is None:
            return False
        self._push("Close gap")
        edits.remove_gap(self.seq, track_id, t)
        self._done()
        return True

    def join_selected(self) -> int:
        clips = self.editable(self.selected_clips())
        if len(clips) < 2:
            self.message.emit("Select two or more clips on a track to join them")
            return 0
        self._push("Join clips")
        n = edits.join_clips(self.seq, [c.id for c in clips])
        self._done()
        self.message.emit(f"Joined clips ({n} moved)")
        return n

    def link_selected(self, link: bool) -> None:
        clips = self.editable(self.selected_clips())
        if not clips:
            return
        if link and len(clips) < 2:
            self.message.emit("Select a video clip and an audio clip to link them")
            return
        self._push("Link clips" if link else "Unlink clips")
        from ..model.project import new_id
        lid = new_id() if link else None
        for c in clips:
            c.link_id = lid
        self._done()
        self.message.emit("Linked: audio and video now move together" if link else
                          "Unlinked: audio and video can now be moved, trimmed and deleted separately")

    def toggle_link_selected(self) -> None:
        """Ctrl+L: unlink if anything selected is linked, otherwise link the selection."""
        clips = self.selected_clips()
        self.link_selected(not any(c.link_id for c in clips))

    def update_clip(self, clip_id: str, fn: Callable[[Clip], None], label: str = "Change clip",
                    merge_key: Optional[str] = None, linked: bool = False) -> None:
        clip = self.clip(clip_id)
        if clip is None:
            return
        if self.is_locked(clip.track_id):
            self.message.emit("Track is locked")
            return
        self._push(label, merge_key)
        targets = [clip] + (self.editable(self.seq.linked(clip)) if linked else [])
        for c in targets:
            fn(c)
        self._done()

    def set_speed(self, clip_id: str, speed: float) -> None:
        """Change speed of a clip (and its linked clips), rippling later clips if it grows."""
        clip = self.clip(clip_id)
        if clip is None or self.is_locked(clip.track_id):
            return
        self._push("Change speed", merge_key=f"speed:{clip_id}")
        for c in [clip] + self.editable(self.seq.linked(clip)):
            old_end = c.end
            edits.set_speed(c, speed)
            nxt = [o for o in self.seq.clips_on(c.track_id) if o.id != c.id and o.start >= old_end - 1e-3]
            if nxt and c.end > nxt[0].start:
                shift = c.end - nxt[0].start
                for o in nxt:
                    o.start += shift
        self._done()

    def apply_effect(self, clip_id: str, effect: str, at_time: Optional[float] = None) -> bool:
        """Apply an effect/transition dragged from the Effects panel."""
        clip = self.clip(clip_id)
        if clip is None or self.is_locked(clip.track_id):
            return False
        visual = clip.kind in ("video", "text")
        if effect == "cross_dissolve":
            target = clip
            if at_time is not None and at_time > clip.start + clip.duration / 2:
                target = self.seq.next_adjacent(clip) or clip
                if target is clip:
                    self.message.emit("Cross dissolve needs a clip right after this one; applied at start")
            self._push("Add cross dissolve")
            for c in [target] + self.editable(self.seq.linked(target)):
                c.cross_dissolve = min(1.0, c.duration)
            self.select([target.id])
            self._done()
            return True
        if effect in ("fade_in", "fade_out"):
            self._push("Add fade")
            for c in [clip] + self.editable(self.seq.linked(clip)):
                setattr(c, effect, min(1.0, c.duration / 2))
            self.select([clip.id])
            self._done()
            return True
        if effect in ("speed_2x", "speed_half"):
            self.undo.break_merge()
            self.set_speed(clip.id, clip.speed * (2.0 if effect == "speed_2x" else 0.5))
            self.select([clip.id])
            return True
        if not visual:
            self.message.emit("That effect only applies to video, image or text clips")
            return False
        self._push("Apply effect")
        fx, tr = clip.effects, clip.transform
        if effect == "brightness_contrast":
            fx.brightness, fx.contrast = 15.0, 15.0
        elif effect == "saturation":
            fx.saturation = 140.0
        elif effect == "black_white":
            fx.black_white = True
        elif effect == "sepia":
            fx.sepia = True
        elif effect == "blur":
            fx.blur = 8.0
        elif effect == "opacity":
            fx.opacity = 60.0
        elif effect == "zoom_in":
            tr.animate, tr.scale_end = True, tr.scale * 1.3
            tr.pos_x_end, tr.pos_y_end = tr.pos_x, tr.pos_y
        elif effect == "zoom_out":
            tr.animate, tr.scale_end = True, tr.scale
            tr.scale = tr.scale * 1.3
            tr.pos_x_end, tr.pos_y_end = tr.pos_x, tr.pos_y
        else:
            return False
        self.select([clip.id])
        self._done()
        return True

    # ------------------------------------------------------------------ tracks
    def add_track(self, kind: str) -> Track:
        self._push("Add track")
        tr = self.seq.add_track(kind)
        self._done()
        return tr

    def remove_track(self, track_id: str) -> bool:
        if not self.seq.is_track_empty(track_id):
            self.message.emit("Only empty tracks can be removed")
            return False
        tr = self.seq.track(track_id)
        if tr is None:
            return False
        lst = self.seq.video_tracks if tr.kind == "video" else self.seq.audio_tracks
        if len(lst) <= 1:
            self.message.emit("A sequence needs at least one track of each kind")
            return False
        self._push("Remove track")
        self.seq.remove_track(track_id)
        self._done()
        return True

    def set_track_flag(self, track_id: str, flag: str, value: bool) -> None:
        tr = self.seq.track(track_id)
        if tr is None or flag not in ("enabled", "locked", "solo"):
            return
        self._push("Track setting")
        setattr(tr, flag, bool(value))
        self._done()

    # ------------------------------------------------------------------ sequence
    def set_sequence_settings(self, width: int, height: int, fps: float) -> None:
        self._push("Sequence settings")
        s = self.seq
        s.width, s.height, s.fps = int(width) // 2 * 2, int(height) // 2 * 2, float(fps)
        s.settings_locked = True
        self._done()

    def set_sequence_marks(self, mark_in: Optional[float], mark_out: Optional[float]) -> None:
        self.seq.mark_in, self.seq.mark_out = mark_in, mark_out
        self.project_changed.emit()

    def snap_points(self, exclude: Iterable[str] = ()) -> list[float]:
        return edits.snap_points(self.seq, exclude, self.playhead)
