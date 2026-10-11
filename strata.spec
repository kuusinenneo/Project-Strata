# PyInstaller spec for Strata (Windows, onedir, windowed).
# Build with:  build.bat   (or: python -m PyInstaller strata.spec --noconfirm --clean)
import datetime
import shutil
import subprocess
from pathlib import Path

import imageio_ffmpeg

ROOT = Path(SPECPATH)
GEN = ROOT / "build" / "generated"
APP_NAME = "Strata"

# --------------------------------------------------------------------------- version
ns = {}
exec((ROOT / "strata" / "__init__.py").read_text(encoding="utf-8"), ns)
VERSION = ns["__version__"]

# --------------------------------------------------------------------------- FFmpeg
# Ship the same FFmpeg the venv uses (from imageio-ffmpeg) as <app>/_internal/ffmpeg/ffmpeg.exe
ffmpeg_src = Path(imageio_ffmpeg.get_ffmpeg_exe())
(GEN / "ffmpeg").mkdir(parents=True, exist_ok=True)
(GEN / "licenses").mkdir(parents=True, exist_ok=True)
ffmpeg_exe = GEN / "ffmpeg" / "ffmpeg.exe"
shutil.copy2(ffmpeg_src, ffmpeg_exe)


def _ffmpeg(*args):
    return subprocess.run([str(ffmpeg_exe), "-hide_banner", *args], capture_output=True,
                          text=True, errors="replace").stdout.strip()


ffmpeg_version = _ffmpeg("-version").splitlines()[0]
notice = f"""FFmpeg license notice
=====================

{APP_NAME} includes an unmodified copy of the FFmpeg command-line program
(ffmpeg/ffmpeg.exe, used to encode exported videos). FFmpeg is a separate
program; {APP_NAME} runs it as an external process.

Build:   {ffmpeg_version}
Origin:  Windows build by gyan.dev (https://www.gyan.dev/ffmpeg/builds/),
         distributed with the imageio-ffmpeg Python package.

FFmpeg is Copyright (c) 2000-2024 the FFmpeg developers and is licensed under
the terms shown below. Because this build enables GPL components
(--enable-gpl --enable-version3), the binary as a whole is licensed under the
GNU General Public License version 3 or later. The full license text is in
GPL-3.0.txt next to this file.

Source code: FFmpeg's source is available at https://ffmpeg.org/download.html
(git: https://git.ffmpeg.org/ffmpeg.git, release 7.1), and the build
configuration used for this binary is listed below. On request, the
corresponding source can also be obtained via https://www.gyan.dev/ffmpeg/builds/.

The decoding libraries used for preview are part of the PyAV package (av) and
are covered by their own licenses in that package's license files.

--- ffmpeg -L -------------------------------------------------------------
{_ffmpeg("-L")}

--- ffmpeg -buildconf -----------------------------------------------------
{_ffmpeg("-buildconf")}

Generated {datetime.date.today().isoformat()}
"""
(GEN / "licenses" / "FFMPEG-NOTICE.txt").write_text(notice, encoding="utf-8")

# --------------------------------------------------------------------------- version resource
(GEN / "version_info.txt").write_text(f"""
VSVersionInfo(
  ffi=FixedFileInfo(filevers=({VERSION.replace('.', ', ')}, 0), prodvers=({VERSION.replace('.', ', ')}, 0),
    mask=0x3f, flags=0x0, OS=0x40004, fileType=0x1, subtype=0x0, date=(0, 0)),
  kids=[
    StringFileInfo([StringTable('040904B0', [
      StringStruct('CompanyName', 'Strata'),
      StringStruct('FileDescription', 'Strata video editor'),
      StringStruct('FileVersion', '{VERSION}'),
      StringStruct('InternalName', 'Strata'),
      StringStruct('OriginalFilename', 'Strata.exe'),
      StringStruct('ProductName', 'Strata'),
      StringStruct('ProductVersion', '{VERSION}')])]),
    VarFileInfo([VarStruct('Translation', [1033, 1200])])
  ]
)
""", encoding="utf-8")

# --------------------------------------------------------------------------- analysis
datas = [
    (str(ROOT / "assets" / "strata.ico"), "assets"),
    (str(ROOT / "assets" / "strata.png"), "assets"),
    (str(ffmpeg_exe), "ffmpeg"),
    (str(GEN / "licenses" / "FFMPEG-NOTICE.txt"), "licenses"),
    (str(ROOT / "licenses" / "GPL-3.0.txt"), "licenses"),
]

a = Analysis(
    [str(ROOT / "main.py")],
    pathex=[str(ROOT)],
    binaries=[],
    datas=datas,
    hiddenimports=[],
    hookspath=[],
    runtime_hooks=[],
    # imageio_ffmpeg would ship a second copy of FFmpeg; the app uses the bundled one.
    excludes=["imageio_ffmpeg", "tkinter", "pytest", "PyQt5", "PyQt6",
              "PySide6.QtWebEngineCore", "PySide6.QtWebEngineWidgets", "PySide6.QtPdf",
              "PySide6.Qt3DCore", "PySide6.QtQuick", "PySide6.QtQml"],
    noarchive=False,
)
# Drop Qt pieces Strata never loads (software OpenGL fallback, PDF, QML/Quick).
_UNUSED = ("opengl32sw.dll", "qt6pdf", "qpdf.dll", "qt6quick", "qt6qml", "qt6qmlmodels",
           "qt6qmlmeta", "qt6qmlworkerscript", "qt6virtualkeyboard")
a.binaries = [b for b in a.binaries if not Path(b[0]).name.lower().startswith(_UNUSED)]
a.datas = [d for d in a.datas if "translations" not in Path(d[0]).parts]

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name=APP_NAME,
    icon=str(ROOT / "assets" / "strata.ico"),
    version=str(GEN / "version_info.txt"),
    console=False,  # windowed app: no console window
    debug=False,
    strip=False,
    upx=False,
)

coll = COLLECT(exe, a.binaries, a.datas, name=APP_NAME, strip=False, upx=False)

# Put the license files next to Strata.exe as well, where people will see them.
dist_app = Path(DISTPATH) / APP_NAME
shutil.copytree(GEN / "licenses", dist_app / "licenses", dirs_exist_ok=True)
shutil.copy2(ROOT / "licenses" / "GPL-3.0.txt", dist_app / "licenses" / "GPL-3.0.txt")
