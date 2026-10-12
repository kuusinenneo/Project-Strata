"""Application entry point."""
from __future__ import annotations

import os
import sys
import traceback


def _install_crash_handler() -> None:
    """A windowed exe has no console, so log uncaught errors and tell the user."""
    from .resources import user_data_dir

    from . import APP_NAME

    log_path = user_data_dir() / "strata-video-errors.log"

    def hook(exc_type, exc, tb):
        text = "".join(traceback.format_exception(exc_type, exc, tb))
        try:
            with open(log_path, "a", encoding="utf-8") as f:
                f.write(text + "\n")
        except OSError:
            pass
        if sys.stderr is not None:
            sys.stderr.write(text)
        try:
            from PySide6.QtWidgets import QApplication, QMessageBox
            if QApplication.instance() is not None:
                QMessageBox.critical(None, f"{APP_NAME} error",
                                     f"An unexpected error occurred:\n\n{exc}\n\nDetails were saved to:\n{log_path}")
        except Exception:
            pass

    sys.excepthook = hook


def _set_windows_app_id() -> None:
    """Group the taskbar button under Strata Video's own icon instead of python.exe."""
    if os.name == "nt":
        try:
            import ctypes
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("Strata.VideoEditor")
        except Exception:
            pass


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv if argv is None else argv)
    if "--self-test" in argv:
        from .selftest import run_self_test
        i = argv.index("--self-test")
        return run_self_test(argv[i + 1] if i + 1 < len(argv) else None)

    _install_crash_handler()
    _set_windows_app_id()
    from PySide6.QtCore import Qt
    from PySide6.QtGui import QIcon
    from PySide6.QtWidgets import QApplication

    from . import APP_NAME
    from .resources import app_icon_path
    from .ui.main_window import MainWindow
    from .ui.theme import apply_dark_theme

    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    app = QApplication.instance() or QApplication(argv)
    app.setApplicationName(APP_NAME)
    app.setOrganizationName("Strata")
    ico = app_icon_path()
    if ico.is_file():
        app.setWindowIcon(QIcon(str(ico)))
    apply_dark_theme(app)
    win = MainWindow()
    win.show()
    recovered = win.check_recovery()
    # Open a project passed on the command line: StrataVideo.exe myproject.strata
    for arg in argv[1:]:
        if not recovered and arg.lower().endswith((".strata", ".json")) and os.path.isfile(arg):
            win.open_project(arg)
            break
    return app.exec()
