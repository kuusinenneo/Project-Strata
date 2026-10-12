import numpy as np
import pytest

from strata.media.audio import AudioStore, decode_audio
from strata.media.decoder import VideoDecoder, load_image, thumbnail
from strata.media.probe import ProbeError, probe


def test_probe_video(media_dir):
    m = probe(str(media_dir / "video.mp4"))
    assert m.kind == "video" and m.has_video and m.has_audio
    assert (m.width, m.height) == (320, 240)
    assert m.fps == pytest.approx(25.0)
    assert m.duration == pytest.approx(3.0, abs=0.1)


def test_probe_audio_image(media_dir):
    a = probe(str(media_dir / "tone.wav"))
    assert a.kind == "audio" and not a.has_video and a.duration == pytest.approx(2.0, abs=0.05)
    i = probe(str(media_dir / "blue.png"))
    assert i.kind == "image" and (i.width, i.height) == (200, 100)


def test_probe_missing():
    with pytest.raises(ProbeError):
        probe("does/not/exist.mp4")


def test_decoder_sequential_and_seek(media_dir):
    dec = VideoDecoder(str(media_dir / "video.mp4"))
    # testsrc draws a frame counter; consecutive frames must differ
    f0 = dec.rgb_at(0.0).copy()
    f1 = dec.rgb_at(0.04).copy()
    assert f0.shape == (240, 320, 3)
    assert not np.array_equal(f0, f1)
    late = dec.rgb_at(2.5).copy()
    back = dec.rgb_at(0.0)  # backwards seek
    assert np.array_equal(back, f0)
    assert dec.rgb_at(2.5).tolist() == late.tolist()
    small = dec.rgb_at(1.0, 160, 120)
    assert small.shape == (120, 160, 3)
    assert dec.frame_at(99.0) is not None  # past the end returns last frame
    dec.close()


def test_load_image_and_thumbnail(media_dir):
    img = load_image(str(media_dir / "blue.png"))
    assert img.shape == (100, 200, 4)
    assert img[50, 100, 2] > 200 and img[50, 100, 0] < 50
    th = thumbnail(str(media_dir / "red.mp4"), 0.5, 80)
    assert th.shape[1] == 80 and th[10, 10, 0] > 200


def test_decode_audio(media_dir):
    a = decode_audio(str(media_dir / "tone.wav"))
    assert a.samples.dtype == np.int16 and a.samples.shape[1] == 2
    assert a.duration == pytest.approx(2.0, abs=0.05)
    assert a.peaks.max() > 0.05


def test_audio_store_async(media_dir):
    import threading
    store = AudioStore()
    done = threading.Event()
    store.listeners.append(lambda mid: done.set())
    store.request("m1", str(media_dir / "video.mp4"))
    assert done.wait(10)
    assert store.get("m1") is not None and store.get("m1").duration > 2.5


def test_decoder_fast_mode_then_exact(media_dir, tmp_path):
    import subprocess
    from strata.media.ffmpeg import find_ffmpeg
    path = str(tmp_path / "longgop.mp4")
    subprocess.run([find_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi",
                    "-i", "testsrc=size=160x120:rate=25:duration=4", "-c:v", "libx264",
                    "-g", "100", "-pix_fmt", "yuv420p", path], check=True)
    dec = VideoDecoder(path)
    dec.frame_at(3.5)  # move far ahead
    fast = dec.frame_at(2.0, accurate=False)  # backwards: seek to keyframe, small budget
    assert fast is not None and dec._cur_t < 2.0
    exact = dec.frame_at(2.0)
    assert abs(dec._cur_t - 2.0) < 0.05 and exact is not None
    dec.close()


def test_proxy_store_builds_small_proxy(media_dir, tmp_path):
    from types import SimpleNamespace
    from strata.media.proxy import ProxyStore, needs_proxy
    src = str(media_dir / "video.mp4")
    store = ProxyStore(str(tmp_path / "px"), height=120)
    assert store.get(src) is None
    out = store.build(src)
    assert store.get(src) == out
    m = probe(out)
    assert m.height == 120 and m.has_video and not m.has_audio
    big = SimpleNamespace(kind="video", has_video=True, width=3840, height=2160)
    small = SimpleNamespace(kind="video", has_video=True, width=1920, height=1080)
    assert needs_proxy(big) and not needs_proxy(small)
    assert not needs_proxy(SimpleNamespace(kind="image", has_video=False, width=8000, height=6000))
