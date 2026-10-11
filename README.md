# Project Strata

Two desktop creative apps that share one design system:

| App | What it is | Tech | Folder |
| --- | --- | --- | --- |
| **Strata Video** | Multi-track video editor: Source/Program monitors, timeline, effects, transitions, text, MP4 export | Python 3.10+, PySide6, FFmpeg | [`video/`](video/README.md) |
| **Strata Photo** | Layered image editor: layers, masks, blend modes, brushes, selections, adjustments, filters, text | C# / .NET 10, Avalonia, SkiaSharp, Dock.Avalonia | [`photo/`](photo/README.md) |

```
Project-Strata/
  video/          Strata Video (Python)            -> video/README.md
  photo/          Strata Photo (C#)                -> photo/README.md
  shared/         design system used by both apps  -> shared/README.md
                  theme.json (colours, fonts, spacing) + icons/ (UI icons, app logos)
  PHOTO_SPEC.md   specification for the restructure and Strata Photo
  PROGRESS.md     progress tracking
```

Both apps build into self-contained Windows folders that run on PCs without Python or .NET
installed. The build output folders are not stored in git; build them with the commands below.

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

Copy or zip the whole `video\dist\StrataVideo\` folder to share it. Full documentation:
[video/README.md](video/README.md).

## Strata Photo

Requirements: Windows 10/11 and the .NET SDK 8 or newer (`winget install Microsoft.DotNet.SDK.10`).

```powershell
cd photo
dotnet run --project src\StrataPhoto.App    # run from source
dotnet test tests\StrataPhoto.Tests         # unit tests
.\build.bat                                 # build the exe -> photo\publish\StrataPhoto\StrataPhoto.exe
```

The build is a single self-contained `StrataPhoto.exe` plus a `shared\` folder (theme and icons);
copy or zip the whole `photo\publish\StrataPhoto\` folder to share it. Full documentation:
[photo/README.md](photo/README.md).

## Shared design system

`shared/theme.json` holds the colour palette, font stacks and spacing; `shared/icons/` holds the
icon set and both app logos. Strata Video reads its theme at startup and Strata Photo maps the same
tokens onto its Avalonia, Fluent and Dock styles; each build bundles a copy. Details:
[shared/README.md](shared/README.md).
