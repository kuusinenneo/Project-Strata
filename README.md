# Project Strata

Two desktop creative apps that share one design system:

| App | What it is | Tech | Folder |
| --- | --- | --- | --- |
| **Strata Video** | Multi-track video editor: Source/Program monitors, timeline, effects, transitions, text, MP4 export | Python 3.10+, PySide6, FFmpeg | [`video/`](video/README.md) |
| **Strata Photo** | Layered image editor: layers, masks, blend modes, brushes, selections, adjustments, filters, text | C# / .NET 10, Avalonia, SkiaSharp | `photo/` (in progress) |

```
Project-Strata/
  video/          Strata Video (Python)          -> video/README.md
  photo/          Strata Photo (C#)               (in progress)
  shared/         design system used by both apps -> shared/README.md
                  theme.json (colours, fonts, spacing) + icons/ (UI icons, app logos)
  PHOTO_SPEC.md   specification for the restructure and Strata Photo
  PROGRESS.md     progress tracking
```

## Strata Video

Requirements: Windows 10/11 (macOS/Linux work from source), Python 3.10+. FFmpeg is bundled.

```powershell
cd video
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python main.py                 # run from source
python -m pytest               # tests (pip install -r requirements-dev.txt first)
.\build.bat                    # build the exe -> video\dist\StrataVideo\StrataVideo.exe
```

The build is a self-contained folder (`video\dist\StrataVideo\`) that runs on PCs without Python
or FFmpeg; copy or zip the whole folder. Full documentation: [video/README.md](video/README.md).

## Strata Photo

In progress — see [PROGRESS.md](PROGRESS.md).

## Shared design system

`shared/theme.json` holds the colour palette, font stacks and spacing; `shared/icons/` holds the
icon set and both app logos. Strata Video reads its theme colours from it at startup (and its
exe bundles a copy). Details: [shared/README.md](shared/README.md).
