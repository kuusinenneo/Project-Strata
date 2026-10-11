import sys

from strata import resources
from strata.media import ffmpeg as ffmod


def test_source_layout_finds_assets():
    assert not resources.is_frozen()
    assert resources.app_icon_path().is_file()
    assert resources.shared_path("theme.json").is_file()
    assert (resources.resource_path("licenses", "GPL-3.0.txt")).is_file()


def test_frozen_layout_prefers_bundled_ffmpeg(tmp_path, monkeypatch):
    (tmp_path / "ffmpeg").mkdir()
    fake = tmp_path / "ffmpeg" / ("ffmpeg.exe" if sys.platform == "win32" else "ffmpeg")
    fake.write_bytes(b"")
    monkeypatch.setattr(sys, "frozen", True, raising=False)
    monkeypatch.setattr(sys, "_MEIPASS", str(tmp_path), raising=False)
    monkeypatch.delenv("STRATA_FFMPEG", raising=False)
    ffmod.find_ffmpeg.cache_clear()
    try:
        assert resources.base_dir() == tmp_path
        assert ffmod.find_ffmpeg() == str(fake)
    finally:
        ffmod.find_ffmpeg.cache_clear()


def test_env_override_wins(tmp_path, monkeypatch):
    exe = tmp_path / "my-ffmpeg.exe"
    exe.write_bytes(b"")
    monkeypatch.setenv("STRATA_FFMPEG", str(exe))
    ffmod.find_ffmpeg.cache_clear()
    try:
        assert ffmod.find_ffmpeg() == str(exe)
    finally:
        ffmod.find_ffmpeg.cache_clear()
