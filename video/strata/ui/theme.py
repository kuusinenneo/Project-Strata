"""Dark theme: Fusion style + palette + a small stylesheet.

All colours, fonts and sizes come from the shared design system in
``shared/theme.json`` (also used by Strata Photo).
"""
from __future__ import annotations

import json

from PySide6.QtGui import QColor, QFont, QPalette
from PySide6.QtWidgets import QApplication

from ..resources import shared_path


def _load_tokens() -> dict:
    path = shared_path("theme.json")
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise RuntimeError(f"Could not load the shared theme from {path}: {exc}") from exc


TOKENS = _load_tokens()
C = TOKENS["colors"]           # hex strings, e.g. C["accent"] == "#409cff"
FONTS = TOKENS["fonts"]
SPACING = TOKENS["spacing"]


def color(name: str, alpha: int | None = None) -> QColor:
    c = QColor(C[name])
    if alpha is not None:
        c.setAlpha(alpha)
    return c


# Shared colours (also used by custom-painted widgets)
BG = color("bg")
BG_DARK = color("bgDark")
BG_PANEL = color("bgPanel")
BG_LIGHT = color("bgLight")
BORDER = color("border")
TEXT = color("text")
TEXT_DIM = color("textDim")
ACCENT = color("accent")
PLAYHEAD = color("danger")
IN_OUT = color("accent", 60)

CLIP_VIDEO = color("clipVideo")
CLIP_IMAGE = color("clipImage")
CLIP_AUDIO = color("clipAudio")
CLIP_TEXT = color("clipText")

_R = SPACING["radius"]
STYLESHEET = f"""
QMainWindow::separator {{ background: {C['bgDark']}; width: {SPACING['sm']}px; height: {SPACING['sm']}px; }}
QMainWindow::separator:hover {{ background: {C['accent']}; }}
QDockWidget {{ color: {C['text']}; font-weight: {FONTS['weightHeader']}; }}
QDockWidget::title {{ background: {C['bgPanel']}; padding: 4px 8px; border-bottom: 1px solid {C['border']}; }}
QTabBar::tab {{ background: {C['bg']}; color: {C['textDim']}; padding: 5px 12px; border: 1px solid {C['borderSubtle']};
               border-bottom: none; }}
QTabBar::tab:selected {{ background: {C['bgPanel']}; color: {C['textBright']}; border-top: 2px solid {C['accent']}; }}
QToolTip {{ background: {C['bgPanel']}; color: {C['text']}; border: 1px solid {C['border']}; }}
QToolButton {{ border: 1px solid transparent; border-radius: {_R}px; padding: 2px; }}
QToolButton:hover {{ background: {C['bgLight']}; border-color: {C['borderLight']}; }}
QToolButton:checked {{ background: {C['selection']}; border-color: {C['accent']}; }}
QPushButton {{ background: {C['bgLight']}; border: 1px solid {C['borderLight']}; border-radius: {_R}px; padding: 4px 12px; }}
QPushButton:hover {{ background: {C['bgHover']}; }}
QPushButton:pressed {{ background: {C['selection']}; }}
QPushButton:default {{ border-color: {C['accent']}; }}
QLineEdit, QSpinBox, QDoubleSpinBox, QComboBox, QPlainTextEdit, QFontComboBox {{
    background: {C['bgInput']}; border: 1px solid {C['border']}; border-radius: {_R}px; padding: 2px 4px;
    selection-background-color: {C['accent']}; }}
QLineEdit:focus, QSpinBox:focus, QDoubleSpinBox:focus, QPlainTextEdit:focus {{ border-color: {C['accent']}; }}
QGroupBox {{ border: 1px solid {C['border']}; border-radius: 4px; margin-top: 14px; padding-top: 6px; }}
QGroupBox::title {{ subcontrol-origin: margin; left: 8px; padding: 0 4px; color: {C['accentSoft']}; }}
QTreeWidget, QListWidget {{ background: {C['bgInput']}; border: 1px solid {C['borderSubtle']};
                           alternate-background-color: {C['bg']}; }}
QTreeWidget::item:selected, QListWidget::item:selected {{ background: {C['selection']}; }}
QHeaderView::section {{ background: {C['bgPanel']}; color: {C['textHeader']}; border: none;
                       border-right: 1px solid {C['border']}; padding: 3px 6px; }}
QSlider::groove:horizontal {{ height: 4px; background: {C['border']}; border-radius: 2px; }}
QSlider::handle:horizontal {{ background: {C['text']}; width: 10px; margin: -5px 0; border-radius: 5px; }}
QSlider::sub-page:horizontal {{ background: {C['accent']}; border-radius: 2px; }}
QProgressBar {{ border: 1px solid {C['border']}; border-radius: {_R}px; background: {C['bgInput']}; text-align: center; }}
QProgressBar::chunk {{ background: {C['accent']}; border-radius: 2px; }}
QScrollBar:horizontal {{ height: 12px; background: {C['bgInput']}; }}
QScrollBar:vertical {{ width: 12px; background: {C['bgInput']}; }}
QScrollBar::handle {{ background: {C['borderLight']}; border-radius: 4px; min-width: 24px; min-height: 24px; }}
QScrollBar::handle:hover {{ background: {C['scrollHandleHover']}; }}
QScrollBar::add-line, QScrollBar::sub-line {{ width: 0; height: 0; }}
QMenu {{ background: {C['bgPanel']}; border: 1px solid {C['border']}; }}
QMenu::item:selected {{ background: {C['selection']}; }}
QMenuBar {{ background: {C['bg']}; }}
QMenuBar::item:selected {{ background: {C['bgLight']}; }}
QStatusBar {{ background: {C['bgInput']}; color: {C['textDim']}; }}
"""


def ui_font() -> QFont:
    """The first available UI font family from the shared font stack."""
    font = QFont()
    font.setFamilies([f for f in FONTS["ui"] if f != "sans-serif"])
    font.setPointSizeF(FONTS["sizeNormal"] * 0.75)  # theme sizes are px; Qt points are 1/72 in at 96 dpi
    return font


def apply_dark_theme(app: QApplication) -> None:
    app.setStyle("Fusion")
    app.setFont(ui_font())
    pal = QPalette()
    pal.setColor(QPalette.Window, BG)
    pal.setColor(QPalette.WindowText, TEXT)
    pal.setColor(QPalette.Base, color("bgInput"))
    pal.setColor(QPalette.AlternateBase, BG_PANEL)
    pal.setColor(QPalette.ToolTipBase, BG_PANEL)
    pal.setColor(QPalette.ToolTipText, TEXT)
    pal.setColor(QPalette.Text, TEXT)
    pal.setColor(QPalette.Button, BG_LIGHT)
    pal.setColor(QPalette.ButtonText, TEXT)
    pal.setColor(QPalette.BrightText, color("danger"))
    pal.setColor(QPalette.Highlight, ACCENT)
    pal.setColor(QPalette.HighlightedText, QColor(255, 255, 255))
    pal.setColor(QPalette.Link, ACCENT)
    pal.setColor(QPalette.PlaceholderText, TEXT_DIM)
    for role in (QPalette.WindowText, QPalette.Text, QPalette.ButtonText):
        pal.setColor(QPalette.Disabled, role, color("textDisabled"))
    app.setPalette(pal)
    app.setStyleSheet(STYLESHEET)
