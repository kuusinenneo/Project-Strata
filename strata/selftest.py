"""``Strata.exe --self-test [report.json]``: verify a build without a UI.

Checks that FFmpeg and the icon are found, generates a short test clip with
FFmpeg, decodes it with PyAV, and exports a small sequence (video, audio and a
text overlay) back to MP4. Writes a JSON report and returns 0 on success.
"""
from __future__ import annotations

import json
import os
import subprocess
import tempfile
import traceback


_keep: list = []  # holds the QApplication for the duration of the test


def run_self_test(report_path: str | None = None) -> int:
    report: dict = {"ok": False, "steps": {}}
    steps = report["steps"]
    try:
        os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
        from PySide6.QtWidgets import QApplication
        _keep.append(QApplication.instance() or QApplication([]))  # must exist for fonts/painting

        from .export.exporter import Exporter
        from .export.settings import ExportSettings
        from .media.ffmpeg import find_ffmpeg, ffmpeg_version, subprocess_flags
        from .media.probe import probe
        from .model import edits
        from .model.project import Project
        from .resources import asset_path, bundled_ffmpeg, is_frozen, resource_path

        report["frozen"] = is_frozen()
        ff = find_ffmpeg()
        steps["ffmpeg_path"] = ff
        steps["ffmpeg_is_bundled"] = bundled_ffmpeg() is not None and os.path.samefile(ff, bundled_ffmpeg())
        steps["ffmpeg_version"] = ffmpeg_version()
        steps["icon_found"] = asset_path("strata.ico").is_file()
        steps["ffmpeg_license_found"] = resource_path("licenses", "FFMPEG-NOTICE.txt").is_file() or not is_frozen()

        steps["audio_output"] = _check_audio_output()

        tmp = tempfile.mkdtemp(prefix="strata_selftest_")
        src = os.path.join(tmp, "src.mp4")
        subprocess.run([ff, "-hide_banner", "-loglevel", "error", "-y",
                        "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2",
                        "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", src],
                       check=True, stdin=subprocess.DEVNULL, capture_output=True,
                       creationflags=subprocess_flags())
        steps["generated_clip"] = True

        p = Project()
        m = p.add_media(probe(src))
        steps["probe"] = {"duration": round(m.duration, 2), "video": m.has_video, "audio": m.has_audio}
        edits.adopt_media_settings(p.sequence, m)
        edits.place_clips(p.sequence, edits.clips_from_media(
            m, 0.0, p.sequence.video_tracks[0].id, p.sequence.audio_tracks[0].id))
        t = edits.make_text_clip(p.sequence.video_tracks[1].id, 0.0, "Self test", "title", 2.0)
        p.sequence.clips[t.id] = t

        out = os.path.join(tmp, "export.mp4")
        Exporter(p, ExportSettings(output_path=out, speed="Fast")).run()
        exported = probe(out)
        steps["export"] = {"bytes": os.path.getsize(out), "duration": round(exported.duration, 2),
                           "video": exported.has_video, "audio": exported.has_audio}
        report["ok"] = bool(exported.has_video and exported.has_audio and steps["icon_found"])
    except Exception:
        report["error"] = traceback.format_exc()
    if report_path:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    else:
        print(json.dumps(report, indent=2))
    return 0 if report["ok"] else 1


def _check_audio_output() -> str:
    """Open the default audio device through Qt Multimedia and play a little silence."""
    from PySide6.QtCore import QCoreApplication, QThread
    from PySide6.QtMultimedia import QAudioFormat, QAudioSink, QMediaDevices
    dev = QMediaDevices.defaultAudioOutput()
    if dev.isNull():
        return "no audio device (ok on machines without sound)"
    fmt = QAudioFormat()
    fmt.setSampleRate(48000)
    fmt.setChannelCount(2)
    fmt.setSampleFormat(QAudioFormat.Int16)
    sink = QAudioSink(dev, fmt)
    io = sink.start()
    if io is None:
        raise RuntimeError("audio sink failed to start")
    io.write(bytes(48000 // 5 * 4))
    for _ in range(20):
        QCoreApplication.processEvents()
        QThread.msleep(10)
    err = sink.error()
    sink.stop()
    if getattr(err, "name", str(err)) != "NoError":
        raise RuntimeError(f"audio sink error: {err}")
    return f"ok ({dev.description()})"
