"""Shared fixtures: small synthetic media files generated with FFmpeg."""
import os
import subprocess

import pytest

from strata.media.ffmpeg import find_ffmpeg


def _run(args):
    subprocess.run([find_ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", *args],
                   check=True, stdin=subprocess.DEVNULL)


@pytest.fixture(scope="session")
def media_dir(tmp_path_factory):
    d = tmp_path_factory.mktemp("media")
    # 3 s, 320x240 @ 25 fps test pattern with a 440 Hz tone
    _run(["-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=3",
          "-f", "lavfi", "-i", "sine=frequency=440:duration=3:sample_rate=44100",
          "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "25", "-c:a", "aac", "-shortest",
          str(d / "video.mp4")])
    # 2 s, red, no audio
    _run(["-f", "lavfi", "-i", "color=c=red:size=160x120:rate=30:duration=2",
          "-c:v", "libx264", "-pix_fmt", "yuv420p", str(d / "red.mp4")])
    _run(["-f", "lavfi", "-i", "sine=frequency=220:duration=2:sample_rate=48000",
          str(d / "tone.wav")])
    _run(["-f", "lavfi", "-i", "color=c=blue:size=200x100", "-frames:v", "1",
          str(d / "blue.png")])
    return d


@pytest.fixture(scope="session")
def qapp():
    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    from PySide6.QtWidgets import QApplication
    app = QApplication.instance() or QApplication([])
    yield app
