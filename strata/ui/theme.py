"""Dark theme: Fusion style + palette + a small stylesheet."""
from __future__ import annotations

from PySide6.QtGui import QColor, QPalette
from PySide6.QtWidgets import QApplication

# Shared colours (also used by custom-painted widgets)
BG = QColor(30, 31, 34)
BG_DARK = QColor(22, 23, 25)
BG_PANEL = QColor(38, 39, 43)
BG_LIGHT = QColor(52, 54, 59)
BORDER = QColor(60, 62, 68)
TEXT = QColor(214, 216, 220)
TEXT_DIM = QColor(140, 144, 152)
ACCENT = QColor(64, 156, 255)
PLAYHEAD = QColor(255, 82, 82)
IN_OUT = QColor(64, 156, 255, 60)

CLIP_VIDEO = QColor(70, 110, 190)
CLIP_IMAGE = QColor(60, 140, 150)
CLIP_AUDIO = QColor(60, 150, 100)
CLIP_TEXT = QColor(150, 90, 180)

STYLESHEET = """
QMainWindow::separator { background: #16171a; width: 4px; height: 4px; }
QMainWindow::separator:hover { background: #409cff; }
QDockWidget { color: #d6d8dc; font-weight: 600; }
QDockWidget::title { background: #26272b; padding: 4px 8px; border-bottom: 1px solid #3c3e44; }
QTabBar::tab { background: #1e1f22; color: #8c9098; padding: 5px 12px; border: 1px solid #2a2b2f;
               border-bottom: none; }
QTabBar::tab:selected { background: #26272b; color: #e6e8ec; border-top: 2px solid #409cff; }
QToolTip { background: #26272b; color: #d6d8dc; border: 1px solid #3c3e44; }
QToolButton { border: 1px solid transparent; border-radius: 3px; padding: 2px; }
QToolButton:hover { background: #34363b; border-color: #44464c; }
QToolButton:checked { background: #2c4a6e; border-color: #409cff; }
QPushButton { background: #34363b; border: 1px solid #44464c; border-radius: 3px; padding: 4px 12px; }
QPushButton:hover { background: #3c3e44; }
QPushButton:pressed { background: #2c4a6e; }
QPushButton:default { border-color: #409cff; }
QLineEdit, QSpinBox, QDoubleSpinBox, QComboBox, QPlainTextEdit, QFontComboBox {
    background: #1a1b1e; border: 1px solid #3c3e44; border-radius: 3px; padding: 2px 4px;
    selection-background-color: #409cff; }
QLineEdit:focus, QSpinBox:focus, QDoubleSpinBox:focus, QPlainTextEdit:focus { border-color: #409cff; }
QGroupBox { border: 1px solid #3c3e44; border-radius: 4px; margin-top: 14px; padding-top: 6px; }
QGroupBox::title { subcontrol-origin: margin; left: 8px; padding: 0 4px; color: #9fc6ff; }
QTreeWidget, QListWidget { background: #1a1b1e; border: 1px solid #2a2b2f; alternate-background-color: #1e1f22; }
QTreeWidget::item:selected, QListWidget::item:selected { background: #2c4a6e; }
QHeaderView::section { background: #26272b; color: #b0b4ba; border: none; border-right: 1px solid #3c3e44;
                       padding: 3px 6px; }
QSlider::groove:horizontal { height: 4px; background: #3c3e44; border-radius: 2px; }
QSlider::handle:horizontal { background: #d6d8dc; width: 10px; margin: -5px 0; border-radius: 5px; }
QSlider::sub-page:horizontal { background: #409cff; border-radius: 2px; }
QProgressBar { border: 1px solid #3c3e44; border-radius: 3px; background: #1a1b1e; text-align: center; }
QProgressBar::chunk { background: #409cff; border-radius: 2px; }
QScrollBar:horizontal { height: 12px; background: #1a1b1e; }
QScrollBar:vertical { width: 12px; background: #1a1b1e; }
QScrollBar::handle { background: #44464c; border-radius: 4px; min-width: 24px; min-height: 24px; }
QScrollBar::handle:hover { background: #55585f; }
QScrollBar::add-line, QScrollBar::sub-line { width: 0; height: 0; }
QMenu { background: #26272b; border: 1px solid #3c3e44; }
QMenu::item:selected { background: #2c4a6e; }
QMenuBar { background: #1e1f22; }
QMenuBar::item:selected { background: #34363b; }
QStatusBar { background: #1a1b1e; color: #8c9098; }
"""


def apply_dark_theme(app: QApplication) -> None:
    app.setStyle("Fusion")
    pal = QPalette()
    pal.setColor(QPalette.Window, BG)
    pal.setColor(QPalette.WindowText, TEXT)
    pal.setColor(QPalette.Base, QColor(26, 27, 30))
    pal.setColor(QPalette.AlternateBase, BG_PANEL)
    pal.setColor(QPalette.ToolTipBase, BG_PANEL)
    pal.setColor(QPalette.ToolTipText, TEXT)
    pal.setColor(QPalette.Text, TEXT)
    pal.setColor(QPalette.Button, BG_LIGHT)
    pal.setColor(QPalette.ButtonText, TEXT)
    pal.setColor(QPalette.BrightText, QColor(255, 80, 80))
    pal.setColor(QPalette.Highlight, ACCENT)
    pal.setColor(QPalette.HighlightedText, QColor(255, 255, 255))
    pal.setColor(QPalette.Link, ACCENT)
    pal.setColor(QPalette.PlaceholderText, TEXT_DIM)
    for role in (QPalette.WindowText, QPalette.Text, QPalette.ButtonText):
        pal.setColor(QPalette.Disabled, role, QColor(100, 103, 110))
    app.setPalette(pal)
    app.setStyleSheet(STYLESHEET)
