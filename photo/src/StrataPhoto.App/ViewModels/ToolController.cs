using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Painting;
using StrataPhoto.Core.Selections;
using StrataPhoto.Core.Tools;

namespace StrataPhoto.App.ViewModels;

/// <summary>Current tool, tool options and routing of canvas pointer input to Core tools.</summary>
public sealed partial class ToolController : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Dictionary<ToolKind, Tool> _tools;
    private readonly FreeTransformTool _transform = new();
    private ToolContext? _activeCtx;

    public ToolController(MainViewModel main)
    {
        _main = main;
        _tools = new Dictionary<ToolKind, Tool>
        {
            [ToolKind.Move] = new MoveTool(),
            [ToolKind.RectMarquee] = new MarqueeTool(false),
            [ToolKind.EllipseMarquee] = new MarqueeTool(true),
            [ToolKind.Lasso] = new LassoTool(),
            [ToolKind.MagicWand] = new MagicWandTool(),
            [ToolKind.Crop] = new CropTool(),
            [ToolKind.Eyedropper] = new EyedropperTool(),
            [ToolKind.Brush] = new BrushTool(ToolKind.Brush),
            [ToolKind.Eraser] = new BrushTool(ToolKind.Eraser),
            [ToolKind.CloneStamp] = new BrushTool(ToolKind.CloneStamp),
            [ToolKind.Fill] = new FillTool(),
            [ToolKind.Gradient] = new GradientTool(),
            [ToolKind.Rectangle] = new ShapeTool(ShapeKind.Rectangle),
            [ToolKind.Ellipse] = new ShapeTool(ShapeKind.Ellipse),
            [ToolKind.Line] = new ShapeTool(ShapeKind.Line),
        };
        Settings.Brush.PropertyChanged += (_, _) => OverlayChanged?.Invoke();
        Settings.Eraser.PropertyChanged += (_, _) => OverlayChanged?.Invoke();
        Settings.Clone.PropertyChanged += (_, _) => OverlayChanged?.Invoke();
    }

    public ToolSettings Settings { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveBrush), nameof(IsBrushTool), nameof(IsCloneTool), nameof(IsSelectionTool),
        nameof(IsWandTool), nameof(IsFillTool), nameof(IsGradientTool), nameof(IsShapeTool), nameof(IsTextTool),
        nameof(IsMoveTool), nameof(IsCropTool), nameof(IsViewTool), nameof(IsEyedropperTool), nameof(ToolName), nameof(ToolHint))]
    private ToolKind _current = ToolKind.Brush;

    /// <summary>Raised when the overlay (marquee, crop box, brush cursor size...) changes.</summary>
    public event Action? OverlayChanged;

    public bool TransformActive => _transform.IsActive;
    public bool CropPending => _tools[ToolKind.Crop].IsBusy && Current == ToolKind.Crop;

    private Tool? CurrentTool => _transform.IsActive ? _transform : _tools.GetValueOrDefault(Current);

    public ToolOverlay? Overlay => CurrentTool?.Overlay;

    public BrushSettings? ActiveBrush => Settings.BrushFor(Current);
    public float BrushDiameter => ActiveBrush?.Size ?? 1;
    public bool ShowsBrushCursor => ActiveBrush != null && !_transform.IsActive;
    public bool WantsCoalescedPoints => CurrentTool is BrushTool or LassoTool;

    public bool IsBrushTool => Current is ToolKind.Brush or ToolKind.Eraser or ToolKind.CloneStamp;
    public bool IsCloneTool => Current == ToolKind.CloneStamp;
    public bool IsSelectionTool => Current is ToolKind.RectMarquee or ToolKind.EllipseMarquee or ToolKind.Lasso or ToolKind.MagicWand;
    public bool IsWandTool => Current == ToolKind.MagicWand;
    public bool IsFillTool => Current == ToolKind.Fill;
    public bool IsGradientTool => Current == ToolKind.Gradient;
    public bool IsShapeTool => Current is ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Line;
    public bool IsTextTool => Current == ToolKind.Text;
    public bool IsMoveTool => Current == ToolKind.Move;
    public bool IsCropTool => Current == ToolKind.Crop;
    public bool IsViewTool => Current is ToolKind.Hand or ToolKind.Zoom;
    public bool IsEyedropperTool => Current == ToolKind.Eyedropper;

    public string ToolName => ToolInfo.Get(Current).Name;
    public string ToolHint => ToolInfo.Get(Current).Hint;

    public void Select(ToolKind kind)
    {
        if (kind == Current) return;
        CancelActive();
        Current = kind;
        OverlayChanged?.Invoke();
    }

    /// <summary>Repeated shortcut presses cycle through a tool group (M: marquees, U: shapes, G: fill/gradient).</summary>
    public void SelectFromShortcut(ToolKind[] group)
    {
        int i = Array.IndexOf(group, Current);
        Select(i < 0 ? group[0] : group[(i + 1) % group.Length]);
    }

    private ToolContext Ctx(DocumentViewModel d) => _activeCtx = new ToolContext(d.Document, Settings);

    private void AfterInput(ToolContext ctx)
    {
        if (ctx.Message != null) _main.StatusMessage = ctx.Message;
        OverlayChanged?.Invoke();
        OnPropertyChanged(nameof(TransformActive));
        OnPropertyChanged(nameof(CropPending));
    }

    public void PointerDown(DocumentViewModel d, PointerInput p)
    {
        var tool = CurrentTool;
        if (tool == null) return;
        // Painting on a text layer: offer the usual fix in the status bar.
        var ctx = Ctx(d);
        tool.Down(ctx, p);
        AfterInput(ctx);
    }

    public void PointerMove(DocumentViewModel d, PointerInput p)
    {
        var tool = CurrentTool;
        if (tool == null || _activeCtx == null) return;
        tool.Move(_activeCtx, p);
        AfterInput(_activeCtx);
    }

    public void PointerMoveMany(DocumentViewModel d, IReadOnlyList<(SKPoint, float)> samples, PointerInput last)
    {
        var tool = CurrentTool;
        if (_activeCtx == null) return;
        if (tool is BrushTool b) b.MoveMany(samples);
        else
            foreach (var (pos, pr) in samples)
                tool?.Move(_activeCtx, last with { Pos = pos, Pressure = pr });
        OverlayChanged?.Invoke();
    }

    public void PointerUp(DocumentViewModel d, PointerInput p)
    {
        var tool = CurrentTool;
        if (tool == null || _activeCtx == null) return;
        tool.Up(_activeCtx, p);
        AfterInput(_activeCtx);
    }

    public Handle TransformHitTest(SKPoint p, float radius) => _transform.HitTest(p, radius);

    public bool BeginFreeTransform(DocumentViewModel d)
    {
        CancelActive();
        var ctx = Ctx(d);
        bool ok = _transform.Begin(ctx);
        AfterInput(ctx);
        return ok;
    }

    /// <summary>Enter key: apply a pending crop or transform.</summary>
    public bool CommitActive()
    {
        if (_activeCtx == null) return false;
        var tool = CurrentTool;
        if (tool == null || !tool.IsBusy) return false;
        tool.Commit(_activeCtx);
        _main.StatusMessage = "";
        AfterInput(_activeCtx);
        return true;
    }

    /// <summary>Esc / tool switch / document switch: abandon whatever is in progress.</summary>
    public bool CancelActive()
    {
        bool any = false;
        if (_activeCtx != null)
        {
            if (_transform.IsActive)
            {
                _transform.Cancel(_activeCtx);
                any = true;
            }
            foreach (var t in _tools.Values.Where(t => t.IsBusy))
            {
                t.Cancel(_activeCtx);
                any = true;
            }
            AfterInput(_activeCtx);
        }
        return any;
    }

    public void StepBrushSize(int direction)
    {
        ActiveBrush?.StepSize(direction);
        OverlayChanged?.Invoke();
    }

    public void SetSelectionMode(SelectionMode mode) => Settings.SelectionMode = mode;
}

/// <summary>Names, icons, shortcuts and hints for the tools strip.</summary>
public sealed record ToolInfo(ToolKind Kind, string Name, string Icon, string Key, string Hint)
{
    public static readonly IReadOnlyList<ToolInfo> All = new[]
    {
        new ToolInfo(ToolKind.Move, "Move", "move", "V", "Drag to move the layer or the selected pixels. Arrow keys nudge. Ctrl+T: Free Transform."),
        new ToolInfo(ToolKind.RectMarquee, "Rectangular Marquee", "marquee-rect", "M", "Drag to select. Shift: add / square, Alt: subtract / from centre."),
        new ToolInfo(ToolKind.EllipseMarquee, "Elliptical Marquee", "marquee-ellipse", "M", "Drag to select. Shift: add / circle, Alt: subtract / from centre."),
        new ToolInfo(ToolKind.Lasso, "Lasso", "lasso", "L", "Drag around an area to select it. Shift: add, Alt: subtract."),
        new ToolInfo(ToolKind.MagicWand, "Magic Wand", "magic-wand", "W", "Click to select similar colours. Shift: add, Alt: subtract."),
        new ToolInfo(ToolKind.Crop, "Crop", "crop", "C", "Drag a crop box, adjust its handles, then press Enter. Esc cancels."),
        new ToolInfo(ToolKind.Eyedropper, "Eyedropper", "eyedropper", "I", "Click to pick the foreground colour. Alt+click: background colour."),
        new ToolInfo(ToolKind.Brush, "Brush", "brush", "B", "Paint with the foreground colour. [ and ] change the size. Shift+click: straight line."),
        new ToolInfo(ToolKind.Eraser, "Eraser", "eraser", "E", "Erase to transparency (on a mask: hide). [ and ] change the size."),
        new ToolInfo(ToolKind.CloneStamp, "Clone Stamp", "clone-stamp", "S", "Alt+click to set the source, then paint to copy pixels from it."),
        new ToolInfo(ToolKind.Fill, "Paint Bucket", "fill", "G", "Click to fill similar colours with the foreground colour. Alt+click: background colour."),
        new ToolInfo(ToolKind.Gradient, "Gradient", "gradient", "G", "Drag to draw a gradient from the foreground to the background colour. Shift: 45° steps."),
        new ToolInfo(ToolKind.Text, "Text", "text", "T", "Click to add text, or click existing text to edit it."),
        new ToolInfo(ToolKind.Rectangle, "Rectangle", "rectangle", "U", "Drag to draw a rectangle on a new layer. Shift: square, Alt: from centre."),
        new ToolInfo(ToolKind.Ellipse, "Ellipse", "ellipse", "U", "Drag to draw an ellipse on a new layer. Shift: circle, Alt: from centre."),
        new ToolInfo(ToolKind.Line, "Line", "line", "U", "Drag to draw a line on a new layer. Shift: 45° steps."),
        new ToolInfo(ToolKind.Hand, "Hand", "hand", "H", "Drag to pan. Hold Space with any tool to pan temporarily."),
        new ToolInfo(ToolKind.Zoom, "Zoom", "zoom", "Z", "Click to zoom in, Alt+click to zoom out. Ctrl+wheel zooms anywhere."),
    };

    public static ToolInfo Get(ToolKind k) => All.First(t => t.Kind == k);
}
