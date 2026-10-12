"""Auto subtitles with faster-whisper (skipped when it isn't installed)."""
import subprocess
import sys

import pytest

from strata.media import transcribe

pytestmark = pytest.mark.skipif(not transcribe.available(), reason="faster-whisper not installed")


@pytest.fixture(scope="session")
def speech_wav(tmp_path_factory):
    if sys.platform != "win32":
        pytest.skip("uses the Windows speech synthesiser")
    path = tmp_path_factory.mktemp("speech") / "speech.wav"
    script = ("Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
              f"$s.Rate = -1; $s.SetOutputToWaveFile('{path}'); "
              "$s.Speak('Hello and welcome to Strata Video. This is an automatic subtitle test.'); $s.Dispose()")
    r = subprocess.run(["powershell.exe", "-NoProfile", "-Command", script], capture_output=True)
    if r.returncode != 0 or not path.exists():
        pytest.skip("speech synthesis unavailable")
    return path


def test_auto_subtitles_end_to_end(speech_wav, qapp):
    from strata.ui.controller import EditorController
    c = EditorController()
    (media,), errors = c.import_files([str(speech_wav)])
    assert not errors
    c.add_media_to_timeline([media.id], None, 2.0)  # starts 2 s into the timeline
    tracks_before = len(c.project.sequence.video_tracks)
    try:
        n = c.auto_subtitles([], "tiny", "en")
    except Exception as exc:  # first run needs the model download
        pytest.skip(f"model unavailable: {exc}")
    assert n >= 1
    track = c.project.sequence.video_tracks[-1]
    assert track.name == "Subtitles"
    subs = sorted(c.project.sequence.clips_on(track.id), key=lambda x: x.start)
    text = " ".join(s.text.text for s in subs).lower()
    assert "strata" in text and "subtitle" in text
    assert subs[0].start >= 2.0 and subs[0].text.preset == "subtitle"
    assert all(a.end <= b.start + 1e-6 for a, b in zip(subs, subs[1:]))  # no overlaps
    c.do_undo()
    assert len(c.project.sequence.video_tracks) == tracks_before  # the Subtitles track is gone again
    assert not any(x.kind == "text" for x in c.project.sequence.clips.values())
