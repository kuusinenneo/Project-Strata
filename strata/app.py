"""Application entry point."""
from __future__ import annotations

import os
import sys


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv if argv is None else argv)
    from PySide6.QtCore import Qt
    from PySide6.QtWidgets import QApplication

    from . import APP_NAME
    from .ui.main_window import MainWindow
    from .ui.theme import apply_dark_theme

    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    app = QApplication.instance() or QApplication(argv)
    app.setApplicationName(APP_NAME)
    app.setOrganizationName(APP_NAME)
    apply_dark_theme(app)
    win = MainWindow()
    win.show()
    # Open a project passed on the command line: python -m strata myproject.strata.json
    for arg in argv[1:]:
        if arg.lower().endswith(".json") and os.path.isfile(arg):
            win.open_project(arg)
            break
    return app.exec()
