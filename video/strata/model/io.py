"""Saving and loading projects as JSON."""
from __future__ import annotations

import json
import os

from .project import Project

PROJECT_EXTENSION = ".strata"
LEGACY_EXTENSIONS = (".strata.json", ".json")  # still opened


def save_project(project: Project, path: str) -> None:
    write_project_file(project, path)
    project.path = path


def write_project_file(project: Project, path: str) -> None:
    """Write the project as JSON without changing ``project.path`` (used by autosave too)."""
    data = project.to_dict()
    base = os.path.dirname(os.path.abspath(path))
    # Store paths relative to the project file too, so projects survive being moved
    # together with their media.
    for m in data["media"]:
        try:
            m["relative_path"] = os.path.relpath(m["path"], base)
        except ValueError:  # different drive on Windows
            m["relative_path"] = None
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    os.replace(tmp, path)


def load_project(path: str) -> Project:
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    base = os.path.dirname(os.path.abspath(path))
    for m in data.get("media", []):
        rel = m.pop("relative_path", None)
        if not os.path.exists(m.get("path", "")) and rel:
            candidate = os.path.normpath(os.path.join(base, rel))
            if os.path.exists(candidate):
                m["path"] = candidate
    project = Project.from_dict(data)
    project.path = path
    return project


def missing_media(project: Project) -> list[str]:
    return [m.name or m.path for m in project.media.values() if not os.path.exists(m.path)]
