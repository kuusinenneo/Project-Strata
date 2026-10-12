# Strata shared design system

Both Strata apps take their look from this folder, so they stay visually consistent.

| File | What it holds | Used by |
| --- | --- | --- |
| `theme.json` | Colour palette, font stacks and sizes, spacing/radius tokens, icon conventions | Strata Video (`video/strata/ui/theme.py`), Strata Photo and Strata PDF (via `dotnet/Strata.Ui`) |
| `icons/*.svg` | 24×24 UI and tool icons, drawn with `currentColor` | Strata Photo (tools, panels, actions) |
| `icons/app/strata-video.*` | Strata Video app logo (`.svg`, `.ico`, 256 px `.png`) | Strata Video exe and window icon |
| `icons/app/strata-photo.*` | Strata Photo app logo (`.svg`, `.ico`, 256 px `.png`) | Strata Photo exe and window icon |
| `dotnet/Strata.Ui/` | .NET library for the C# apps: loads `theme.json` into Avalonia/Fluent/Dock resources, the SVG icon loader (`IconView`), shared styles and `ValueSlider` | Strata Photo, Strata PDF |
| `tools/icon_sources.py` | Path data for every icon (the source of truth) | `tools/make_icons.py` |
| `tools/make_icons.py` | Writes all SVGs and renders the `.ico`/`.png` app icons | run by hand |

## Colours

Colours are hex strings under `colors`. The main ones: `bg` (window background), `bgPanel`
(panel headers, menus), `bgInput` (text fields, lists), `border`, `text` / `textDim`, `accent`
(the Strata blue, used for focus, selection and active tools) and `selection`.

## Icons

Every UI icon is a plain SVG made only of `<path>` elements so both apps can render them
without a full SVG engine: paths are stroked with `currentColor` at width 1.6 by default; solid
parts carry `fill="currentColor" stroke="none"`, and a path may set its own `stroke-width` or
`opacity`. To add or change an icon, edit `tools/icon_sources.py` and regenerate:

```powershell
video\.venv\Scripts\python.exe shared\tools\make_icons.py            # needs PySide6
video\.venv\Scripts\python.exe shared\tools\make_icons.py --preview sheet.png
```
