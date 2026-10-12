import json
import os

import pytest

from strata.model import Clip, MediaItem, Project, Sequence, UndoStack
from strata.model import edits
from strata.model.io import load_project, save_project


def make_media(duration=10.0, audio=True, kind="video"):
    return MediaItem(path="C:/x/clip.mp4", kind=kind, name="clip", duration=duration,
                     width=1280, height=720, fps=25.0, has_video=kind != "audio",
                     has_audio=audio)


def make_project():
    p = Project()
    m = p.add_media(make_media())
    return p, m


def test_default_sequence_tracks():
    seq = Sequence.create_default()
    assert [seq.track_name(t.id) for t in seq.video_tracks] == ["V1", "V2", "V3"]
    assert [seq.track_name(t.id) for t in seq.audio_tracks] == ["A1", "A2"]


def test_add_and_remove_tracks():
    seq = Sequence.create_default()
    v4 = seq.add_track("video")
    a3 = seq.add_track("audio")
    assert seq.track_name(v4.id) == "V4" and seq.video_tracks[-1] is v4
    assert seq.track_name(a3.id) == "A3"
    seq.clips["c"] = Clip("video", v4.id, 0, 0, 1, id="c")
    assert not seq.remove_track(v4.id)  # not empty
    del seq.clips["c"]
    assert seq.remove_track(v4.id)
    assert len(seq.video_tracks) == 3


def test_clips_from_media_linked():
    p, m = make_project()
    seq = p.sequence
    clips = edits.clips_from_media(m, 2.0, seq.video_tracks[0].id, seq.audio_tracks[0].id)
    assert [c.kind for c in clips] == ["video", "audio"]
    assert clips[0].link_id and clips[0].link_id == clips[1].link_id
    edits.place_clips(seq, clips)
    assert seq.duration == pytest.approx(12.0)
    assert seq.linked(clips[0]) == [clips[1]]


def test_adopt_settings():
    seq = Sequence.create_default()
    assert edits.adopt_media_settings(seq, make_media())
    assert (seq.width, seq.height, seq.fps) == (1280, 720, 25.0)
    assert not edits.adopt_media_settings(seq, make_media())


def test_split_and_trim():
    p, m = make_project()
    seq = p.sequence
    v, a = edits.clips_from_media(m, 0.0, seq.video_tracks[0].id, seq.audio_tracks[0].id)
    edits.place_clips(seq, [v, a])
    new = edits.split_at(seq, 4.0, [v, a])
    assert len(new) == 2
    assert v.end == pytest.approx(4.0) and new[0].start == pytest.approx(4.0)
    assert new[0].in_point == pytest.approx(4.0)
    assert new[0].link_id == new[1].link_id != v.link_id
    edits.trim_end(v, 20.0, p)  # can't exceed source
    assert v.out_point == pytest.approx(10.0)
    edits.trim_start(new[0], -5.0, p)  # can't go before source 0
    assert new[0].in_point == pytest.approx(0.0) and new[0].start == pytest.approx(0.0)


def test_speed_changes_duration_and_source_time():
    c = Clip("video", "t", 1.0, 2.0, 6.0)
    assert c.duration == pytest.approx(4.0)
    edits.set_speed(c, 2.0)
    assert c.duration == pytest.approx(2.0)
    assert c.source_time(2.0) == pytest.approx(4.0)


def test_overwrite_splits_underlying():
    seq = Sequence.create_default()
    tid = seq.video_tracks[0].id
    base = Clip("video", tid, 0.0, 0.0, 10.0, id="base")
    seq.clips[base.id] = base
    top = Clip("video", tid, 3.0, 0.0, 2.0, id="top")
    edits.place_clips(seq, [top])
    clips = seq.clips_on(tid)
    assert [(round(c.start, 3), round(c.end, 3)) for c in clips] == [(0, 3), (3, 5), (5, 10)]
    assert clips[2].in_point == pytest.approx(5.0)


def test_ripple_delete_and_remove_gap():
    seq = Sequence.create_default()
    tid = seq.video_tracks[0].id
    a = Clip("video", tid, 0.0, 0.0, 2.0, id="a")
    b = Clip("video", tid, 2.0, 0.0, 3.0, id="b")
    c = Clip("video", tid, 6.0, 0.0, 1.0, id="c")
    for x in (a, b, c):
        seq.clips[x.id] = x
    edits.ripple_delete(seq, ["b"])
    assert c.start == pytest.approx(3.0)
    assert edits.remove_gap(seq, tid, 2.5)
    assert c.start == pytest.approx(2.0)


def test_join_clips_closes_gaps():
    seq = Sequence.create_default()
    tid = seq.video_tracks[0].id
    for i, s in enumerate([0.0, 5.0, 12.0]):
        seq.clips[f"c{i}"] = Clip("video", tid, s, 0.0, 2.0, id=f"c{i}")
    edits.join_clips(seq, ["c0", "c1", "c2"])
    assert [c.start for c in seq.clips_on(tid)] == pytest.approx([0.0, 2.0, 4.0])


def test_ripple_insert():
    seq = Sequence.create_default()
    tid = seq.video_tracks[0].id
    seq.clips["a"] = Clip("video", tid, 0.0, 0.0, 4.0, id="a")
    ins = Clip("video", tid, 0, 0.0, 1.0, id="ins")
    edits.ripple_insert(seq, [ins], 2.0)
    spans = [(c.id, round(c.start, 3), round(c.end, 3)) for c in seq.clips_on(tid)]
    assert spans[0] == ("a", 0.0, 2.0) and spans[1] == ("ins", 2.0, 3.0)
    assert spans[2][1:] == (3.0, 5.0)


def test_snap():
    assert edits.snap(4.95, [0, 5, 10], 0.1) == (5, 5)
    assert edits.snap(4.5, [0, 5, 10], 0.1) == (4.5, None)


def test_fade_and_transform_animation():
    c = Clip("video", "t", 0.0, 0.0, 10.0, fade_in=2.0, fade_out=2.0)
    assert c.fade_factor(1.0) == pytest.approx(0.5)
    assert c.fade_factor(5.0) == pytest.approx(1.0)
    assert c.fade_factor(9.0) == pytest.approx(0.5)
    c.transform.animate = True
    c.transform.scale_end = 200.0
    assert c.transform.at(0.5)[0] == pytest.approx(150.0)


def test_undo_redo_and_merge():
    p, m = make_project()
    undo = UndoStack(p)
    tid = p.sequence.video_tracks[0].id
    undo.push("add")
    p.sequence.clips["x"] = Clip("video", tid, 0, 0, 1, id="x")
    assert undo.is_dirty()
    undo.push("vol", merge_key="vol:x")
    p.sequence.clips["x"].volume = 50
    undo.push("vol", merge_key="vol:x")  # merged
    p.sequence.clips["x"].volume = 20
    assert undo.undo()
    assert p.sequence.clips["x"].volume == 100
    assert undo.undo()
    assert "x" not in p.sequence.clips
    assert not undo.is_dirty()
    assert undo.redo() and undo.redo()
    assert p.sequence.clips["x"].volume == 20


def test_save_load_roundtrip(tmp_path):
    p, m = make_project()
    seq = p.sequence
    clips = edits.clips_from_media(m, 1.0, seq.video_tracks[1].id, seq.audio_tracks[0].id)
    clips[0].effects.sepia = True
    clips[0].transform.scale = 150
    edits.place_clips(seq, clips)
    t = edits.make_text_clip(seq.video_tracks[2].id, 0.0, "Hello", "lower_third")
    seq.clips[t.id] = t
    path = os.path.join(tmp_path, "p.strata.json")
    save_project(p, path)
    json.load(open(path))  # valid JSON
    q = load_project(path)
    assert q.to_dict()["sequence"] == p.to_dict()["sequence"]
    assert q.sequence.clips[t.id].text.text == "Hello"
    assert q.sequence.clips[clips[0].id].effects.sepia


def test_reverse_clip_editing():
    from strata.model.project import Clip
    c = Clip(kind="video", track_id="v", start=10.0, in_point=1.0, out_point=5.0, reverse=True)
    assert c.source_time(10.0) == pytest.approx(5.0)
    assert c.source_time(12.0) == pytest.approx(3.0)
    seq = Sequence()
    seq.clips[c.id] = c
    right = edits.split_clip(seq, c, 12.0)
    assert (c.in_point, c.out_point) == pytest.approx((3.0, 5.0))  # left shows 5 -> 3
    assert (right.in_point, right.out_point) == pytest.approx((1.0, 3.0))  # right shows 3 -> 1
    assert right.source_time(12.0) == pytest.approx(3.0)
    edits.trim_end(right, 13.0)
    assert right.in_point == pytest.approx(2.0)
    edits.trim_start(c, 11.0)
    assert (c.start, c.out_point) == pytest.approx((11.0, 4.0))
    assert c.source_time(11.0) == pytest.approx(4.0)  # content stays anchored


def test_remove_ranges_and_split_at_times():
    from strata.model.project import Clip
    seq = Project().sequence
    v = seq.video_tracks[0].id
    a = seq.audio_tracks[0].id
    vid = Clip(kind="video", track_id=v, start=0, in_point=0, out_point=10, link_id="L")
    aud = Clip(kind="audio", track_id=a, start=0, in_point=0, out_point=10, link_id="L")
    after = Clip(kind="video", track_id=v, start=10, in_point=0, out_point=2)
    for c in (vid, aud, after):
        seq.clips[c.id] = c
    removed = edits.remove_ranges(seq, [vid, aud], [(2, 3), (6, 8)])
    assert removed == pytest.approx(3)
    vs = sorted((c.start, c.end, c.in_point) for c in seq.clips_on(v))
    assert vs == [pytest.approx((0, 2, 0)), pytest.approx((2, 5, 3)), pytest.approx((5, 7, 8)), pytest.approx((7, 9, 0))]
    assert len(seq.clips_on(a)) == 3  # audio cut the same way
    assert edits.source_to_timeline(seq.clips_on(v)[1], 4.0) == pytest.approx(3.0)
    # razor at scene times
    seq2 = Project().sequence
    c = Clip(kind="video", track_id=seq2.video_tracks[0].id, start=0, in_point=0, out_point=6)
    seq2.clips[c.id] = c
    assert edits.split_at_times(seq2, [c], [2.0, 4.0]) == 2
    assert [round(x.start) for x in seq2.clips_on(seq2.video_tracks[0].id)] == [0, 2, 4]
