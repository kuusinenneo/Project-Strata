import os

import pytest

from strata.export.exporter import ExportCancelled, Exporter, ExportError
from strata.export.settings import (CUSTOM_BITRATE, ExportSettings, estimate_size_bytes,
                                    format_size, resolve_fps, resolve_size, video_args)
from strata.media.probe import probe
from strata.model import Project
from strata.model import edits


@pytest.fixture
def project(media_dir):
    p = Project()
    seq = p.sequence
    vid = p.add_media(probe(str(media_dir / "video.mp4")))
    img = p.add_media(probe(str(media_dir / "blue.png")))
    edits.adopt_media_settings(seq, vid)
    edits.place_clips(seq, edits.clips_from_media(vid, 0, seq.video_tracks[0].id,
                                                  seq.audio_tracks[0].id))
    still = edits.clips_from_media(img, 3.0, seq.video_tracks[0].id, None)[0]
    edits.trim_end(still, 4.0, p)
    still.cross_dissolve = 0.5
    edits.place_clips(seq, [still])
    t = edits.make_text_clip(seq.video_tracks[1].id, 0.5, "Hello", "lower_third", 2.0)
    seq.clips[t.id] = t
    return p


def test_settings_resolution(project):
    s = ExportSettings(resolution="720p")
    assert resolve_size(project, s) == (960, 720)  # 4:3 sequence from the 320x240 clip
    assert resolve_size(project, ExportSettings()) == (320, 240)
    assert resolve_fps(project, ExportSettings()) == pytest.approx(25.0)
    assert resolve_fps(project, ExportSettings(frame_rate="60")) == 60.0
    assert "-crf" in video_args(ExportSettings(quality="Low"))
    assert "-b:v" in video_args(ExportSettings(quality=CUSTOM_BITRATE, custom_bitrate_kbps=5000))
    small = estimate_size_bytes(project, ExportSettings(quality="Low"))
    big = estimate_size_bytes(project, ExportSettings(quality="Very high", resolution="4K (2160p)"))
    assert 0 < small < big
    assert format_size(1536 * 1024).endswith("MB")


def test_export_mp4(qapp, project, tmp_path):
    out = str(tmp_path / "out.mp4")
    calls = []
    s = ExportSettings(output_path=out, frame_rate="25", quality="Medium", speed="Fast")
    Exporter(project, s).run(lambda d, t, eta: calls.append((d, t)))
    assert os.path.getsize(out) > 1000
    m = probe(out)
    assert m.has_video and m.has_audio
    assert (m.width, m.height) == (320, 240)
    assert m.duration == pytest.approx(4.0, abs=0.15)
    assert calls[-1][0] == calls[-1][1] == 100


def test_export_cancel(qapp, project, tmp_path):
    out = str(tmp_path / "cancel.mp4")
    ex = Exporter(project, ExportSettings(output_path=out))
    def progress(done, total, eta):
        if done >= 10:
            ex.cancel()
    with pytest.raises(ExportCancelled):
        ex.run(progress)
    assert not os.path.exists(out)


def test_export_empty(qapp, tmp_path):
    with pytest.raises(ExportError):
        Exporter(Project(), ExportSettings(output_path=str(tmp_path / "e.mp4"))).run()


def test_platform_presets(qapp, project, tmp_path):
    from strata.export.settings import EXPORT_PRESETS, apply_preset
    sizes = {name: resolve_size(project, apply_preset(ExportSettings(), name)) for name in EXPORT_PRESETS}
    assert sizes == {"YouTube 1080p": (1920, 1080), "TikTok / Reels / Shorts (1080 × 1920)": (1080, 1920),
                     "Instagram square (1080 × 1080)": (1080, 1080)}
    # a vertical export of the 4:3 sequence: clips are fitted into the tall frame
    out = str(tmp_path / "vertical.mp4")
    s = apply_preset(ExportSettings(output_path=out, speed="Fast", range_end=1.0), "TikTok / Reels / Shorts (1080 × 1920)")
    Exporter(project, s).run(lambda d, t, eta: None)
    m = probe(out)
    assert (m.width, m.height) == (1080, 1920)
