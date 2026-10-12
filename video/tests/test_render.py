import numpy as np
import pytest

from strata.media.audio import AudioStore
from strata.media.probe import probe
from strata.model import Effects, Project
from strata.model import edits
from strata.render import effects as fx
from strata.render.compositor import Compositor, track_layers
from strata.render.mixer import AudioMixer


def pixel(img, x, y):
    c = img.pixelColor(x, y)
    return c.red(), c.green(), c.blue()


@pytest.fixture
def project(media_dir):
    p = Project()
    p.sequence.width, p.sequence.height = 320, 180
    red = p.add_media(probe(str(media_dir / "red.mp4")))
    blue = p.add_media(probe(str(media_dir / "blue.png")))
    tone = p.add_media(probe(str(media_dir / "tone.wav")))
    return p, red, blue, tone


def test_effects_color():
    img = np.full((4, 4, 3), 200, np.uint8)
    img[..., 0] = 250
    bw = fx.apply_color(img, Effects(black_white=True))
    assert np.all(bw[..., 0] == bw[..., 1])
    dark = fx.apply_color(img, Effects(brightness=-100))
    assert dark.mean() < img.mean()
    sep = fx.apply_color(img, Effects(sepia=True))
    assert sep[0, 0, 0] >= sep[0, 0, 2]
    assert fx.apply_color(img, Effects()) is img
    blurred = fx.blur(np.random.randint(0, 255, (60, 80, 3), dtype=np.uint8), 4)
    assert blurred.dtype == np.uint8 and blurred.std() < 60


def test_layers_stack_upper_on_top(qapp, project):
    p, red, blue, _ = project
    seq = p.sequence
    v1, v2 = seq.video_tracks[0].id, seq.video_tracks[1].id
    edits.place_clips(seq, edits.clips_from_media(red, 0, v1, None))
    comp = Compositor(p)
    r, g, b = pixel(comp.render(0.5, 320, 180), 160, 90)
    assert r > 200 and b < 60
    small = edits.clips_from_media(blue, 0, v2, None)[0]
    small.transform.scale = 50
    edits.place_clips(seq, [small])
    img = comp.render(0.5, 320, 180)
    assert pixel(img, 160, 90)[2] > 200  # blue on top in the centre
    assert pixel(img, 50, 90)[0] > 200  # red visible outside the scaled image
    assert pixel(img, 10, 90) == (0, 0, 0)  # 4:3 clip is pillarboxed
    seq.video_tracks[1].enabled = False  # hide V2
    assert pixel(comp.render(0.5, 320, 180), 160, 90)[0] > 200
    seq.video_tracks[1].enabled = True
    seq.video_tracks[0].solo = True  # solo V1
    assert pixel(comp.render(0.5, 320, 180), 160, 90)[0] > 200


def test_opacity_fade_and_dissolve(qapp, project):
    p, red, blue, _ = project
    seq = p.sequence
    v1 = seq.video_tracks[0].id
    a = edits.clips_from_media(red, 0, v1, None)[0]  # 0..2
    b = edits.clips_from_media(blue, 2.0, v1, None)[0]  # 2..7
    edits.place_clips(seq, [a, b])
    comp = Compositor(p)
    b.cross_dissolve = 1.0
    layers = track_layers(seq, v1, 2.0)
    assert [c.id for c, _ in layers] == [a.id, b.id] and layers[1][1] == pytest.approx(0.5)
    mid = pixel(comp.render(2.0, 320, 180), 160, 90)
    assert 80 < mid[0] < 180 and 80 < mid[2] < 180  # half red, half blue
    b.cross_dissolve = 0
    b.effects.opacity = 0
    assert pixel(comp.render(3.0, 320, 180), 160, 90) == (0, 0, 0)
    b.effects.opacity = 100
    b.fade_in = 2.0
    assert pixel(comp.render(3.0, 320, 180), 160, 90)[2] == pytest.approx(127, abs=10)


def test_gradual_zoom(qapp, project):
    p, red, blue, _ = project
    seq = p.sequence
    c = edits.clips_from_media(blue, 0, seq.video_tracks[0].id, None)[0]
    c.transform.animate = True
    c.transform.scale = 20
    c.transform.scale_end = 100
    edits.place_clips(seq, [c])
    comp = Compositor(p)
    assert pixel(comp.render(0.0, 320, 180), 100, 90) == (0, 0, 0)
    assert pixel(comp.render(4.99, 320, 180), 100, 90)[2] > 200


def test_text_overlay(qapp, project):
    p, *_ = project
    seq = p.sequence
    t = edits.make_text_clip(seq.video_tracks[2].id, 0, "XXXXXXXX", "title")
    t.text.bg_box = True
    t.text.bg_color = "#00ff00"
    t.text.bg_opacity = 100
    seq.clips[t.id] = t
    img = Compositor(p).render(1.0, 320, 180)
    from strata.render.text import text_layout
    _, text_rect, box_rect, _ = text_layout(t.text, 320, 180)
    inside_box = (int(box_rect.left() + 1), int(box_rect.center().y()))
    assert pixel(img, *inside_box)[1] > 200
    assert pixel(img, 5, 5) == (0, 0, 0)
    for preset in ("lower_third", "top_caption", "subtitle"):
        t.text.preset = preset
        Compositor(p).render(1.0, 320, 180)


def test_audio_mix(project):
    p, red, blue, tone = project
    seq = p.sequence
    a1, a2 = seq.audio_tracks[0].id, seq.audio_tracks[1].id
    c1 = edits.clips_from_media(tone, 0, None, a1)[0]
    c2 = edits.clips_from_media(tone, 0, None, a2)[0]
    edits.place_clips(seq, [c1, c2])
    mixer = AudioMixer(p, AudioStore())
    one = mixer.mix(0.5, 4800, wait=True)
    seq.audio_tracks[1].enabled = False
    single = mixer.mix(0.5, 4800, wait=True)
    assert np.abs(one).max() == pytest.approx(2 * np.abs(single).max(), rel=0.02)
    c1.volume = 50
    assert np.abs(mixer.mix(0.5, 4800, wait=True)).max() == pytest.approx(
        0.5 * np.abs(single).max(), rel=0.02)
    assert np.abs(mixer.mix(5.0, 4800, wait=True)).max() == 0.0  # past the end


def test_dip_to_black_and_wipe(qapp, project):
    p, red, blue, _ = project
    seq = p.sequence
    v1 = seq.video_tracks[0].id
    a = edits.clips_from_media(red, 0, v1, None)[0]  # 0..2
    b = edits.clips_from_media(blue, 2.0, v1, None)[0]  # 2..7
    edits.place_clips(seq, [a, b])
    comp = Compositor(p)
    b.cross_dissolve = 1.0  # window 1.5..2.5
    b.transition = "dip_black"
    assert max(pixel(comp.render(2.0, 320, 180), 160, 90)) < 10  # black in the middle
    assert pixel(comp.render(1.75, 320, 180), 160, 90)[0] == pytest.approx(127, abs=12)  # red half faded
    assert pixel(comp.render(2.25, 320, 180), 160, 90)[2] == pytest.approx(127, abs=12)  # blue half in
    b.transition = "wipe"
    img = comp.render(2.0, 320, 180)  # halfway: blue on the left, red on the right
    assert pixel(img, 60, 90)[2] > 200 and pixel(img, 60, 90)[0] < 40
    assert pixel(img, 260, 90)[0] > 200 and pixel(img, 260, 90)[2] < 40
    # survives save/load
    from strata.model.project import Project
    assert Project.from_dict(p.to_dict()).sequence.clips[b.id].transition == "wipe"


def test_keyframes_interpolate_render_and_survive_edits(qapp, project):
    from strata.model.project import Project
    p, red, blue, _ = project
    seq = p.sequence
    v1 = seq.video_tracks[0].id
    (b,) = [c for c in edits.clips_from_media(blue, 0, v1, None) if c.kind == "video"]  # 0..5
    edits.place_clips(seq, [b])
    b.set_keyframe(1.0, "opacity", 0.0)
    b.set_keyframe(3.0, "opacity", 100.0)
    b.set_keyframe(3.0, "rotation", 90.0)
    assert [k.t for k in b.transform.keyframes] == [1.0, 3.0]
    assert b.motion_at(0.0)["opacity"] == 0.0  # held before the first keyframe
    assert b.motion_at(2.0)["opacity"] == pytest.approx(50.0)
    assert b.motion_at(2.0)["rotation"] == pytest.approx(45.0)
    assert b.motion_at(4.5)["rotation"] == 90.0
    comp = Compositor(p)
    assert pixel(comp.render(2.0, 320, 180), 160, 90)[2] == pytest.approx(127, abs=12)
    # rotated 90°: a 16:9 frame turned on its side leaves the left/right edges black
    img = comp.render(4.0, 320, 180)
    assert pixel(img, 160, 90)[2] > 200 and max(pixel(img, 5, 90)) < 20
    # save/load
    again = Project.from_dict(p.to_dict()).sequence.clips[b.id]
    assert again.motion_at(2.0)["opacity"] == pytest.approx(50.0)
    # splitting and trimming keep keyframes at the same timeline positions
    right = edits.split_clip(seq, b, 2.0)
    assert right.motion_at(2.5)["opacity"] == pytest.approx(b.motion_at(2.5)["opacity"])
    edits.trim_start(b, 0.5, p)
    assert b.motion_at(1.5)["opacity"] == pytest.approx(25.0)
    assert b.remove_keyframe(1.0) and len(b.transform.keyframes) == 1


def _write_invert_cube(path):
    lines = ["TITLE \"invert\"", "LUT_3D_SIZE 2"]
    for b in (0, 1):
        for g in (0, 1):
            for r in (0, 1):
                lines.append(f"{1 - r} {1 - g} {1 - b}")
    path.write_text("\n".join(lines))


def test_cube_lut(tmp_path, qapp, project):
    import numpy as np
    from strata.render.lut import load_lut, parse_cube
    cube = tmp_path / "invert.cube"
    _write_invert_cube(cube)
    lut = load_lut(str(cube))
    assert lut is not None and lut.size == 2 and lut.title == "invert"
    px = np.array([[[200, 100, 50]]], np.uint8)
    assert lut.apply(px)[0, 0].tolist() == [55, 155, 205]
    assert lut.apply(px, 0.5)[0, 0].tolist() == pytest.approx([128, 128, 128], abs=1)
    with pytest.raises(ValueError):
        parse_cube("LUT_3D_SIZE 3\n0 0 0\n")
    assert load_lut(str(tmp_path / "missing.cube")) is None
    # through the compositor: a red clip comes out cyan
    p, red, blue, _ = project
    seq = p.sequence
    (a,) = [c for c in edits.clips_from_media(red, 0, seq.video_tracks[0].id, None) if c.kind == "video"]
    edits.place_clips(seq, [a])
    a.effects.lut_path = str(cube)
    r, g, b = pixel(Compositor(p).render(1.0, 320, 180), 160, 90)
    assert r < 30 and g > 220 and b > 220


def test_reverse_audio_plays_backwards(project):
    from strata.media.audio import AudioData
    p, red, blue, tone = project
    seq = p.sequence
    c = edits.clips_from_media(tone, 0, None, seq.audio_tracks[0].id)[0]
    edits.place_clips(seq, [c])
    store = AudioStore()
    sr = store.sample_rate
    ramp = (np.arange(int(tone.duration * sr)) % 30000).astype(np.int16)  # increasing values
    store._data[tone.id] = AudioData(np.stack([ramp, ramp], 1), sr, np.zeros(1, np.float32))
    mixer = AudioMixer(p, store)
    fwd = mixer.mix(0.2, 2000)[:, 0]
    assert np.all(np.diff(fwd) >= 0)
    c.reverse = True
    rev = mixer.mix(0.2, 2000)[:, 0]
    assert np.all(np.diff(rev) <= 0)
