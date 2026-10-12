"""Autosave / crash recovery logic (no Qt)."""
import os

from strata.model.autosave import AutosaveManager, pid_alive
from strata.model.io import load_project
from strata.model.project import Project


def test_pid_alive():
    assert pid_alive(os.getpid())
    assert not pid_alive(999_999_991)
    assert not pid_alive(0)


def test_autosave_roundtrip_and_recovery(tmp_path):
    proj = Project()
    proj.path = str(tmp_path / "mine.strata")
    running = AutosaveManager(str(tmp_path / "auto"))
    crashed = AutosaveManager(str(tmp_path / "auto"), pid=999_999_991)
    crashed.save(proj)
    assert proj.path == str(tmp_path / "mine.strata")  # autosave doesn't re-point the project
    running.save(proj)
    found = running.find_recoverable()
    assert [c.pid for c in found] == [999_999_991]  # own (live) session is not offered
    assert found[0].original_path == proj.path
    assert "mine.strata" in found[0].describe()
    assert load_project(found[0].project_file).sequence is not None
    AutosaveManager.discard(found[0])
    assert running.find_recoverable() == []
    running.clear()
    assert not os.path.exists(running.project_file)
