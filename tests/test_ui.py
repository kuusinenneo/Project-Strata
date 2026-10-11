"""Headless smoke tests for the main window and the timeline interactions."""
import os

import pytest
from PySide6.QtCore import QPoint, Qt
from PySide6.QtTest import QTest

from strata.ui.timeline import RULER_H


@pytest.fixture
def win(qapp, media_dir, tmp_path, monkeypatch):
    monkeypatch.setenv("APPDATA", str(tmp_path))  # keep QSettings out of the real profile
    from strata.ui.main_window import MainWindow
    from strata.ui.theme import apply_dark_theme
    apply_dark_theme(qapp)
    w = MainWindow()
    w.resize(1400, 900)
    w.show()
    qapp.processEvents()
    yield w
    w.controller.undo.mark_clean()
    w.close()


def media(win, media_dir, *names):
    added, errors = win.controller.import_files([str(media_dir / n) for n in names])
    assert not errors
    return added


def test_layout_and_docks(win):
    titles = {d.windowTitle() for d in win.docks}
    assert titles == {"Source", "Effect Controls", "Program", "Project", "Effects", "Tools",
                      "Timeline", "Audio"}
    win.d_program.setFloating(True)
    win.d_timeline.hide()
    win.apply_default_layout()
    assert not win.d_program.isFloating() and win.d_timeline.isVisible()
    seq = win.controller.project.sequence
    assert [seq.track_name(t.id) for t in seq.tracks()] == ["V1", "V2", "V3", "A1", "A2"]


def test_import_place_and_edit(win, media_dir, qapp):
    c = win.controller
    vid, img = media(win, media_dir, "video.mp4", "blue.png")
    assert win.project_panel.tree.topLevelItemCount() == 2
    placed = c.add_media_to_timeline([vid.id, img.id], c.project.sequence.video_tracks[0].id, 0.0)
    assert len(placed) == 3  # video + linked audio + image
    seq = c.project.sequence
    assert (seq.width, seq.height) == (320, 240)  # adopted from first clip
    assert seq.duration == pytest.approx(8.0, abs=0.1)

    win.program.request_render()
    qapp.processEvents()
    img_out = win.program.render_frame(1.0)
    assert img_out is not None and not img_out.isNull()

    c.set_playhead(1.5)
    c.clear_selection()
    assert c.cut_at(1.5) == 2  # video + linked audio
    assert len(seq.clips) == 5
    c.do_undo()
    assert len(c.project.sequence.clips) == 3
    c.do_redo()
    assert len(c.project.sequence.clips) == 5

    # ripple delete the first piece; everything shifts left
    seq = c.project.sequence
    first = min((cl for cl in seq.clips.values() if cl.kind == "video"), key=lambda x: x.start)
    c.delete_clips([first.id], ripple=True)
    assert min(cl.start for cl in seq.clips.values()) == pytest.approx(0.0)

    # text + effects
    t = c.add_text(None, 0.5, "lower_third")
    assert t.kind == "text" and c.selection == {t.id}
    v = next(cl for cl in seq.clips.values() if cl.kind == "video")
    assert c.apply_effect(v.id, "sepia")
    assert seq.clips[v.id].effects.sepia
    assert c.apply_effect(v.id, "zoom_in")
    assert seq.clips[v.id].transform.animate
    assert c.apply_effect(v.id, "fade_in")
    assert seq.clips[v.id].fade_in > 0
    for key in ("brightness_contrast", "saturation", "black_white", "blur", "opacity", "zoom_out"):
        assert c.apply_effect(v.id, key)
    win.timeline.canvas.grab()  # exercise painting with all decorations


def test_effect_controls_edit_and_undo_merge(win, media_dir):
    c = win.controller
    (vid,) = media(win, media_dir, "video.mp4")
    clips = c.add_media_to_timeline([vid.id], None, 0.0)
    c.select([clips[0].id])
    ec = win.effect_controls
    assert ec.clip_id == clips[0].id
    ec.scale.setValue(10)  # programmatic setValue must not create an edit
    assert c.project.sequence.clips[clips[0].id].transform.scale == 100
    ec.scale.spin.setValue(150)
    ec.scale.spin.setValue(160)  # merged into the same undo step
    assert c.project.sequence.clips[clips[0].id].transform.scale == 160
    ec.volume.spin.setValue(50)
    audio = next(x for x in c.project.sequence.clips.values() if x.kind == "audio")
    assert audio.volume == 50
    c.do_undo()  # volume
    c.do_undo()  # scale (both changes)
    assert c.project.sequence.clips[clips[0].id].transform.scale == 100
    ec.speed.spin.setValue(2.0)
    v = c.project.sequence.clips[clips[0].id]
    assert v.speed == 2.0 and v.duration == pytest.approx(1.5, abs=0.05)


def test_text_controls(win):
    c = win.controller
    t = c.add_text(c.project.sequence.video_tracks[0].id, 0.0)
    ec = win.effect_controls
    ec.text_edit.setPlainText("Hello Strata")
    assert c.project.sequence.clips[t.id].text.text == "Hello Strata"
    ec.preset.setCurrentIndex(ec.preset.findData("subtitle"))
    assert c.project.sequence.clips[t.id].text.preset == "subtitle"
    ec.bg_box.setChecked(True)
    assert c.project.sequence.clips[t.id].text.bg_box


def _clip_center(canvas, clip):
    r = canvas.clip_rect(clip)
    return QPoint(int(r.center().x()), int(r.center().y()))


def test_timeline_mouse_tools(win, media_dir, qapp):
    c = win.controller
    (vid,) = media(win, media_dir, "red.mp4")
    (clip,) = c.add_media_to_timeline([vid.id], c.project.sequence.video_tracks[0].id, 1.0)
    canvas = win.timeline.canvas
    canvas.pps = 100.0
    canvas.hscroll = 0.0
    qapp.processEvents()

    # click in the ruler moves the playhead
    QTest.mouseClick(canvas, Qt.LeftButton, Qt.NoModifier, QPoint(150, RULER_H // 2))
    assert c.playhead == pytest.approx(1.5, abs=0.02)

    # selection: click selects
    c.clear_selection()
    QTest.mouseClick(canvas, Qt.LeftButton, Qt.NoModifier, _clip_center(canvas, clip))
    assert clip.id in c.selection

    # drag-move to V2 and 1 s later
    c.set_snapping(False)
    start = _clip_center(canvas, clip)
    v2_row = next(r for r in canvas.rows() if r.track.id == c.project.sequence.video_tracks[1].id)
    end = QPoint(start.x() + 100, int(RULER_H + v2_row.y + v2_row.h / 2))
    QTest.mousePress(canvas, Qt.LeftButton, Qt.NoModifier, start)
    QTest.mouseMove(canvas, QPoint(start.x() + 10, start.y()))
    QTest.mouseMove(canvas, end)
    QTest.mouseRelease(canvas, Qt.LeftButton, Qt.NoModifier, end)
    moved = c.project.sequence.clips[clip.id]
    assert moved.start == pytest.approx(2.0, abs=0.02)
    assert moved.track_id == c.project.sequence.video_tracks[1].id

    # trim right edge by dragging 50 px left
    r = canvas.clip_rect(moved)
    edge = QPoint(int(r.right()) - 2, int(r.center().y()))
    QTest.mousePress(canvas, Qt.LeftButton, Qt.NoModifier, edge)
    QTest.mouseMove(canvas, QPoint(edge.x() - 25, edge.y()))
    QTest.mouseMove(canvas, QPoint(edge.x() - 50, edge.y()))
    QTest.mouseRelease(canvas, Qt.LeftButton, Qt.NoModifier, QPoint(edge.x() - 50, edge.y()))
    assert c.project.sequence.clips[clip.id].duration == pytest.approx(1.5, abs=0.05)

    # razor tool splits at the click
    c.set_tool("razor")
    trimmed = c.project.sequence.clips[clip.id]
    r = canvas.clip_rect(trimmed)
    QTest.mouseClick(canvas, Qt.LeftButton, Qt.NoModifier, QPoint(int(r.left() + 50), int(r.center().y())))
    on_v2 = c.project.sequence.clips_on(c.project.sequence.video_tracks[1].id)
    assert len(on_v2) == 2

    # ripple tool deletes the first piece and closes the gap
    c.set_tool("ripple")
    QTest.mouseClick(canvas, Qt.LeftButton, Qt.NoModifier, _clip_center(canvas, on_v2[0]))
    on_v2 = c.project.sequence.clips_on(c.project.sequence.video_tracks[1].id)
    assert len(on_v2) == 1 and on_v2[0].start == pytest.approx(2.0, abs=0.02)

    # text tool adds a text clip on the clicked track
    c.set_tool("text")
    v3_row = next(r for r in canvas.rows() if r.track.id == c.project.sequence.video_tracks[2].id)
    QTest.mouseClick(canvas, Qt.LeftButton, Qt.NoModifier, QPoint(50, int(RULER_H + v3_row.y + 10)))
    assert any(x.kind == "text" for x in c.project.sequence.clips.values())
    assert c.tool == "select"


def test_tracks_add_remove_flags(win):
    c = win.controller
    seq = c.project.sequence
    v4 = c.add_track("video")
    a3 = c.add_track("audio")
    seq = c.project.sequence
    assert seq.track_name(v4.id) == "V4" and seq.track_name(a3.id) == "A3"
    headers = win.timeline.headers.headers
    assert headers[v4.id].del_btn.isEnabled()
    headers[v4.id].solo_btn.click()
    assert c.project.sequence.track(v4.id).solo
    headers[v4.id].lock_btn.click()
    assert c.project.sequence.track(v4.id).locked
    assert c.remove_track(a3.id)
    headers[c.project.sequence.video_tracks[0].id].add_btn.click()
    assert len(c.project.sequence.video_tracks) == 5


def test_shortcuts_route(win, media_dir, qapp):
    c = win.controller
    QTest.keyClick(win, Qt.Key_C)
    assert c.tool == "razor"
    QTest.keyClick(win, Qt.Key_V)
    assert c.tool == "select"
    (vid,) = media(win, media_dir, "video.mp4")
    c.add_media_to_timeline([vid.id], None, 0.0)
    pps = win.timeline.canvas.pps
    win.a_zoom_in.trigger()
    assert win.timeline.canvas.pps > pps
    c.set_playhead(1.0)
    win.a_in.trigger()
    c.set_playhead(2.0)
    win.a_out.trigger()
    assert (c.project.sequence.mark_in, c.project.sequence.mark_out) == (1.0, 2.0)
    win.a_play.trigger()
    assert win.program.player.playing
    for _ in range(5):
        QTest.qWait(20)
    win.a_k.trigger()
    assert not win.program.player.playing and c.playhead > 1.0
    win.a_l.trigger()
    win.a_l.trigger()
    assert win.program.player.rate == 2.0
    win.a_k.trigger()
    c.select_all()
    win.a_delete.trigger()
    assert not c.project.sequence.clips
    win.a_undo.trigger()
    assert c.project.sequence.clips


def test_source_monitor_marks(win, media_dir):
    c = win.controller
    (vid,) = media(win, media_dir, "video.mp4")
    win._open_in_source(vid.id)
    assert win.active_monitor is win.source
    win.source.player.seek(0.5)
    win.a_in.trigger()
    win.source.player.seek(1.5)
    win.a_out.trigger()
    assert (vid.mark_in, vid.mark_out) == (0.5, 1.5)
    assert win.source.render_frame(1.0) is not None
    win.a_overwrite.trigger()
    clips = list(c.project.sequence.clips.values())
    assert clips and all(x.duration == pytest.approx(1.0) for x in clips)


def test_save_open_roundtrip(win, media_dir, tmp_path):
    c = win.controller
    (vid,) = media(win, media_dir, "video.mp4")
    c.add_media_to_timeline([vid.id], None, 0.0)
    path = str(tmp_path / "proj.strata.json")
    assert win._save_to(path)
    assert not c.undo.is_dirty()
    c.new_project()
    assert not c.project.sequence.clips
    assert c.open_project(path) == []
    assert len(c.project.sequence.clips) == 2
    assert os.path.basename(path) in win.windowTitle()


def test_export_dialog_summary(win, media_dir):
    from strata.ui.export_dialog import ExportDialog
    c = win.controller
    (vid,) = media(win, media_dir, "video.mp4")
    c.add_media_to_timeline([vid.id], None, 0.0)
    dlg = ExportDialog(c, win)
    assert "Estimated file size" in dlg.summary.text()
    dlg.res.setCurrentIndex(dlg.res.findData("1080p"))
    assert "1440×1080" in dlg.summary.text()
    dlg.quality.setCurrentIndex(dlg.quality.findData("Custom bitrate"))
    assert dlg.bitrate.isEnabled()


def _drop(canvas, mime, pos, modifiers=Qt.NoModifier):
    from PySide6.QtCore import QPointF
    from PySide6.QtGui import QDragEnterEvent, QDragMoveEvent, QDropEvent
    p = QPointF(pos)
    canvas.dragEnterEvent(QDragEnterEvent(pos, Qt.CopyAction, mime, Qt.LeftButton, modifiers))
    canvas.dragMoveEvent(QDragMoveEvent(pos, Qt.CopyAction, mime, Qt.LeftButton, modifiers))
    canvas.dropEvent(QDropEvent(p, Qt.CopyAction, mime, Qt.LeftButton, modifiers))


def test_drag_drop_media_and_effects(win, media_dir, qapp):
    from PySide6.QtCore import QMimeData
    from strata.ui.mime import EFFECT_MIME, MEDIA_MIME
    c = win.controller
    vid, png = media(win, media_dir, "video.mp4", "blue.png")
    canvas = win.timeline.canvas
    canvas.pps, canvas.hscroll = 100.0, 0.0
    seq = c.project.sequence
    v2 = next(r for r in canvas.rows() if r.track.id == seq.video_tracks[1].id)
    # media from the Project panel (tree builds the same mime type)
    items = [win.project_panel.tree.topLevelItem(i) for i in range(2)]
    mime = win.project_panel.tree.mimeData(items)
    assert mime.hasFormat(MEDIA_MIME)
    _drop(canvas, mime, QPoint(100, int(RULER_H + v2.y + 20)))
    seq = c.project.sequence
    v2_clips = seq.clips_on(seq.video_tracks[1].id)
    assert [round(x.start, 2) for x in v2_clips] == [1.0, 4.0]  # placed end to end
    assert seq.clips_on(seq.audio_tracks[1].id)  # linked audio lands on A2
    # effect drop onto the first clip
    m2 = QMimeData()
    m2.setData(EFFECT_MIME, b"black_white")
    _drop(canvas, m2, _clip_center(canvas, v2_clips[0]))
    assert c.project.sequence.clips[v2_clips[0].id].effects.black_white
    # cross dissolve dropped near the end of clip 1 goes onto clip 2
    m3 = QMimeData()
    m3.setData(EFFECT_MIME, b"cross_dissolve")
    r = canvas.clip_rect(v2_clips[0])
    _drop(canvas, m3, QPoint(int(r.right() - 5), int(r.center().y())))
    assert c.project.sequence.clips[v2_clips[1].id].cross_dissolve > 0
    # text preset dropped on V3
    m4 = QMimeData()
    m4.setData(EFFECT_MIME, b"text:subtitle")
    v3 = next(r for r in canvas.rows() if r.track.id == seq.video_tracks[2].id)
    _drop(canvas, m4, QPoint(300, int(RULER_H + v3.y + 20)))
    texts = [x for x in c.project.sequence.clips.values() if x.kind == "text"]
    assert texts and texts[0].text.preset == "subtitle" and texts[0].start == pytest.approx(3.0, abs=0.1)
    # Ctrl+drop inserts (ripples) instead of overwriting
    before = c.project.sequence.clips[v2_clips[1].id].start
    mime = win.project_panel.tree.mimeData([items[1]])
    _drop(canvas, mime, QPoint(100, int(RULER_H + v2.y + 20)), Qt.ControlModifier)
    assert c.project.sequence.clips[v2_clips[1].id].start == pytest.approx(before + 5.0)
