# Project Strata

Three desktop creative apps and a launcher, sharing one design system:

| App | What it is | Tech | Folder |
| --- | --- | --- | --- |
| **Strata Video** | Multi-track video editor: Source/Program monitors, timeline, effects, transitions, keyframes, LUTs, text, MP4 export with platform presets | Python 3.10+, PySide6, FFmpeg | [`video/`](video/README.md) |
| **Strata Photo** | Layered image editor: layers, masks, blend modes, brushes, selections, adjustments, filters, text | C# / .NET 10, Avalonia, SkiaSharp, Dock.Avalonia | [`photo/`](photo/README.md) |
| **Strata PDF** | PDF toolkit: merge, split, reorder, annotate, sign, fill forms, page numbers, watermarks, redact, compress | C# / .NET 10, Avalonia, PDFium (PDFiumCore), PDFsharp | [`pdf/`](pdf/README.md) |
| **Strata** (launcher) | Opens any of the three apps, or a file in the right app | C# / .NET 10, Avalonia | `launcher/` |

```
Project-Strata/
  video/          Strata Video (Python)           -> video/README.md
  photo/          Strata Photo (C#)               -> photo/README.md
  pdf/            Strata PDF (C#)                 -> pdf/README.md
  launcher/       Strata launcher (C#)
  shared/         design system used by all apps  -> shared/README.md
                  theme.json, icons/, app logos, dotnet/Strata.Ui (shared C# theme library)
  build-all.bat   builds everything into dist\Strata Suite\
  PROGRESS.md     progress tracking
```

The apps work together: Strata Video can **send the current frame to Strata Photo**
(*Sequence → Send Frame to Strata Photo*), and Strata PDF can **extract a document's images into
Strata Photo** (*File → Extract Images to Strata Photo*).

## Requirements

- Windows 10/11 (the apps are developed and packaged for Windows; Strata Video also runs from
  source on macOS/Linux).
- **Strata Video:** Python 3.10+ (FFmpeg is bundled through `imageio-ffmpeg`).
- **Strata Photo, Strata PDF, launcher:** the .NET SDK 8 or newer — `winget install Microsoft.DotNet.SDK.10`.

The built apps are self-contained: the PCs you copy them to need neither Python nor .NET.

## Build everything as Windows .exe files

```powershell
.\build-all.bat            # builds, tests and self-tests all apps, then assembles the suite
.\build-all.bat notest     # faster: skip tests and self-tests
```

Result: **`dist\Strata Suite\`** — run `Strata.exe` (the launcher), or the apps directly:

```
dist\Strata Suite\
  Strata.exe                    launcher
  StrataVideo\StrataVideo.exe   Strata Video
  StrataPhoto\StrataPhoto.exe   Strata Photo
  StrataPDF\StrataPDF.exe       Strata PDF
```

Copy or zip the whole `Strata Suite` folder to move it; the apps find each other inside it. The
exes aren't code-signed, so Windows may show *"Windows protected your PC"* → **More info** → **Run anyway**.

## Each app on its own

### Strata Video

```powershell
cd video
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python main.py                 # run from source
python -m pytest               # tests (pip install -r requirements-dev.txt first)
.\build.bat                    # exe -> video\dist\StrataVideo\StrataVideo.exe
```

### Strata Photo

```powershell
cd photo
dotnet run --project src\StrataPhoto.App    # run from source
dotnet test tests\StrataPhoto.Tests         # tests
.\build.bat                                 # exe -> photo\publish\StrataPhoto\StrataPhoto.exe
```

### Strata PDF

```powershell
cd pdf
dotnet run --project src\StrataPdf.App      # run from source
dotnet test tests\StrataPdf.Tests           # tests
.\build.bat                                 # exe -> pdf\publish\StrataPDF\StrataPDF.exe
```

### Launcher

```powershell
cd launcher
dotnet run --project src\StrataLauncher     # finds the apps you have built in this repository
.\build.bat                                 # exe -> launcher\publish\Strata\Strata.exe
```

Every `build.bat` runs the app's unit tests, builds the exe and then runs the built exe's
`--self-test`, which drives the real app and saves a screenshot next to the build output.

## Shared design system

`shared/theme.json` holds the colour palette, font stacks and spacing; `shared/icons/` holds the
icon set and the app logos. Strata Video reads the theme at startup; the C# apps load it through
`shared/dotnet/Strata.Ui`, which maps the same tokens onto Avalonia, Fluent and Dock styles. Every
build bundles a copy. Details: [shared/README.md](shared/README.md).
