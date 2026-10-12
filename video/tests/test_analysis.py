"""Silence and scene-cut detection."""
import subprocess

import numpy as np
import pytest

from strata.media.analysis import scene_cuts, silent_ranges
from strata.media.ffmpeg import find_ffmpeg


@pytest.fixture(scope="session")
def gap_audio(tmp_path_factory):
    """1 s tone, 1.5 s silence, 1 s tone (as WAV)."""
    path = tmp_path_factory.mktemp("an") / "gap.wav"
    subprocess.run([find_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "aevalsrc='if(between(t,1,2.5),0,0.5*sin(440*2*PI*t))':s=48000:d=3.5",
                    "-ac", "2", str(path)], check=True)
    return path


@pytest.fixture(scope="session")
def three_scenes(tmp_path_factory):
    """Red 0-1 s, green 1-2 s, blue 2-3 s."""
    path = tmp_path_factory.mktemp("an") / "scenes.mp4"
    subprocess.run([find_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "color=c=red:s=160x90:r=25:d=1",
                    "-f", "lavfi", "-i", "color=c=green:s=160x90:r=25:d=1",
                    "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=25:d=1",
                    "-filter_complex", "[0][1][2]concat=n=3:v=1:a=0", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    str(path)], check=True)
    return path


def test_silent_ranges_finds_the_gap(gap_audio):
    from strata.media.audio import decode_audio
    data = decode_audio(str(gap_audio))
    ranges = silent_ranges(data.samples, data.sample_rate, 0.0, data.duration, padding=0.1)
    assert len(ranges) == 1
    a, b = ranges[0]
    assert a == pytest.approx(1.1, abs=0.05) and b == pytest.approx(2.4, abs=0.05)
    # too short to count
    assert silent_ranges(data.samples, data.sample_rate, 0.0, data.duration, min_silence=2.0) == []
    # within a sub-range only
    assert silent_ranges(data.samples, data.sample_rate, 2.6, 3.4) == []


def test_silent_ranges_synthetic_edges():
    sr = 1000
    s = np.zeros((3000, 2), np.int16)
    s[1000:2000] = 10000  # loud middle second
    ranges = silent_ranges(s, sr, 0.0, 3.0, padding=0.0, window=0.01)
    assert ranges == [pytest.approx((0.0, 1.0), abs=0.02), pytest.approx((2.0, 3.0), abs=0.02)]


def test_builtin_scene_detection(three_scenes):
    cuts = scene_cuts(str(three_scenes), 0.0, 3.0, use_pyscenedetect=False)
    assert len(cuts) == 2
    assert cuts[0] == pytest.approx(1.0, abs=0.08) and cuts[1] == pytest.approx(2.0, abs=0.08)
    assert scene_cuts(str(three_scenes), 1.2, 3.0, use_pyscenedetect=False) == [pytest.approx(2.0, abs=0.08)]


@pytest.fixture(scope="session")
def talk_video(tmp_path_factory):
    """3.5 s video whose audio is tone / 1.5 s silence / tone."""
    path = tmp_path_factory.mktemp("an") / "talk.mp4"
    subprocess.run([find_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "testsrc=size=160x90:rate=25:duration=3.5",
                    "-f", "lavfi", "-i", "aevalsrc='if(between(t,1,2.5),0,0.5*sin(440*2*PI*t))':s=48000:d=3.5",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", str(path)], check=True)
    return path


def test_controller_remove_silence_and_detect_scenes(talk_video, three_scenes, qapp):
    from strata.ui.controller import EditorController
    c = EditorController()
    added, errors = c.import_files([str(talk_video), str(three_scenes)])
    assert not errors
    talk, scenes = added
    c.add_media_to_timeline([talk.id], None, 0.0)
    clip = next(x for x in c.project.sequence.clips.values() if x.kind == "video")
    removed = c.remove_silence([clip.id], padding=0.1)
    assert removed == pytest.approx(1.3, abs=0.1)
    assert c.project.sequence.duration == pytest.approx(3.5 - removed, abs=0.05)
    assert len([x for x in c.project.sequence.clips.values() if x.kind == "audio"]) == 2
    c.do_undo()
    assert c.project.sequence.duration == pytest.approx(3.5, abs=0.05)

    c.new_project()
    c.import_files([str(three_scenes)])
    sid = next(iter(c.project.media))
    c.add_media_to_timeline([sid], None, 0.0)
    v = next(x for x in c.project.sequence.clips.values() if x.kind == "video")
    assert c.detect_scenes(v.id) == 2
    assert sorted(round(x.start, 1) for x in c.project.sequence.clips.values()) == [0.0, 1.0, 2.0]
