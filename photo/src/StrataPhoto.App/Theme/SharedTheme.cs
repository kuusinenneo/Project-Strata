using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;

namespace StrataPhoto.App.Theme;

/// <summary>
/// The Strata design system from <c>shared/theme.json</c> (copied next to the exe at build time).
/// Colours become resources named <c>Strata.&lt;name&gt;</c> (Color) and <c>Strata.&lt;name&gt;Brush</c>, and
/// are also mapped onto the Fluent and Dock theme resources so stock controls match Strata Video.
/// </summary>
public static class SharedTheme
{
    private static Dictionary<string, Color> _colors = new();

    public static IReadOnlyDictionary<string, Color> Colors => _colors;
    public static IReadOnlyList<string> UiFonts { get; private set; } = new[] { "Segoe UI" };
    public static double FontSizeSmall { get; private set; } = 11;
    public static double FontSizeNormal { get; private set; } = 12;
    public static double Radius { get; private set; } = 3;
    public static string SharedFolder { get; private set; } = "";

    public static Color Get(string name) => _colors.TryGetValue(name, out var c) ? c : Avalonia.Media.Colors.Magenta;
    public static SKColor GetSk(string name)
    {
        var c = Get(name);
        return new SKColor(c.R, c.G, c.B, c.A);
    }

    /// <summary>Finds <c>shared/</c> next to the exe, or (when running from source) up the folder tree.</summary>
    public static string FindSharedFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var d = dir; d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "shared");
            if (File.Exists(Path.Combine(candidate, "theme.json"))) return candidate;
        }
        throw new FileNotFoundException("shared/theme.json was not found next to Strata Photo.");
    }

    public static void Load()
    {
        SharedFolder = FindSharedFolder();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SharedFolder, "theme.json")));
        var root = doc.RootElement;
        _colors = root.GetProperty("colors").EnumerateObject()
            .ToDictionary(p => p.Name, p => Color.Parse(p.Value.GetString()!));
        var fonts = root.GetProperty("fonts");
        UiFonts = fonts.GetProperty("ui").EnumerateArray().Select(e => e.GetString()!).ToList();
        FontSizeSmall = fonts.GetProperty("sizeSmall").GetDouble();
        FontSizeNormal = fonts.GetProperty("sizeNormal").GetDouble();
        Radius = root.GetProperty("spacing").GetProperty("radius").GetDouble();
    }

    public static void Apply(IResourceDictionary res)
    {
        foreach (var (name, color) in _colors)
        {
            res[$"Strata.{name}"] = color;
            res[$"Strata.{name}Brush"] = new SolidColorBrush(color);
        }
        res["Strata.FontFamily"] = new FontFamily(string.Join(",", UiFonts.Where(f => f != "sans-serif")));
        res["Strata.FontSizeSmall"] = FontSizeSmall;
        res["Strata.FontSizeNormal"] = FontSizeNormal;
        res["Strata.CornerRadius"] = new CornerRadius(Radius);

        // Fluent theme (dark variant) -> Strata palette
        var accent = Get("accent");
        res["SystemAccentColor"] = accent;
        res["SystemAccentColorLight1"] = Get("accentSoft");
        res["SystemAccentColorLight2"] = Get("accentSoft");
        res["SystemAccentColorLight3"] = Get("accentSoft");
        res["SystemAccentColorDark1"] = Get("selection");
        res["SystemAccentColorDark2"] = Get("selection");
        res["SystemAccentColorDark3"] = Get("selection");
        res["SystemAltHighColor"] = Get("bgDark");
        res["SystemAltMediumHighColor"] = Get("bg");
        res["SystemChromeLowColor"] = Get("bg");
        res["SystemChromeMediumLowColor"] = Get("bgPanel");
        res["SystemChromeMediumColor"] = Get("bgPanel");
        res["SystemChromeHighColor"] = Get("border");
        res["SystemChromeAltLowColor"] = Get("text");
        res["SystemBaseHighColor"] = Get("text");
        res["SystemBaseMediumHighColor"] = Get("text");
        res["SystemBaseMediumColor"] = Get("textDim");
        res["SystemRegionColor"] = Get("bg");
        res["SystemControlBackgroundAltHighBrush"] = Brush("bg");
        res["SystemControlHighlightAccentBrush"] = Brush("accent");
        res["SystemControlForegroundAccentBrush"] = Brush("accent");
        res["TextControlBackground"] = Brush("bgInput");
        res["TextControlBackgroundPointerOver"] = Brush("bgInput");
        res["TextControlBackgroundFocused"] = Brush("bgInput");
        res["TextControlBorderBrush"] = Brush("border");
        res["TextControlBorderBrushPointerOver"] = Brush("borderLight");
        res["TextControlBorderBrushFocused"] = Brush("accent");
        res["TextControlForeground"] = Brush("text");
        res["TextControlForegroundFocused"] = Brush("textBright");
        res["TextControlForegroundPointerOver"] = Brush("text");
        res["ComboBoxBackground"] = Brush("bgInput");
        res["ComboBoxBackgroundPointerOver"] = Brush("bgLight");
        res["ComboBoxBorderBrush"] = Brush("border");
        res["ComboBoxDropDownBackground"] = Brush("bgPanel");
        res["ButtonBackground"] = Brush("bgLight");
        res["ButtonBackgroundPointerOver"] = Brush("bgHover");
        res["ButtonBackgroundPressed"] = Brush("selection");
        res["ButtonBorderBrush"] = Brush("borderLight");
        res["ButtonForeground"] = Brush("text");
        res["MenuFlyoutPresenterBackground"] = Brush("bgPanel");
        res["MenuFlyoutPresenterBorderBrush"] = Brush("border");
        res["MenuFlyoutItemBackgroundPointerOver"] = Brush("selection");
        res["MenuBarBackground"] = Brush("bg");
        res["ToolTipBackground"] = Brush("bgPanel");
        res["ToolTipForeground"] = Brush("text");
        res["ToolTipBorderBrush"] = Brush("border");
        res["SliderTrackFill"] = Brush("border");
        res["SliderTrackValueFill"] = Brush("accent");
        res["SliderThumbBackground"] = Brush("text");
        res["ListBoxItemSelectedBackground"] = Brush("selection");

        // Dock theme -> Strata palette
        res["DockThemeBackgroundBrush"] = Brush("bg");
        res["DockThemeForegroundBrush"] = Brush("text");
        res["DockThemeAccentBrush"] = Brush("accent");
        res["DockThemeBorderLowBrush"] = Brush("border");
        res["DockThemeControlBackgroundBrush"] = Brush("bgPanel");
        res["DockApplicationAccentBrush"] = Brush("accent");
        res["DockApplicationAccentForegroundBrush"] = Brush("textBright");
        res["DockBorderStrongBrush"] = Brush("border");
        res["DockBorderSubtleBrush"] = Brush("borderSubtle");
        res["DockSeparatorBrush"] = Brush("bgDark");
        res["DockSplitterIdleBrush"] = Brush("bgDark");
        res["DockSplitterHoverBrush"] = Brush("accent");
        res["DockSplitterDragBrush"] = Brush("accent");
        res["DockSurfaceWorkbenchBrush"] = Brush("bgDark");
        res["DockSurfaceEditorBrush"] = Brush("canvasBg");
        res["DockSurfacePanelBrush"] = Brush("bg");
        res["DockSurfaceSidebarBrush"] = Brush("bg");
        res["DockSurfaceHeaderBrush"] = Brush("bgPanel");
        res["DockSurfaceHeaderActiveBrush"] = Brush("bgPanel");
        res["DockTabBackgroundBrush"] = Brush("bg");
        res["DockTabForegroundBrush"] = Brush("textDim");
        res["DockTabHoverBackgroundBrush"] = Brush("bgLight");
        res["DockTabActiveBackgroundBrush"] = Brush("bgPanel");
        res["DockTabActiveForegroundBrush"] = Brush("textBright");
        res["DockTabSelectedForegroundBrush"] = Brush("textBright");
        res["DockTabActiveIndicatorBrush"] = Brush("accent");
        res["DockTabCloseHoverBackgroundBrush"] = Brush("bgHover");
        res["DockDocumentTabStripBackgroundBrush"] = Brush("bg");
        res["DockDocumentTabSelectedForegroundBrush"] = Brush("textBright");
        res["DockDocumentTabPointerOverForegroundBrush"] = Brush("text");
        res["DockDocumentContentBorderBrush"] = Brush("border");
        res["DockChromeButtonForegroundBrush"] = Brush("textDim");
        res["DockChromeButtonHoverBackgroundBrush"] = Brush("bgHover");
        res["DockChromeButtonPressedBackgroundBrush"] = Brush("selection");
        res["DockChromeGripBrush"] = Brush("borderLight");
        res["DockToolChromeIconBrush"] = Brush("textDim");
        res["DockTargetIndicatorBrush"] = new SolidColorBrush(Get("accent"), 0.4);
        res["DockWindowChromeBackgroundBrush"] = Brush("bg");
        res["DockWindowChromeBorderBrush"] = Brush("border");
        res["DockWindowChromeForegroundBrush"] = Brush("text");
        res["DockWindowChromeTitleBarBackgroundBrush"] = Brush("bgPanel");
    }

    private static SolidColorBrush Brush(string name) => new(Get(name));
}
