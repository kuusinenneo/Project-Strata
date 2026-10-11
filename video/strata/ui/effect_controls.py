"""Effect Controls: edit the selected clip's motion, effects, transitions, audio and text."""
from __future__ import annotations

from typing import Callable, Optional

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QColor, QFont
from PySide6.QtWidgets import (QCheckBox, QColorDialog, QComboBox, QDoubleSpinBox, QFontComboBox,
                               QFormLayout, QGroupBox, QHBoxLayout, QLabel, QPlainTextEdit,
                               QPushButton, QScrollArea, QSlider, QVBoxLayout, QWidget)

from ..model.project import TEXT_PRESETS, Clip, Effects, Transform
from .playback import timecode


class SliderSpin(QWidget):
    """Slider + spin box pair editing one float."""
    valueChanged = Signal(float)

    def __init__(self, lo: float, hi: float, step: float = 1.0, decimals: int = 0,
                 suffix: str = "", slider: bool = True, parent=None):
        super().__init__(parent)
        self._scale = 10 ** decimals
        self.spin = QDoubleSpinBox()
        self.spin.setRange(lo, hi)
        self.spin.setDecimals(decimals)
        self.spin.setSingleStep(step)
        self.spin.setSuffix(suffix)
        self.spin.setKeyboardTracking(False)
        self.spin.setFixedWidth(84)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        self.slider = None
        if slider:
            self.slider = QSlider(Qt.Horizontal)
            self.slider.setRange(int(lo * self._scale), int(hi * self._scale))
            self.slider.valueChanged.connect(self._from_slider)
            lay.addWidget(self.slider, 1)
        lay.addWidget(self.spin)
        self.spin.valueChanged.connect(self._from_spin)

    def _from_slider(self, v: int) -> None:
        val = v / self._scale
        self.spin.blockSignals(True)
        self.spin.setValue(val)
        self.spin.blockSignals(False)
        self.valueChanged.emit(val)

    def _from_spin(self, v: float) -> None:
        if self.slider is not None:
            self.slider.blockSignals(True)
            self.slider.setValue(int(round(v * self._scale)))
            self.slider.blockSignals(False)
        self.valueChanged.emit(v)

    def setValue(self, v: float) -> None:
        self.spin.blockSignals(True)
        self.spin.setValue(v)
        self.spin.blockSignals(False)
        if self.slider is not None:
            self.slider.blockSignals(True)
            self.slider.setValue(int(round(v * self._scale)))
            self.slider.blockSignals(False)

    def value(self) -> float:
        return self.spin.value()


class ColorButton(QPushButton):
    colorChanged = Signal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self._color = "#ffffff"
        self.setFixedWidth(64)
        self.clicked.connect(self._pick)
        self._paint()

    def _paint(self) -> None:
        self.setStyleSheet(f"background:{self._color}; border:1px solid #55585f; min-height:18px;")

    def setColor(self, c: str) -> None:
        self._color = c
        self._paint()

    def _pick(self) -> None:
        c = QColorDialog.getColor(QColor(self._color), self, "Choose colour")
        if c.isValid():
            self.setColor(c.name())
            self.colorChanged.emit(c.name())


class EffectControls(QWidget):
    def __init__(self, controller, parent=None):
        super().__init__(parent)
        self.controller = controller
        self.clip_id: Optional[str] = None
        self._loading = False

        self.header = QLabel("No clip selected")
        self.header.setWordWrap(True)
        self.header.setStyleSheet("font-weight:600; padding:4px;")
        self.info = QLabel("")
        self.info.setStyleSheet("color:#8c9098; padding:0 4px 4px 4px;")

        body = QWidget()
        self.body_lay = QVBoxLayout(body)
        self.body_lay.setContentsMargins(4, 0, 4, 4)

        # -- Motion
        self.motion = QGroupBox("Motion")
        f = QFormLayout(self.motion)
        self.scale = self._num(f, "Scale", 1, 1000, 1, 0, " %", lambda c, v: setattr(c.transform, "scale", v))
        self.pos_x = self._num(f, "Position X", -200, 200, 0.5, 1, " %", lambda c, v: setattr(c.transform, "pos_x", v))
        self.pos_y = self._num(f, "Position Y", -200, 200, 0.5, 1, " %", lambda c, v: setattr(c.transform, "pos_y", v))
        self.animate = self._check(f, "Gradual zoom / pan over the clip", self._set_animate)
        self.scale_end = self._num(f, "End scale", 1, 1000, 1, 0, " %", lambda c, v: setattr(c.transform, "scale_end", v))
        self.pos_x_end = self._num(f, "End position X", -200, 200, 0.5, 1, " %", lambda c, v: setattr(c.transform, "pos_x_end", v))
        self.pos_y_end = self._num(f, "End position Y", -200, 200, 0.5, 1, " %", lambda c, v: setattr(c.transform, "pos_y_end", v))
        f.addRow(self._reset_button("Reset motion", lambda c: setattr(c, "transform", Transform())))

        # -- Opacity / speed
        self.basic = QGroupBox("Opacity && Time")
        f = QFormLayout(self.basic)
        self.opacity = self._num(f, "Opacity", 0, 100, 1, 0, " %", lambda c, v: setattr(c.effects, "opacity", v))
        self.speed = SliderSpin(0.1, 16, 0.1, 2, " ×", slider=False)
        self.speed.valueChanged.connect(self._set_speed)
        f.addRow("Speed", self.speed)

        # -- Colour
        self.color = QGroupBox("Color && Blur")
        f = QFormLayout(self.color)
        self.brightness = self._num(f, "Brightness", -100, 100, 1, 0, "", lambda c, v: setattr(c.effects, "brightness", v))
        self.contrast = self._num(f, "Contrast", -100, 100, 1, 0, "", lambda c, v: setattr(c.effects, "contrast", v))
        self.saturation = self._num(f, "Saturation", 0, 200, 1, 0, " %", lambda c, v: setattr(c.effects, "saturation", v))
        self.bw = self._check(f, "Black && white", lambda c, v: setattr(c.effects, "black_white", v))
        self.sepia = self._check(f, "Sepia", lambda c, v: setattr(c.effects, "sepia", v))
        self.blur = self._num(f, "Blur", 0, 100, 0.5, 1, " px", lambda c, v: setattr(c.effects, "blur", v))
        f.addRow(self._reset_button("Reset effects", self._reset_effects))

        # -- Audio
        self.audio = QGroupBox("Audio")
        f = QFormLayout(self.audio)
        self.volume = self._num(f, "Volume", 0, 200, 1, 0, " %", self._set_volume, linked=True)

        # -- Transitions
        self.trans = QGroupBox("Fades && Transitions")
        f = QFormLayout(self.trans)
        self.fade_in = self._num(f, "Fade in", 0, 30, 0.1, 2, " s", lambda c, v: setattr(c, "fade_in", min(v, c.duration)), linked=True)
        self.fade_out = self._num(f, "Fade out", 0, 30, 0.1, 2, " s", lambda c, v: setattr(c, "fade_out", min(v, c.duration)), linked=True)
        self.dissolve = self._num(f, "Cross dissolve in", 0, 10, 0.1, 2, " s", lambda c, v: setattr(c, "cross_dissolve", min(v, c.duration)), linked=True)

        # -- Text
        self.text_box = QGroupBox("Text")
        f = QFormLayout(self.text_box)
        self.text_edit = QPlainTextEdit()
        self.text_edit.setFixedHeight(64)
        self.text_edit.textChanged.connect(self._text_changed)
        f.addRow("Text", self.text_edit)
        self.preset = QComboBox()
        for k, v in TEXT_PRESETS.items():
            self.preset.addItem(v, k)
        self.preset.currentIndexChanged.connect(
            lambda _: self._apply("text_preset", lambda c, v: setattr(c.text, "preset", v), self.preset.currentData()))
        f.addRow("Layout", self.preset)
        self.font = QFontComboBox()
        self.font.currentFontChanged.connect(
            lambda font: self._apply("text_font", lambda c, v: setattr(c.text, "font_family", v), font.family()))
        f.addRow("Font", self.font)
        self.font_size = self._num(f, "Size", 6, 400, 1, 0, " px", lambda c, v: setattr(c.text, "font_size", v))
        style_row = QHBoxLayout()
        self.bold = QCheckBox("Bold")
        self.italic = QCheckBox("Italic")
        self.bold.toggled.connect(lambda v: self._apply("text_bold", lambda c, x: setattr(c.text, "bold", x), v))
        self.italic.toggled.connect(lambda v: self._apply("text_italic", lambda c, x: setattr(c.text, "italic", x), v))
        style_row.addWidget(self.bold)
        style_row.addWidget(self.italic)
        style_row.addStretch(1)
        f.addRow("Style", style_row)
        self.text_color = ColorButton()
        self.text_color.colorChanged.connect(lambda v: self._apply("text_color", lambda c, x: setattr(c.text, "color", x), v))
        f.addRow("Color", self.text_color)
        self.bg_box = self._check(f, "Background box", lambda c, v: setattr(c.text, "bg_box", v))
        self.bg_color = ColorButton()
        self.bg_color.colorChanged.connect(lambda v: self._apply("text_bg", lambda c, x: setattr(c.text, "bg_color", x), v))
        f.addRow("Box color", self.bg_color)
        self.bg_opacity = self._num(f, "Box opacity", 0, 100, 1, 0, " %", lambda c, v: setattr(c.text, "bg_opacity", v))

        for g in (self.text_box, self.motion, self.basic, self.color, self.audio, self.trans):
            self.body_lay.addWidget(g)
        self.body_lay.addStretch(1)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(body)
        scroll.setFrameShape(QScrollArea.NoFrame)
        lay = QVBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(0)
        lay.addWidget(self.header)
        lay.addWidget(self.info)
        lay.addWidget(scroll, 1)

        controller.selection_changed.connect(self.load)
        controller.project_changed.connect(self.load)
        self.load()

    # ------------------------------------------------------------------ builders
    def _num(self, form: QFormLayout, label: str, lo, hi, step, dec, suffix,
             setter: Callable[[Clip, float], None], linked: bool = False) -> SliderSpin:
        w = SliderSpin(lo, hi, step, dec, suffix)
        key = label.lower().replace(" ", "_")
        w.valueChanged.connect(lambda v: self._apply(key, setter, v, linked))
        form.addRow(label, w)
        return w

    def _check(self, form: QFormLayout, label: str, setter) -> QCheckBox:
        cb = QCheckBox(label.replace("&&", "&"))
        key = label.lower().replace(" ", "_")
        cb.toggled.connect(lambda v: self._apply(key, setter, v))
        form.addRow(cb)
        return cb

    def _reset_button(self, label: str, fn) -> QPushButton:
        b = QPushButton(label)
        b.clicked.connect(lambda: self._apply(None, lambda c, _: fn(c), None))
        return b

    # ------------------------------------------------------------------ apply
    def _apply(self, key: Optional[str], setter, value, linked: bool = False) -> None:
        if self._loading or self.clip_id is None:
            return
        merge = f"{self.clip_id}:{key}" if key else None
        self.controller.update_clip(self.clip_id, lambda c: setter(c, value),
                                    "Change " + (key or "clip").replace("_", " "), merge, linked)

    def _set_animate(self, c: Clip, on: bool) -> None:
        tr = c.transform
        if on and not tr.animate:
            # start from the current framing so enabling it doesn't jump
            tr.scale_end = tr.scale * 1.25 if abs(tr.scale_end - tr.scale) < 1e-6 else tr.scale_end
            tr.pos_x_end, tr.pos_y_end = tr.pos_x, tr.pos_y
        tr.animate = on

    def _set_volume(self, c: Clip, v: float) -> None:
        if c.kind == "audio":
            c.volume = v

    def _set_speed(self, v: float) -> None:
        if not self._loading and self.clip_id:
            self.controller.set_speed(self.clip_id, v)

    @staticmethod
    def _reset_effects(c: Clip) -> None:
        c.effects = Effects()

    def _text_changed(self) -> None:
        if self._loading or self.clip_id is None:
            return
        txt = self.text_edit.toPlainText()
        self._apply("text", lambda c, v: setattr(c.text, "text", v), txt)

    # ------------------------------------------------------------------ load
    def load(self) -> None:
        clip = self.controller.primary_selected()
        self.clip_id = clip.id if clip else None
        self._loading = True
        try:
            self._load(clip)
        finally:
            self._loading = False

    def _load(self, clip: Optional[Clip]) -> None:
        groups = (self.text_box, self.motion, self.basic, self.color, self.audio, self.trans)
        if clip is None:
            self.header.setText("No clip selected")
            self.info.setText("Select a clip in the timeline to edit its properties.")
            for g in groups:
                g.setVisible(False)
            return
        seq = self.controller.project.sequence
        fps = seq.fps
        media = self.controller.project.media_for(clip)
        name = clip.text.text.splitlines()[0][:40] if clip.kind == "text" and clip.text and clip.text.text else (clip.name or "Clip")
        self.header.setText(f"{seq.track_name(clip.track_id)} · {name}")
        self.info.setText(f"Start {timecode(clip.start, fps)}   Duration {timecode(clip.duration, fps)}"
                          + ("   (locked track)" if self.controller.is_locked(clip.track_id) else ""))
        visual = clip.kind in ("video", "text")
        audio_targets = [c for c in [clip] + seq.linked(clip) if c.kind == "audio"]
        self.text_box.setVisible(clip.kind == "text")
        self.motion.setVisible(visual)
        self.basic.setVisible(True)
        self.opacity.setEnabled(visual)
        self.speed.setEnabled(clip.kind != "text" and not (media and media.is_still))
        self.color.setVisible(visual and clip.kind != "text")
        self.audio.setVisible(bool(audio_targets))
        self.trans.setVisible(True)

        tr, fx = clip.transform, clip.effects
        for w, v in ((self.scale, tr.scale), (self.pos_x, tr.pos_x), (self.pos_y, tr.pos_y),
                     (self.scale_end, tr.scale_end), (self.pos_x_end, tr.pos_x_end),
                     (self.pos_y_end, tr.pos_y_end), (self.opacity, fx.opacity),
                     (self.speed, clip.speed), (self.brightness, fx.brightness),
                     (self.contrast, fx.contrast), (self.saturation, fx.saturation),
                     (self.blur, fx.blur), (self.fade_in, clip.fade_in),
                     (self.fade_out, clip.fade_out), (self.dissolve, clip.cross_dissolve)):
            w.setValue(v)
        if audio_targets:
            self.volume.setValue(audio_targets[0].volume)
        for cb, v in ((self.animate, tr.animate), (self.bw, fx.black_white), (self.sepia, fx.sepia)):
            cb.blockSignals(True)
            cb.setChecked(v)
            cb.blockSignals(False)
        for w in (self.scale_end, self.pos_x_end, self.pos_y_end):
            w.setEnabled(tr.animate)

        if clip.kind == "text" and clip.text is not None:
            t = clip.text
            if self.text_edit.toPlainText() != t.text:
                self.text_edit.setPlainText(t.text)
            idx = self.preset.findData(t.preset)
            self.preset.setCurrentIndex(max(0, idx))
            self.font.blockSignals(True)
            self.font.setCurrentFont(QFont(t.font_family))
            self.font.blockSignals(False)
            self.font_size.setValue(t.font_size)
            self.bold.setChecked(t.bold)
            self.italic.setChecked(t.italic)
            self.text_color.setColor(t.color)
            self.bg_box.setChecked(t.bg_box)
            self.bg_color.setColor(t.bg_color)
            self.bg_opacity.setValue(t.bg_opacity)
