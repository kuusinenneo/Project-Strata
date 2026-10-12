using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using Strata.Ui;
using StrataPhoto.App.ViewModels;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Tools;

namespace StrataPhoto.App.Canvas;

/// <summary>
/// The editing surface: shows the document's composite (GPU tiles) with zoom and pan, forwards
/// pointer and pen input to the active tool, and draws selection outlines and tool overlays.
/// </summary>
public sealed class CanvasView : Control
{
    private DocumentViewModel? _vm;
    private readonly DispatcherTimer _antsTimer;
    private float _antsPhase;
    private Point? _pointer;          // last pointer position (view coordinates)
    private bool _panning;
    private Point _panStart;
    private Point _panOrigin;
    private bool _toolDown;
    private bool _spaceHeld;

    public CanvasView()
    {
        Focusable = true;
        ClipToBounds = true;
        _antsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(110), DispatcherPriority.Background, (_, _) =>
        {
            _antsPhase = (_antsPhase + 1) % 8;
            if (_vm?.Document.Selection.IsEmpty == false || _vm?.Main.Tools.Overlay is CropOverlay) InvalidateVisual();
        });
    }

    public DocumentViewModel? ViewModel => _vm;

    private double RenderScale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    /// <summary>View (DIP) units per document pixel.</summary>
    private double Scale => (_vm?.Zoom ?? 1) / RenderScale;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Detach();
        _vm = DataContext as DocumentViewModel;
        if (_vm != null)
        {
            _vm.Document.Invalidated += OnDocInvalidated;
            _vm.Document.SelectionChanged += InvalidateVisual;
            _vm.Document.SizeChanged += InvalidateVisual;
            _vm.ViewChanged += InvalidateVisual;
            _vm.Main.Tools.OverlayChanged += InvalidateVisual;
            _vm.Canvas = this;
        }
        InvalidateVisual();
    }

    private void Detach()
    {
        if (_vm == null) return;
        _vm.Document.Invalidated -= OnDocInvalidated;
        _vm.Document.SelectionChanged -= InvalidateVisual;
        _vm.Document.SizeChanged -= InvalidateVisual;
        _vm.ViewChanged -= InvalidateVisual;
        _vm.Main.Tools.OverlayChanged -= InvalidateVisual;
        if (_vm.Canvas == this) _vm.Canvas = null;
        _vm = null;
    }

    private void OnDocInvalidated(SKRectI r)
    {
        _vm?.Tiles.MarkDirty(r);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _antsTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _antsTimer.Stop();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_vm != null && _vm.NeedsFit && Bounds.Width > 10 && Bounds.Height > 10)
            _vm.FitToView(Bounds.Size, RenderScale);
    }

    // ------------------------------------------------------------------ coordinates

    public SKPoint ToDoc(Point p)
    {
        if (_vm == null) return default;
        double s = Scale;
        return new SKPoint((float)((p.X - _vm.Pan.X) / s), (float)((p.Y - _vm.Pan.Y) / s));
    }

    public Point ToView(SKPoint p)
    {
        if (_vm == null) return default;
        double s = Scale;
        return new Point(p.X * s + _vm.Pan.X, p.Y * s + _vm.Pan.Y);
    }

    public Size ViewSize => Bounds.Size;
    public double CurrentRenderScale => RenderScale;

    // ------------------------------------------------------------------ input

    private PointerInput MakeInput(PointerEventArgs e, Point pos, float? pressure = null, int clicks = 1)
    {
        var mods = e.KeyModifiers;
        var props = e.GetCurrentPoint(this).Properties;
        float p = pressure ?? (e.Pointer.Type == PointerType.Pen ? props.Pressure : 1f);
        return new PointerInput(ToDoc(pos), Math.Clamp(p, 0f, 1f),
            Shift: mods.HasFlag(KeyModifiers.Shift), Alt: mods.HasFlag(KeyModifiers.Alt),
            Ctrl: mods.HasFlag(KeyModifiers.Control),
            HandleRadius: (float)(7 / Scale), DoubleClick: clicks >= 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm == null) return;
        Focus();
        var pt = e.GetCurrentPoint(this);
        var pos = pt.Position;
        _pointer = pos;
        var tools = _vm.Main.Tools;

        bool pan = pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed &&
                   (_spaceHeld || (tools.Current == ToolKind.Hand && !tools.TransformActive)));
        if (pan)
        {
            _panning = true;
            _panStart = pos;
            _panOrigin = _vm.Pan;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;

        if (tools.Current == ToolKind.Zoom && !tools.TransformActive)
        {
            _vm.ZoomStep(e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? -1 : 1, pos, RenderScale);
            e.Handled = true;
            return;
        }
        if (tools.Current == ToolKind.Text && !tools.TransformActive)
        {
            _vm.Main.BeginTextEdit(_vm, ToDoc(pos));
            e.Handled = true;
            return;
        }

        _toolDown = true;
        e.Pointer.Capture(this);
        tools.PointerDown(_vm, MakeInput(e, pos, clicks: e.ClickCount));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm == null) return;
        var pos = e.GetPosition(this);
        _pointer = pos;
        var docPos = ToDoc(pos);
        _vm.SetCursor(docPos);

        if (_panning)
        {
            _vm.Pan = _panOrigin + (pos - _panStart);
            return;
        }
        UpdateCursor();
        if (_toolDown)
        {
            var tools = _vm.Main.Tools;
            if (tools.WantsCoalescedPoints)
            {
                // Use every sample the OS delivered since the last event (smooth fast strokes, pen pressure).
                var pts = e.GetIntermediatePoints(this);
                var samples = new List<(SKPoint, float)>(pts.Count);
                foreach (var ip in pts)
                {
                    float pr = e.Pointer.Type == PointerType.Pen ? ip.Properties.Pressure : 1f;
                    samples.Add((ToDoc(ip.Position), Math.Clamp(pr, 0f, 1f)));
                }
                if (samples.Count == 0) samples.Add((docPos, MakeInput(e, pos).Pressure));
                tools.PointerMoveMany(_vm, samples, MakeInput(e, pos));
            }
            else
            {
                tools.PointerMove(_vm, MakeInput(e, pos));
            }
        }
        if (_toolDown || _vm.Main.Tools.ShowsBrushCursor) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm == null) return;
        var pos = e.GetPosition(this);
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            UpdateCursor();
            return;
        }
        if (_toolDown && e.InitialPressMouseButton == MouseButton.Left)
        {
            _toolDown = false;
            e.Pointer.Capture(null);
            _vm.Main.Tools.PointerUp(_vm, MakeInput(e, pos));
            InvalidateVisual();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_toolDown && _vm != null && _pointer is { } p)
        {
            _toolDown = false;
            _vm.Main.Tools.PointerUp(_vm, new PointerInput(ToDoc(p)));
        }
        _panning = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        _vm?.SetCursor(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm == null) return;
        var pos = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            double factor = Math.Pow(1.2, e.Delta.Y);
            _vm.ZoomAt(_vm.Zoom * factor, pos, RenderScale);
        }
        else
        {
            var d = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y, 0) : e.Delta;
            _vm.Pan += d * 60;
        }
        e.Handled = true;
    }

    /// <summary>Space-bar temporary hand tool (called by the window's key handler).</summary>
    public void SetSpaceHeld(bool held)
    {
        _spaceHeld = held;
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        if (_vm == null) return;
        var tools = _vm.Main.Tools;
        StandardCursorType type;
        if (_spaceHeld || (tools.Current == ToolKind.Hand && !tools.TransformActive)) type = StandardCursorType.Hand;
        else if (tools.TransformActive && _pointer is { } p)
        {
            type = tools.TransformHitTest(ToDoc(p), (float)(7 / Scale)) switch
            {
                Handle.Inside => StandardCursorType.SizeAll,
                Handle.Outside => StandardCursorType.Arrow,
                Handle.N or Handle.S => StandardCursorType.SizeNorthSouth,
                Handle.E or Handle.W => StandardCursorType.SizeWestEast,
                Handle.NW or Handle.SE => StandardCursorType.TopLeftCorner,
                _ => StandardCursorType.TopRightCorner,
            };
        }
        else type = tools.Current switch
        {
            ToolKind.Move => StandardCursorType.SizeAll,
            ToolKind.Text => StandardCursorType.Ibeam,
            ToolKind.Zoom => StandardCursorType.Cross,
            ToolKind.Brush or ToolKind.Eraser or ToolKind.CloneStamp =>
                tools.BrushDiameter * Scale >= 6 ? StandardCursorType.None : StandardCursorType.Cross,
            _ => StandardCursorType.Cross,
        };
        Cursor = new Cursor(type);
    }

    // ------------------------------------------------------------------ rendering

    public override void Render(DrawingContext context)
    {
        if (_vm == null) return;
        var doc = _vm.Document;
        var tools = _vm.Main.Tools;
        double s = Scale;
        var view = new SKMatrix((float)s, 0, (float)_vm.Pan.X, 0, (float)s, (float)_vm.Pan.Y, 0, 0, 1);
        var state = new OverlayState
        {
            View = view,
            Scale = (float)s,
            DeviceScale = (float)RenderScale,
            DocWidth = doc.Width,
            DocHeight = doc.Height,
            Selection = doc.Selection.IsEmpty ? null : doc.Selection.Outline, // built here, on the UI thread
            AntsPhase = _antsPhase,
            Tool = tools.Overlay,
            BrushCursor = _pointer is { } p && tools.ShowsBrushCursor && !_panning && !_spaceHeld
                ? (ToDoc(p), tools.BrushDiameter / 2f) : null,
            Crosshair = _pointer is { } cp && tools.ShowsBrushCursor && tools.BrushDiameter * s < 6 ? ToDoc(cp) : null,
            CanvasBg = SharedTheme.GetSk("canvasBg"),
            CheckerLight = SharedTheme.GetSk("checkerLight"),
            CheckerDark = SharedTheme.GetSk("checkerDark"),
            Accent = SharedTheme.GetSk("accent"),
        };
        context.Custom(new CanvasDrawOperation(new Rect(Bounds.Size), _vm.Tiles, state));
    }

    internal sealed class OverlayState
    {
        public SKMatrix View;
        public float Scale, DeviceScale;
        public int DocWidth, DocHeight;
        public SKPath? Selection;
        public float AntsPhase;
        public ToolOverlay? Tool;
        public (SKPoint pos, float radius)? BrushCursor;
        public SKPoint? Crosshair;
        public SKColor CanvasBg, CheckerLight, CheckerDark, Accent;
    }

    private sealed class CanvasDrawOperation : ICustomDrawOperation
    {
        private static SKShader? _checker;
        private readonly TileCache _tiles;
        private readonly OverlayState _s;

        public CanvasDrawOperation(Rect bounds, TileCache tiles, OverlayState state)
        {
            Bounds = bounds;
            _tiles = tiles;
            _s = state;
        }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease == null) return;
            using var l = lease.Lease();
            var canvas = l.SkCanvas;
            var s = _s;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            using (var bg = new SKPaint { Color = s.CanvasBg })
                canvas.DrawRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height), bg);

            var docRect = SKRect.Create(s.DocWidth, s.DocHeight);
            var screenDoc = s.View.MapRect(docRect);

            // drop shadow + checkerboard (constant on-screen size)
            using (var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(90), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) })
                canvas.DrawRect(SKRect.Inflate(screenDoc, 1, 1), shadow);
            using (var checker = new SKPaint { Shader = Checker(s) })
                canvas.DrawRect(screenDoc, checker);

            // image tiles
            if (!s.View.TryInvert(out var inv)) inv = SKMatrix.Identity;
            var visible = inv.MapRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            visible.Intersect(docRect);
            if (!visible.IsEmpty)
            {
                canvas.Save();
                canvas.Concat(ref s.View);
                _tiles.Draw(canvas, l.GrContext, visible, smooth: s.Scale * s.DeviceScale < 0.999f);
                canvas.Restore();
            }

            if (s.Scale * s.DeviceScale >= 10) DrawPixelGrid(canvas, s, visible);
            DrawSelection(canvas, s);
            DrawTool(canvas, s);
            DrawBrushCursor(canvas, s);
            canvas.Restore();
        }

        private static SKShader Checker(OverlayState s)
        {
            if (_checker == null)
            {
                using var bmp = new SKBitmap(16, 16);
                using (var c = new SKCanvas(bmp))
                {
                    c.Clear(s.CheckerLight);
                    using var p = new SKPaint { Color = s.CheckerDark };
                    c.DrawRect(0, 0, 8, 8, p);
                    c.DrawRect(8, 8, 8, 8, p);
                }
                _checker = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            }
            return _checker;
        }

        private static void DrawPixelGrid(SKCanvas canvas, OverlayState s, SKRect visible)
        {
            using var p = new SKPaint { Color = SKColors.Gray.WithAlpha(70), StrokeWidth = 1, IsAntialias = false };
            for (int x = (int)visible.Left; x <= (int)visible.Right; x++)
            {
                var a = s.View.MapPoint(x, visible.Top);
                var b = s.View.MapPoint(x, visible.Bottom);
                canvas.DrawLine(MathF.Round(a.X) + 0.5f, a.Y, MathF.Round(b.X) + 0.5f, b.Y, p);
            }
            for (int y = (int)visible.Top; y <= (int)visible.Bottom; y++)
            {
                var a = s.View.MapPoint(visible.Left, y);
                var b = s.View.MapPoint(visible.Right, y);
                canvas.DrawLine(a.X, MathF.Round(a.Y) + 0.5f, b.X, MathF.Round(b.Y) + 0.5f, p);
            }
        }

        private static SKPaint Ants(bool white, float phase) => new()
        {
            Color = white ? SKColors.White : SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = false,
            PathEffect = white ? SKPathEffect.CreateDash(new[] { 4f, 4f }, phase) : null,
        };

        private static void StrokeAnts(SKCanvas canvas, SKPath screenPath, float phase)
        {
            using var black = Ants(false, phase);
            using var white = Ants(true, phase);
            canvas.DrawPath(screenPath, black);
            canvas.DrawPath(screenPath, white);
        }

        private static void DrawSelection(SKCanvas canvas, OverlayState s)
        {
            if (s.Selection == null || s.Selection.IsEmpty) return;
            using var path = new SKPath();
            s.Selection.Transform(s.View, path);
            StrokeAnts(canvas, path, s.AntsPhase);
        }

        private static void DrawTool(SKCanvas canvas, OverlayState s)
        {
            switch (s.Tool)
            {
                case RectOverlay r:
                {
                    using var path = new SKPath();
                    var rr = s.View.MapRect(r.Rect);
                    if (r.Ellipse) path.AddOval(rr); else path.AddRect(rr);
                    StrokeAnts(canvas, path, s.AntsPhase);
                    break;
                }
                case PolylineOverlay pl when pl.Points.Count > 1:
                {
                    using var path = new SKPath();
                    path.AddPoly(s.View.MapPoints(pl.Points.ToArray()), pl.Closed);
                    StrokeAnts(canvas, path, s.AntsPhase);
                    break;
                }
                case LineOverlay line:
                {
                    var a = s.View.MapPoint(line.From);
                    var b = s.View.MapPoint(line.To);
                    using var p = new SKPaint { Color = SKColors.Black.WithAlpha(160), StrokeWidth = 3, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
                    canvas.DrawLine(a, b, p);
                    p.Color = SKColors.White;
                    p.StrokeWidth = 1.2f;
                    canvas.DrawLine(a, b, p);
                    p.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(a, 3.5f, p);
                    canvas.DrawCircle(b, 3.5f, p);
                    break;
                }
                case ShapeOverlay sh:
                {
                    canvas.Save();
                    canvas.Concat(ref s.View);
                    PaintOps.DrawShape(canvas, sh.Kind, sh.From, sh.To, sh.Color.WithAlpha(200), sh.Fill, sh.StrokeWidth);
                    canvas.Restore();
                    break;
                }
                case CropOverlay crop:
                {
                    var r = s.View.MapRect(crop.Rect.Standardized);
                    var doc = s.View.MapRect(SKRect.Create(s.DocWidth, s.DocHeight));
                    using var shade = new SKPaint { Color = SKColors.Black.WithAlpha(130) };
                    using (var outside = new SKPath { FillType = SKPathFillType.EvenOdd })
                    {
                        outside.AddRect(SKRect.Union(doc, r));
                        outside.AddRect(r);
                        canvas.DrawPath(outside, shade);
                    }
                    using var line = new SKPaint { Color = SKColors.White.WithAlpha(120), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
                    for (int i = 1; i < 3; i++)
                    {
                        canvas.DrawLine(r.Left + r.Width * i / 3, r.Top, r.Left + r.Width * i / 3, r.Bottom, line);
                        canvas.DrawLine(r.Left, r.Top + r.Height * i / 3, r.Right, r.Top + r.Height * i / 3, line);
                    }
                    line.Color = SKColors.White;
                    canvas.DrawRect(r, line);
                    DrawHandles(canvas, new[] { new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Top),
                        new SKPoint(r.Right, r.Bottom), new SKPoint(r.Left, r.Bottom) }, s.Accent);
                    break;
                }
                case TransformOverlay t:
                {
                    var pts = s.View.MapPoints(t.Corners);
                    using var path = new SKPath();
                    path.AddPoly(pts, true);
                    using var p = new SKPaint { Color = s.Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
                    canvas.DrawPath(path, p);
                    DrawHandles(canvas, pts, s.Accent);
                    break;
                }
                case MarkerOverlay m:
                {
                    var c = s.View.MapPoint(m.Pos);
                    using var p = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
                    canvas.DrawCircle(c, 7, p);
                    canvas.DrawLine(c.X - 11, c.Y, c.X + 11, c.Y, p);
                    canvas.DrawLine(c.X, c.Y - 11, c.X, c.Y + 11, p);
                    break;
                }
            }
        }

        /// <summary>Square handles at the corners and edge midpoints of a quad.</summary>
        private static void DrawHandles(SKCanvas canvas, SKPoint[] corners, SKColor accent)
        {
            var points = new List<SKPoint>(corners);
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                points.Add(new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            }
            using var fill = new SKPaint { Color = SKColors.White };
            using var stroke = new SKPaint { Color = accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            foreach (var p in points)
            {
                var r = new SKRect(p.X - 4, p.Y - 4, p.X + 4, p.Y + 4);
                canvas.DrawRect(r, fill);
                canvas.DrawRect(r, stroke);
            }
        }

        private static void DrawBrushCursor(SKCanvas canvas, OverlayState s)
        {
            if (s.Crosshair is { } ch)
            {
                var c = s.View.MapPoint(ch);
                using var p = new SKPaint { Color = SKColors.White, StrokeWidth = 1, BlendMode = SKBlendMode.Difference };
                canvas.DrawLine(c.X - 6, c.Y, c.X + 6, c.Y, p);
                canvas.DrawLine(c.X, c.Y - 6, c.X, c.Y + 6, p);
                return;
            }
            if (s.BrushCursor is not { } bc) return;
            var center = s.View.MapPoint(bc.pos);
            float r = bc.radius * s.Scale;
            using var paint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };
            paint.Color = SKColors.Black.WithAlpha(150);
            canvas.DrawCircle(center, r + 1, paint);
            paint.Color = SKColors.White.WithAlpha(220);
            canvas.DrawCircle(center, r, paint);
        }
    }
}
