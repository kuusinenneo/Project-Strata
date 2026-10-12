# Strata PDF

**Strata PDF** is a PDF toolkit for Windows: view, merge, split, reorder, rotate and delete pages,
annotate, sign, fill forms, add page numbers and watermarks, redact and compress. It is written in
C# with .NET 10 and Avalonia, uses the Strata design system (`../shared`, via the shared
`Strata.Ui` library) so it looks like Strata Video and Strata Photo, renders pages with
**PDFium** (PDFiumCore bindings, Apache-2.0) and writes PDFs with **PDFsharp** (MIT).
It is a toolkit, not a full text editor: existing page text can't be edited.

## Running the Windows app (StrataPDF.exe)

After building, the app is in `pdf\publish\StrataPDF\`: `StrataPDF.exe` plus a `shared\` folder
(keep them together). It is self-contained, so the PC doesn't need .NET.
`StrataPDF.exe a.pdf b.pdf` opens `a.pdf` and merges `b.pdf` into it.

## Build, run and test

Requirements: Windows 10/11 and the .NET SDK 8 or newer (`winget install Microsoft.DotNet.SDK.10`).

```powershell
cd pdf
dotnet run --project src\StrataPdf.App      # run from source
dotnet test tests\StrataPdf.Tests           # unit tests for the Core library
.\build.bat                                 # tests + self-contained exe + self-test -> publish\StrataPDF\StrataPDF.exe
```

The self-test (`StrataPDF.exe --self-test`) builds a sample PDF and drives the real window: tools
through pointer events, rotate/reorder, stamps, saving, redaction, splitting, compression, merging
and undo. It writes `publish\selftest.png` and `publish\selftest.json`.

## Features

- **View:** continuous pages with zoom (Ctrl+wheel, fit width/page), page thumbnails, page navigation.
- **Pages:** merge (File → Insert/Merge PDF, or drop PDFs on the window), reorder by dragging
  thumbnails (or Ctrl+Up/Down), rotate, delete, extract selected pages, split into files every N pages.
- **Annotate:** highlight, freehand pen, text boxes, rectangles, ellipses, lines, arrows and sticky
  notes (saved as real PDF notes). Select tool: move annotations, double-click to edit text, Delete
  to remove.
- **Sign:** draw a signature with the mouse or a pen, or load an image, then click to place it.
- **Forms:** the Form tab lists the fillable fields (text, check boxes, choices); *Apply to Document*.
- **Page numbers and watermarks:** Document tab; previewed on the pages and written on save.
- **Redact:** redaction boxes really remove the content: on save, those pages are re-rendered as
  images with the boxes burned in, so no text or images remain under them.
- **Compress:** *Save Compressed Copy* — lossless stream compression, or strong (pages stored as
  JPEG at a chosen resolution/quality, for scans).
- **Extract images to Strata Photo:** File menu; saves the embedded images (JPEG as-is, RGB/grey
  as PNG) to `Pictures\Strata PDF\<document>` and opens them in Strata Photo.
- **Undo/redo** for every page and annotation change. Save, Save As.

Annotations stay editable until you close the document; saving writes them into the page content
(sticky notes stay notes), so a saved file reopens with them flattened.

## How it is built

```
pdf/
  src/StrataPdf.Core/     no UI: workspace model, annotations, PDFium rendering, PDFsharp saving,
                          forms, image extraction
  src/StrataPdf.App/      Avalonia UI (uses Core and shared/dotnet/Strata.Ui)
  tests/StrataPdf.Tests/  xUnit tests for Core
  build.bat               one-command build
```

Edits never touch the PDF directly: the workspace is a list of page references (source file, page
index, extra rotation, annotations) and the PDF is written only on save. Annotation coordinates are
stored on the page as displayed and mapped through each page's own `/Rotate` and crop box when
saving; tests check every rotation combination by rendering the saved file with PDFium.

## Not included

- OCR of scanned PDFs (would need Tesseract) — not implemented.
- Editing existing page text.
