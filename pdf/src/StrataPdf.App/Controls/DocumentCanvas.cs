using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Strata.Ui;
using StrataPdf.App.ViewModels;
using StrataPdf.Core.IO;
using StrataPdf.Core.Model;
using StrataPdf.Core.Rendering;

namespace StrataPdf.App.Controls;

/// <summary>
/// All pages stacked vertically inside a ScrollViewer. Only visible pages are rendered (in the
/// background, at the current zoom); annotations, stamp previews and tool feedback are drawn on top.
/// </summary>
public sealed class DocumentCanvas : Control
{
    private const double Gap = 18, PageMargin = 24;
    private const double PointToDip = 96.0 / 72.0;

    private MainViewModel? _vm;
    private readonly Dictionary<string, Bitmap> _pages = new();
    private readonly HashSet<string> _pending = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<Guid, Bitmap> _images = new();
    private List<(PageItem Page, Rect Rect)> _layout = new();

    // interaction
    private PageItem? _dragPage;
    private PagePoint _dragStart, _dragNow;   // base coordinates
    private List<PagePoint>? _ink;
    private Annotation? _moving;
    private bool _dragging, _panning, _spaceHeld;
    private Point _panStart;
    private Vector _panOffset;

    public DocumentCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    private double Dpp => (_vm?.Zoom ?? 1) * PointToDip;  // DIPs per PDF point
    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    private ScrollViewer? Scroller => this.FindAncestorOfType<ScrollViewer>();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.DocumentChanged -= OnDocumentChanged;
            _vm.PropertyChanged -= OnVmChanged;
            _vm.ScrollToPageRequested -= ScrollToPage;
        }
        _vm = DataContext as MainViewModel;
        if (_vm != null)
        {
            _vm.DocumentChanged += OnDocumentChanged;
            _vm.PropertyChanged += OnVmChanged;
            _vm.ScrollToPageRequested += ScrollToPage;
            _vm.FitWidthAction = FitWidth;
            _vm.FitPageAction = FitPage;
        }
        OnDocumentChanged();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Scroller is { } sv)
            sv.ScrollChanged += (_, _) =>
            {
                InvalidateVisual();
                UpdateCurrentPage();
            };
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Zoom):
                ZoomKeepingCenter();
                break;
            case nameof(MainViewModel.Selection):
            case nameof(MainViewModel.Tool):
                UpdateCursor();
                InvalidateVisual();
                break;
        }
    }

    private void OnDocumentChanged()
    {
        if (_vm?.Workspace == null)
        {
            foreach (var b in _pages.Values) b.Dispose();
            _pages.Clear();
            _lru.Clear();
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout(double availableWidth)
    {
        _layout = new();
        var ws = _vm?.Workspace;
        if (ws == null) return;
        double dpp = Dpp;
        double maxW = ws.Pages.Count == 0 ? 0 : ws.Pages.Max(p => p.Width) * dpp;
        double width = Math.Max(availableWidth, maxW + 2 * PageMargin);
        double y = PageMargin;
        foreach (var p in ws.Pages)
        {
            double w = p.Width * dpp, h = p.Height * dpp;
            _layout.Add((p, new Rect((width - w) / 2, y, w, h)));
            y += h + Gap;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double vw = Scroller?.Viewport.Width ?? 0;
        if (double.IsInfinity(vw) || vw <= 0) vw = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        BuildLayout(vw);
        if (_layout.Count == 0) return new Size(0, 0);
        double w = _layout.Max(l => l.Rect.Right) + PageMargin;
        double h = _layout[^1].Rect.Bottom + PageMargin;
        return new Size(Math.Max(w, vw), h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        BuildLayout(finalSize.Width);
        return finalSize;
    }

    private Rect Viewport
    {
        get
        {
            var sv = Scroller;
            return sv == null ? new Rect(Bounds.Size) : new Rect(new Point(sv.Offset.X, sv.Offset.Y), sv.Viewport);
        }
    }

    private void UpdateCurrentPage()
    {
        if (_vm == null || _layout.Count == 0) return;
        var vp = Viewport;
        double mid = vp.Top + vp.Height / 3;
        int best = 0;
        for (int i = 0; i < _layout.Count; i++)
            if (_layout[i].Rect.Top <= mid) best = i;
        if (_vm.CurrentPage != best) _vm.CurrentPage = best;
    }

    public void ScrollToPage(int index)
    {
        if (index < 0 || index >= _layout.Count || Scroller is not { } sv) return;
        sv.Offset = new Vector(sv.Offset.X, Math.Max(0, _layout[index].Rect.Top - Gap));
    }

    private void ZoomKeepingCenter()
    {
        if (Scroller is not { } sv || _layout.Count == 0)
        {
            InvalidateMeasure();
            return;
        }
        // keep the same document position at the top of the view
        int page = _vm!.CurrentPage;
        double frac = 0;
        if (page < _layout.Count)
        {
            var r = _layout[page].Rect;
            frac = (sv.Offset.Y - r.Top) / Math.Max(1, r.Height);
        }
        InvalidateMeasure();
        Dispatcher.UIThread.Post(() =>
        {
            if (page < _layout.Count)
            {
                var r = _layout[page].Rect;
                sv.Offset = new Vector(sv.Offset.X, Math.Max(0, r.Top + frac * r.Height));
            }
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private void FitWidth()
    {
        var ws = _vm?.Workspace;
        if (ws == null || Scroller is not { } sv || ws.Pages.Count == 0) return;
        double w = ws.Pages.Max(p => p.Width);
        _vm!.Zoom = Math.Clamp((sv.Viewport.Width - 2 * PageMargin - 20) / (w * PointToDip), 0.1, 8);
    }

    private void FitPage()
    {
        var ws = _vm?.Workspace;
        if (ws == null || Scroller is not { } sv || ws.Pages.Count == 0) return;
        var p = ws.Pages[Math.Min(_vm!.CurrentPage, ws.Pages.Count - 1)];
        double z = Math.Min((sv.Viewport.Width - 2 * PageMargin) / (p.Width * PointToDip), (sv.Viewport.Height - 2 * Gap) / (p.Height * PointToDip));
        _vm.Zoom = Math.Clamp(z, 0.1, 8);
        Dispatcher.UIThread.Post(() => ScrollToPage(_vm.CurrentPage), DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ page bitmaps

    private static string KeyFor(PageItem p, double scale) => $"{p.SourceId}|{p.SourceIndex}|{p.Rotation}|{scale:0.###}";

    /// <summary>Render scale (pixels per point) bucketed so small zoom changes reuse bitmaps.</summary>
    private double Bucket(double pixelsPerPoint)
    {
        double step = Math.Pow(1.25, Math.Ceiling(Math.Log(pixelsPerPoint) / Math.Log(1.25)));
        return Math.Min(step, 6);
    }

    private Bitmap? PageBitmap(PageItem p, double pixelsPerPoint)
    {
        double scale = Bucket(pixelsPerPoint);
        string key = KeyFor(p, scale);
        if (_pages.TryGetValue(key, out var bmp))
        {
            _lru.Remove(key);
            _lru.AddFirst(key);
            return bmp;
        }
        Request(p, scale, key);
        // meanwhile show any other resolution we already have
        string prefix = $"{p.SourceId}|{p.SourceIndex}|{p.Rotation}|";
        return _pages.FirstOrDefault(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Value;
    }

    private void Request(PageItem p, double scale, string key)
    {
        if (_vm?.Workspace is not { } ws || !_pending.Add(key)) return;
        string path = ws.SourceOf(p).Path;
        Task.Run(() =>
        {
            try
            {
                using var sk = PdfRenderer.RenderPage(path, p.SourceIndex, scale, p.Rotation);
                return (Bitmap?)BitmapConvert.ToAvalonia(sk);
            }
            catch
            {
                return null;
            }
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _pending.Remove(key);
            if (t.Result == null) return;
            _pages[key] = t.Result;
            _lru.AddFirst(key);
            while (_lru.Count > 24)
            {
                var old = _lru.Last!.Value;
                _lru.RemoveLast();
                if (_pages.Remove(old, out var b)) b.Dispose();
            }
            InvalidateVisual();
        }));
    }

    private Bitmap? ImageFor(ImageAnnotation a)
    {
        if (!_images.TryGetValue(a.Id, out var b))
        {
            try { b = BitmapConvert.FromPng(a.Png); }
            catch { return null; }
            _images[a.Id] = b;
        }
        return b;
    }

    // ------------------------------------------------------------------ coordinates

    private (PageItem Page, Rect Rect)? HitPage(Point p)
    {
        foreach (var l in _layout)
            if (l.Rect.Inflate(8).Contains(p)) return l;
        return null;
    }

    private PagePoint ToBase(PageItem page, Rect rect, Point p)
    {
        double dpp = Dpp;
        double sx = Math.Clamp((p.X - rect.X) / dpp, 0, page.Width), sy = Math.Clamp((p.Y - rect.Y) / dpp, 0, page.Height);
        return PageGeometry.ToBase(page, sx, sy);
    }

    /// <summary>Base page space → canvas DIPs.</summary>
    private Matrix BaseToView(PageItem page, Rect rect)
    {
        Matrix rot = (((page.Rotation % 360) + 360) % 360) switch
        {
            90 => new Matrix(0, 1, -1, 0, page.BaseHeight, 0),
            180 => new Matrix(-1, 0, 0, -1, page.BaseWidth, page.BaseHeight),
            270 => new Matrix(0, -1, 1, 0, 0, page.BaseWidth),
            _ => Matrix.Identity,
        };
        return rot * Matrix.CreateScale(Dpp, Dpp) * Matrix.CreateTranslation(rect.X, rect.Y);
    }

    // ------------------------------------------------------------------ rendering

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(SharedTheme.Get("canvasBg")), new Rect(Bounds.Size));
        var ws = _vm?.Workspace;
        if (ws == null)
        {
            var hint = new FormattedText("Open a PDF (Ctrl+O) or drop one here", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default), 14, new SolidColorBrush(SharedTheme.Get("textDim")));
            var vp0 = Viewport;
            ctx.DrawText(hint, new Point(vp0.X + (vp0.Width - hint.Width) / 2, vp0.Y + vp0.Height / 2 - 10));
            return;
        }
        var vp = Viewport.Inflate(200);
        double pxPerPt = Dpp * RenderScaling;
        var shadow = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
        for (int i = 0; i < _layout.Count; i++)
        {
            var (page, rect) = _layout[i];
            if (!rect.Intersects(vp)) continue;
            ctx.FillRectangle(shadow, rect.Translate(new Vector(2, 3)));
            ctx.FillRectangle(Brushes.White, rect);
            var bmp = PageBitmap(page, pxPerPt);
            if (bmp != null)
                ctx.DrawImage(bmp, new Rect(bmp.Size), rect);
            using (ctx.PushClip(rect))
            {
                using (ctx.PushTransform(BaseToView(page, rect)))
                {
                    foreach (var a in page.Annotations) DrawAnnotation(ctx, a);
                    if (_moving != null && _dragPage?.Id == page.Id) DrawAnnotation(ctx, _moving, preview: true);
                    if (_dragPage?.Id == page.Id) DrawToolPreview(ctx);
                    if (_vm!.Selection is { } s && s.PageId == page.Id && _moving == null
                        && page.Annotations.FirstOrDefault(a => a.Id == s.AnnotationId) is { } sel)
                    {
                        var b = sel.Bounds.Inflate(3);
                        var pen = new Pen(new SolidColorBrush(SharedTheme.Get("accent")), 1.2 / Dpp, new DashStyle(new double[] { 4, 3 }, 0));
                        ctx.DrawRectangle(null, pen, new Rect(b.X, b.Y, b.Width, b.Height));
                    }
                }
                using (ctx.PushTransform(Matrix.CreateScale(Dpp, Dpp) * Matrix.CreateTranslation(rect.X, rect.Y)))
                    DrawStamps(ctx, ws, i, page);
            }
            if (i == _vm!.CurrentPage)
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(SharedTheme.Get("accent"), 0.5), 1), rect.Inflate(1));
        }
    }

    private static IBrush Brush(Rgba c) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.Opacity * 255), c.R, c.G, c.B));

    private void DrawAnnotation(DrawingContext ctx, Annotation a, bool preview = false)
    {
        using var _ = ctx.PushOpacity(preview ? 0.7 : 1);
        switch (a)
        {
            case HighlightAnnotation h:
                ctx.FillRectangle(Brush(h.Color), R(h.Rect));
                break;
            case InkAnnotation ink when ink.Points.Count > 1:
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(ink.Points[0].X, ink.Points[0].Y), false);
                    foreach (var p in ink.Points.Skip(1)) g.LineTo(new Point(p.X, p.Y));
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(Brush(ink.Color), ink.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
                break;
            }
            case TextBoxAnnotation t:
            {
                if (t.Border) ctx.DrawRectangle(null, new Pen(Brush(t.Color), 0.8), R(t.Rect));
                var ft = new FormattedText(t.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(t.FontFamily), t.FontSize, Brush(t.Color))
                {
                    MaxTextWidth = Math.Max(1, t.Rect.Width - 4),
                };
                ctx.DrawText(ft, new Point(t.Rect.X + 2, t.Rect.Y + 1));
                break;
            }
            case ShapeAnnotation s:
                DrawShape(ctx, s);
                break;
            case NoteAnnotation n:
            {
                var r = R(n.Bounds);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 214, 0)), new Pen(new SolidColorBrush(Color.FromRgb(150, 110, 0)), 1), r, 3, 3);
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 90, 0)), 1.4);
                for (int i = 0; i < 3; i++)
                    ctx.DrawLine(pen, new Point(r.X + 5, r.Y + 7 + i * 4.5), new Point(r.Right - 5 - (i == 2 ? 5 : 0), r.Y + 7 + i * 4.5));
                break;
            }
            case ImageAnnotation img:
                if (ImageFor(img) is { } b) ctx.DrawImage(b, new Rect(b.Size), R(img.Rect));
                break;
            case RedactionAnnotation red:
            {
                ctx.FillRectangle(Brushes.Black, R(red.Rect));
                ctx.DrawRectangle(null, new Pen(Brushes.Red, 1, new DashStyle(new double[] { 3, 2 }, 0)), R(red.Rect));
                break;
            }
        }
    }

    private static Rect R(PageRect r) => new(r.X, r.Y, Math.Max(0.1, r.Width), Math.Max(0.1, r.Height));

    private static void DrawShape(DrawingContext ctx, ShapeAnnotation s)
    {
        var pen = new Pen(Brush(s.Color), s.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var fill = s.Filled ? Brush(s.Color) : null;
        var r = R(PageRect.FromPoints(s.From, s.To));
        switch (s.Kind)
        {
            case ShapeKind.Rectangle:
                ctx.DrawRectangle(fill, pen, r);
                break;
            case ShapeKind.Ellipse:
                ctx.DrawEllipse(fill, pen, r.Center, r.Width / 2, r.Height / 2);
                break;
            default:
                ctx.DrawLine(pen, new Point(s.From.X, s.From.Y), new Point(s.To.X, s.To.Y));
                if (s.Kind == ShapeKind.Arrow)
                {
                    var head = PdfSaver.ArrowHead(s.From, s.To, s.Width);
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(head[0].X, head[0].Y), true);
                        g.LineTo(new Point(head[1].X, head[1].Y));
                        g.LineTo(new Point(head[2].X, head[2].Y));
                        g.EndFigure(true);
                    }
                    ctx.DrawGeometry(Brush(s.Color), null, geo);
                }
                break;
        }
    }

    private void DrawStamps(DrawingContext ctx, PdfWorkspace ws, int index, PageItem page)
    {
        var wm = ws.Watermark;
        if (wm.Enabled && !string.IsNullOrWhiteSpace(wm.Text))
        {
            var ft = new FormattedText(wm.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.Bold), wm.FontSize, Brush(wm.Color));
            using (ctx.PushTransform(Matrix.CreateTranslation(-ft.Width / 2, -ft.Height / 2)
                                     * Matrix.CreateRotation(wm.Angle * Math.PI / 180)
                                     * Matrix.CreateTranslation(page.Width / 2, page.Height / 2)))
                ctx.DrawText(ft, new Point(0, 0));
        }
        var pn = ws.PageNumbers;
        if (pn.AppliesTo(index))
        {
            var ft = new FormattedText(pn.TextFor(index, ws.Pages.Count), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Arial"), pn.FontSize, Brush(pn.Color));
            var (x, y) = PdfSaver.StampOrigin(pn.Position, page.Width, page.Height, ft.Width, ft.Height, pn.Margin);
            ctx.DrawText(ft, new Point(x, y));
        }
    }

    private void DrawToolPreview(DrawingContext ctx)
    {
        if (_vm == null || !_dragging) return;
        var r = R(PageRect.FromPoints(_dragStart, _dragNow));
        double w = 1.2 / Dpp;
        switch (_vm.Tool)
        {
            case PdfTool.Pen when _ink is { Count: > 1 }:
                DrawAnnotation(ctx, new InkAnnotation(_ink, _vm.StrokeColor, _vm.StrokeWidth), true);
                break;
            case PdfTool.Highlight:
                ctx.FillRectangle(Brush(_vm.HighlightColor), r);
                break;
            case PdfTool.Rectangle or PdfTool.Ellipse or PdfTool.Arrow or PdfTool.Line:
                DrawShape(ctx, NewShape());
                break;
            case PdfTool.Redact:
                ctx.FillRectangle(new SolidColorBrush(Colors.Black, 0.6), r);
                break;
            case PdfTool.Text or PdfTool.Signature or PdfTool.Zoom:
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(SharedTheme.Get("accent")), w, new DashStyle(new double[] { 4, 3 }, 0)), r);
                break;
        }
    }

    private ShapeAnnotation NewShape() => new(
        _vm!.Tool switch { PdfTool.Rectangle => ShapeKind.Rectangle, PdfTool.Ellipse => ShapeKind.Ellipse, PdfTool.Arrow => ShapeKind.Arrow, _ => ShapeKind.Line },
        _dragStart, _dragNow, _vm.StrokeColor, _vm.StrokeWidth, _vm.FillShapes);

    // ------------------------------------------------------------------ input

    public void SetSpaceHeld(bool held)
    {
        _spaceHeld = held;
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        Cursor = new Cursor(_spaceHeld || _vm?.Tool == PdfTool.Hand ? StandardCursorType.Hand
            : _vm?.Tool switch
            {
                PdfTool.Select => StandardCursorType.Arrow,
                PdfTool.Text => StandardCursorType.Ibeam,
                PdfTool.Zoom => StandardCursorType.Cross,
                _ => StandardCursorType.Cross,
            });
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_vm?.Workspace is not { } ws) return;
        var pt = e.GetCurrentPoint(this);
        var pos = pt.Position;
        if (pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed && (_spaceHeld || _vm.Tool == PdfTool.Hand)))
        {
            _panning = true;
            _panStart = e.GetPosition(Scroller);
            _panOffset = Scroller?.Offset ?? default;
            e.Pointer.Capture(this);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        var hit = HitPage(pos);
        if (hit == null)
        {
            _vm.Selection = null;
            return;
        }
        var (page, rect) = hit.Value;
        _vm.CurrentPage = ws.IndexOf(page.Id);
        var b = ToBase(page, rect, pos);

        if (_vm.Tool == PdfTool.Zoom)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) _vm.ZoomOutCommand.Execute(null);
            else _vm.ZoomInCommand.Execute(null);
            return;
        }
        if (_vm.Tool == PdfTool.Select)
        {
            var target = page.Annotations.LastOrDefault(a => a.Bounds.Inflate(4).Contains(b));
            if (target == null)
            {
                _vm.Selection = null;
                return;
            }
            _vm.Selection = (page.Id, target.Id);
            if (e.ClickCount >= 2)
            {
                _ = _vm.EditAnnotationTextAsync(page.Id, target);
                return;
            }
            _moving = target;
        }
        if (_vm.Tool == PdfTool.Note)
        {
            _ = AddNoteAsync(page, b);
            return;
        }
        _dragPage = page;
        _dragStart = _dragNow = b;
        _dragging = true;
        _ink = _vm.Tool == PdfTool.Pen ? new List<PagePoint> { b } : null;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panning && Scroller is { } sv)
        {
            var p = e.GetPosition(sv);
            sv.Offset = _panOffset - (p - _panStart);
            return;
        }
        if (!_dragging || _dragPage == null) return;
        var rect = _layout.First(l => l.Page.Id == _dragPage.Id).Rect;
        foreach (var ip in e.GetIntermediatePoints(this))
        {
            _dragNow = ToBase(_dragPage, rect, ip.Position);
            _ink?.Add(_dragNow);
        }
        if (_moving != null && _vm?.Selection is { } s)
        {
            var orig = _dragPage.Annotations.First(a => a.Id == s.AnnotationId);
            _moving = orig.Moved(_dragNow.X - _dragStart.X, _dragNow.Y - _dragStart.Y);
        }
        InvalidateVisual();
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            return;
        }
        if (!_dragging || _dragPage == null || _vm?.Workspace is not { } ws) return;
        _dragging = false;
        e.Pointer.Capture(null);
        var page = _dragPage;
        var r = PageRect.FromPoints(_dragStart, _dragNow);
        bool isClick = r.Width < 3 && r.Height < 3;
        var moved = _moving;
        _moving = null;
        _dragPage = null;
        try
        {
            switch (_vm.Tool)
            {
                case PdfTool.Select when moved != null && !isClick:
                    ws.ReplaceAnnotation(page.Id, moved, "Move " + moved.Describe());
                    break;
                case PdfTool.Highlight when !isClick:
                    Add(ws, page, new HighlightAnnotation(r, _vm.HighlightColor));
                    break;
                case PdfTool.Pen when _ink is { Count: > 1 }:
                    Add(ws, page, new InkAnnotation(Simplify(_ink), _vm.StrokeColor, _vm.StrokeWidth));
                    break;
                case PdfTool.Rectangle or PdfTool.Ellipse or PdfTool.Arrow or PdfTool.Line when !isClick:
                    Add(ws, page, NewShape());
                    break;
                case PdfTool.Redact when !isClick:
                    Add(ws, page, new RedactionAnnotation(r));
                    _vm.StatusMessage = "Redaction added: the content under it is permanently removed when you save.";
                    break;
                case PdfTool.Text:
                {
                    var text = await _vm.Dialogs.AskTextAsync("Add Text", "Text", "", true);
                    if (string.IsNullOrWhiteSpace(text)) break;
                    var box = isClick ? new PageRect(_dragStart.X, _dragStart.Y, Math.Min(260, page.BaseWidth - _dragStart.X), _vm.FontSize * 1.6 * Math.Max(1, text.Split('\n').Length)) : r;
                    Add(ws, page, new TextBoxAnnotation(box, text, _vm.FontSize, _vm.StrokeColor, _vm.FontFamily));
                    break;
                }
                case PdfTool.Signature:
                {
                    if (_vm.SignaturePng == null)
                    {
                        await _vm.EditSignatureCommand.ExecuteAsync(null);
                        if (_vm.SignaturePng == null) break;
                    }
                    using var probe = SkiaSharp.SKBitmap.Decode(_vm.SignaturePng);
                    double aspect = probe.Height / (double)Math.Max(1, probe.Width);
                    var box = isClick || r.Width < 10 ? new PageRect(_dragStart.X, _dragStart.Y, 150, 150 * aspect) : new PageRect(r.X, r.Y, r.Width, r.Width * aspect);
                    Add(ws, page, new ImageAnnotation(box, _vm.SignaturePng));
                    break;
                }
            }
        }
        finally
        {
            _ink = null;
            InvalidateVisual();
        }
    }

    private void Add(PdfWorkspace ws, PageItem page, Annotation a)
    {
        ws.AddAnnotation(page.Id, a);
        _vm!.Selection = (page.Id, a.Id);
    }

    private async Task AddNoteAsync(PageItem page, PagePoint at)
    {
        var text = await _vm!.Dialogs.AskTextAsync("Sticky Note", "Note", "", true);
        if (!string.IsNullOrWhiteSpace(text)) Add(_vm.Workspace!, page, new NoteAnnotation(at, text));
    }

    /// <summary>Drops points closer than 0.5 pt to the previous one.</summary>
    private static List<PagePoint> Simplify(List<PagePoint> pts)
    {
        var o = new List<PagePoint> { pts[0] };
        foreach (var p in pts.Skip(1))
            if (Math.Abs(p.X - o[^1].X) + Math.Abs(p.Y - o[^1].Y) > 0.5) o.Add(p);
        return o;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_vm != null && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm.Zoom = Math.Clamp(_vm.Zoom * Math.Pow(1.15, e.Delta.Y), 0.1, 8);
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }

    /// <summary>Canvas position of a point in a page's base space (self-test, scrolling to annotations).</summary>
    internal Point ViewPoint(int pageIndex, PagePoint b)
    {
        var (page, rect) = _layout[pageIndex];
        var s = PageGeometry.ToShown(page, b.X, b.Y);
        return new Point(rect.X + s.X * Dpp, rect.Y + s.Y * Dpp);
    }
}
