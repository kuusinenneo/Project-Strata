using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Painting;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Tools;

/// <summary>Brush, eraser and clone stamp.</summary>
public sealed class BrushTool : Tool
{
    private StrokeSession? _stroke;
    private SKPoint? _lastStrokeEnd;
    private SKPoint? _cloneSource;
    private SKPointI? _cloneOffset;

    public BrushTool(ToolKind kind) => Kind = kind;

    public override ToolKind Kind { get; }
    public override bool IsBusy => _stroke != null;
    public SKPoint? CloneSource => _cloneSource;

    public override ToolOverlay? Overlay =>
        Kind == ToolKind.CloneStamp && _cloneSource is { } s && _stroke == null ? new MarkerOverlay(s) : null;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        var s = ctx.Settings;
        var brush = s.BrushFor(Kind)!;
        SKPointI offset = default;
        if (Kind == ToolKind.CloneStamp)
        {
            if (p.Alt)
            {
                _cloneSource = p.Pos;
                _cloneOffset = null;
                ctx.Message = "Clone source set. Paint to clone from it.";
                return;
            }
            if (_cloneSource == null)
            {
                ctx.Message = "Alt+click to set the clone source first.";
                return;
            }
            if (_cloneOffset == null || !s.CloneAligned)
                _cloneOffset = new SKPointI((int)MathF.Round(_cloneSource.Value.X - p.Pos.X),
                    (int)MathF.Round(_cloneSource.Value.Y - p.Pos.Y));
            offset = _cloneOffset.Value;
        }
        var mode = Kind switch { ToolKind.Eraser => StrokeMode.Erase, ToolKind.CloneStamp => StrokeMode.Clone, _ => StrokeMode.Paint };
        // Shift+click draws a straight line from the end of the previous stroke.
        var start = p.Shift && _lastStrokeEnd is { } last ? last : p.Pos;
        _stroke = StrokeSession.Begin(ctx.Doc, brush, mode, s.Foreground, start, p.Pressure, offset);
        if (_stroke == null)
        {
            ctx.Message = PixelOps.CannotEditReason(ctx.Doc);
            return;
        }
        if (start != p.Pos) _stroke.MoveTo(p.Pos, p.Pressure);
    }

    public override void Move(ToolContext ctx, PointerInput p) => _stroke?.MoveTo(p.Pos, p.Pressure);

    /// <summary>Several coalesced samples at once (smoother strokes from fast mice and tablets).</summary>
    public void MoveMany(IEnumerable<(SKPoint pos, float pressure)> points) => _stroke?.MoveTo(points);

    public override void Up(ToolContext ctx, PointerInput p)
    {
        if (_stroke == null) return;
        _stroke.End();
        _stroke = null;
        _lastStrokeEnd = p.Pos;
    }

    public override void Cancel(ToolContext ctx)
    {
        _stroke?.Cancel();
        _stroke = null;
    }
}

/// <summary>Rectangular and elliptical marquee.</summary>
public sealed class MarqueeTool : Tool
{
    private SKPoint _start;
    private SKRect? _rect;
    private SelectionMode _mode;
    private bool _modeFromShift;

    public MarqueeTool(bool ellipse) => Ellipse = ellipse;

    public bool Ellipse { get; }
    public override ToolKind Kind => Ellipse ? ToolKind.EllipseMarquee : ToolKind.RectMarquee;
    public override bool IsBusy => _rect != null;
    public override ToolOverlay? Overlay => _rect is { } r ? new RectOverlay(r, Ellipse) : null;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        _start = p.Pos;
        _mode = ModeFor(ctx.Settings.SelectionMode, p);
        _modeFromShift = p.Shift && !p.Alt;
        _rect = new SKRect(p.Pos.X, p.Pos.Y, p.Pos.X, p.Pos.Y);
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_rect == null) return;
        bool square = p.Shift && !_modeFromShift;
        _rect = DragRect(_start, p.Pos, square, p.Alt && _mode != SelectionMode.Subtract);
    }

    public override void Up(ToolContext ctx, PointerInput p)
    {
        if (_rect is not { } r) return;
        _rect = null;
        var doc = ctx.Doc;
        if (r.Width < 1 || r.Height < 1)
        {
            if (_mode == SelectionMode.Replace) doc.SetSelection("Deselect", Selection.None);
            return;
        }
        var sel = Ellipse
            ? SelectionOps.Ellipse(doc.Width, doc.Height, r, doc.Selection, _mode, ctx.Settings.Feather)
            : SelectionOps.Rectangle(doc.Width, doc.Height, r, doc.Selection, _mode, ctx.Settings.Feather);
        doc.SetSelection(Ellipse ? "Elliptical Marquee" : "Rectangular Marquee", sel);
    }

    public override void Cancel(ToolContext ctx) => _rect = null;

    internal static SelectionMode ModeFor(SelectionMode setting, PointerInput p) =>
        p.Shift && p.Alt ? SelectionMode.Intersect
        : p.Shift ? SelectionMode.Add
        : p.Alt ? SelectionMode.Subtract
        : setting;
}

/// <summary>Freehand lasso; Alt+click points for straight segments are covered by dragging.</summary>
public sealed class LassoTool : Tool
{
    private List<SKPoint>? _points;
    private SelectionMode _mode;

    public override ToolKind Kind => ToolKind.Lasso;
    public override bool IsBusy => _points != null;
    public override ToolOverlay? Overlay => _points is { Count: > 1 } pts ? new PolylineOverlay(pts.ToArray(), false) : null;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        _mode = MarqueeTool.ModeFor(ctx.Settings.SelectionMode, p);
        _points = new List<SKPoint> { p.Pos };
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_points == null) return;
        if (SKPoint.Distance(_points[^1], p.Pos) >= 1) _points.Add(p.Pos);
    }

    public override void Up(ToolContext ctx, PointerInput p)
    {
        if (_points == null) return;
        var pts = _points;
        _points = null;
        var doc = ctx.Doc;
        if (pts.Count < 3)
        {
            if (_mode == SelectionMode.Replace) doc.SetSelection("Deselect", Selection.None);
            return;
        }
        doc.SetSelection("Lasso", SelectionOps.Polygon(doc.Width, doc.Height, pts, doc.Selection, _mode, ctx.Settings.Feather));
    }

    public override void Cancel(ToolContext ctx) => _points = null;
}

public sealed class MagicWandTool : Tool
{
    public override ToolKind Kind => ToolKind.MagicWand;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        var doc = ctx.Doc;
        var s = ctx.Settings;
        int x = (int)MathF.Floor(p.Pos.X), y = (int)MathF.Floor(p.Pos.Y);
        if (x < 0 || y < 0 || x >= doc.Width || y >= doc.Height) return;
        var mode = MarqueeTool.ModeFor(s.SelectionMode, p);
        SKBitmap? source = s.SampleAllLayers ? Compositor.Flatten(doc.Root, doc.Width, doc.Height) : doc.ActivePixelLayer?.Pixels;
        if (source == null)
        {
            ctx.Message = "Select a pixel layer or turn on \"Sample all layers\".";
            return;
        }
        var sel = SelectionOps.MagicWand(source, x, y, s.WandTolerance, s.WandContiguous, s.WandAntiAlias, doc.Selection, mode);
        if (s.SampleAllLayers) source.Dispose();
        doc.SetSelection("Magic Wand", sel);
    }
}

public sealed class EyedropperTool : Tool
{
    private bool _down;
    private bool _toBackground;

    public override ToolKind Kind => ToolKind.Eyedropper;
    public override bool IsBusy => _down;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        _down = true;
        _toBackground = p.Alt;
        Sample(ctx, p);
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_down) Sample(ctx, p);
    }

    public override void Up(ToolContext ctx, PointerInput p) => _down = false;

    private void Sample(ToolContext ctx, PointerInput p)
    {
        int x = (int)MathF.Floor(p.Pos.X), y = (int)MathF.Floor(p.Pos.Y);
        if (x < 0 || y < 0 || x >= ctx.Doc.Width || y >= ctx.Doc.Height) return;
        var c = PaintOps.SampleColor(ctx.Doc, x, y, allLayers: true);
        if (c.Alpha == 0) return;
        c = c.WithAlpha(255);
        if (_toBackground) ctx.Settings.Background = c;
        else ctx.Settings.Foreground = c;
    }
}

public sealed class FillTool : Tool
{
    public override ToolKind Kind => ToolKind.Fill;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        var s = ctx.Settings;
        var ok = PaintOps.BucketFill(ctx.Doc, (int)MathF.Floor(p.Pos.X), (int)MathF.Floor(p.Pos.Y),
            p.Alt ? s.Background : s.Foreground, s.FillTolerance, s.FillContiguous, s.SampleAllLayers, s.FillOpacity);
        if (!ok) ctx.Message = PixelOps.CannotEditReason(ctx.Doc);
    }
}

public sealed class GradientTool : Tool
{
    private SKPoint _start;
    private SKPoint? _end;

    public override ToolKind Kind => ToolKind.Gradient;
    public override bool IsBusy => _end != null;
    public override ToolOverlay? Overlay => _end is { } e ? new LineOverlay(_start, e) : null;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        if (!PixelOps.CanEdit(ctx.Doc))
        {
            ctx.Message = PixelOps.CannotEditReason(ctx.Doc);
            return;
        }
        _start = p.Pos;
        _end = p.Pos;
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_end != null) _end = p.Shift ? Constrain45(_start, p.Pos) : p.Pos;
    }

    public override void Up(ToolContext ctx, PointerInput p)
    {
        if (_end is not { } end) return;
        _end = null;
        var s = ctx.Settings;
        SKColor from = s.Foreground, to = s.GradientToTransparent ? s.Foreground.WithAlpha(0) : s.Background;
        if (s.GradientReverse) (from, to) = (to, from);
        PaintOps.Gradient(ctx.Doc, _start, end, from, to, s.GradientType, s.GradientOpacity);
    }

    public override void Cancel(ToolContext ctx) => _end = null;
}

public sealed class ShapeTool : Tool
{
    private SKPoint _start;
    private SKPoint? _end;
    private bool _alt, _shift;

    public ShapeTool(ShapeKind shape) => Shape = shape;

    public ShapeKind Shape { get; }
    public override ToolKind Kind => Shape switch
    {
        ShapeKind.Rectangle => ToolKind.Rectangle,
        ShapeKind.Ellipse => ToolKind.Ellipse,
        _ => ToolKind.Line,
    };
    public override bool IsBusy => _end != null;

    private SKToolSettingsSnapshot? _style;

    private sealed record SKToolSettingsSnapshot(SKColor Color, bool Fill, float Width);

    public override ToolOverlay? Overlay
    {
        get
        {
            if (_end is not { } e || _style == null) return null;
            var (a, b) = Endpoints(e);
            return new ShapeOverlay(Shape, a, b, _style.Color, _style.Fill, _style.Width);
        }
    }

    public override void Down(ToolContext ctx, PointerInput p)
    {
        _start = p.Pos;
        _end = p.Pos;
        var s = ctx.Settings;
        _style = new SKToolSettingsSnapshot(s.Foreground, s.ShapeFill, s.ShapeStrokeWidth);
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_end == null) return;
        _end = p.Pos;
        _alt = p.Alt;
        _shift = p.Shift;
    }

    private (SKPoint, SKPoint) Endpoints(SKPoint end)
    {
        if (Shape == ShapeKind.Line) return (_start, _shift ? Constrain45(_start, end) : end);
        var r = DragRect(_start, end, _shift, _alt);
        return (new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom));
    }

    public override void Up(ToolContext ctx, PointerInput p)
    {
        if (_end == null || _style == null) return;
        _end = p.Pos;
        _alt = p.Alt;
        _shift = p.Shift;
        var (a, b) = Endpoints(p.Pos);
        _end = null;
        PaintOps.Shape(ctx.Doc, Shape, a, b, _style.Color, _style.Fill, _style.Width);
    }

    public override void Cancel(ToolContext ctx) => _end = null;
}

/// <summary>Move tool: drags the active layer, or the selected pixels when there is a selection.</summary>
public sealed class MoveTool : Tool
{
    private TransformSession? _session;
    private SKPoint _start;

    public override ToolKind Kind => ToolKind.Move;
    public override bool IsBusy => _session != null;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        _session = TransformSession.Begin(ctx.Doc);
        if (_session == null)
            ctx.Message = ctx.Doc.ActiveLayer is GroupLayer ? "Select a layer inside the group to move it."
                : ctx.Doc.ActiveLayer?.Locked == true ? "The layer is locked." : "Nothing to move on this layer.";
        _start = p.Pos;
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_session == null) return;
        var d = p.Pos - _start;
        if (p.Shift) d = Math.Abs(d.X) > Math.Abs(d.Y) ? new SKPoint(d.X, 0) : new SKPoint(0, d.Y);
        _session.SetMatrix(SKMatrix.CreateTranslation(MathF.Round(d.X), MathF.Round(d.Y)));
    }

    public override void Up(ToolContext ctx, PointerInput p)
    {
        _session?.Commit("Move");
        _session = null;
    }

    public override void Cancel(ToolContext ctx)
    {
        _session?.Cancel();
        _session = null;
    }

    /// <summary>Arrow-key nudge.</summary>
    public static void Nudge(Document doc, int dx, int dy)
    {
        using var s = TransformSession.Begin(doc);
        if (s == null) return;
        s.SetMatrix(SKMatrix.CreateTranslation(dx, dy));
        s.Commit("Nudge");
    }
}
