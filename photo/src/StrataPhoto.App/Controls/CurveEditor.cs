using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using Strata.Ui;
using StrataPhoto.Core.Operations;

namespace StrataPhoto.App.Controls;

/// <summary>
/// Tone curve editor for the Curves adjustment: click to add a point, drag to move it, drag a
/// point out of the box (or right-click it) to remove it.
/// </summary>
public sealed class CurveEditor : Control
{
    private List<SKPoint> _points = Curves.Identity.ToList();
    private int _drag = -1;

    public CurveEditor()
    {
        MinHeight = 180;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public IReadOnlyList<SKPoint> Points
    {
        get => _points;
        set
        {
            _points = value.OrderBy(p => p.X).ToList();
            InvalidateVisual();
        }
    }

    public event Action<IReadOnlyList<SKPoint>>? PointsChanged;

    private double Side => Math.Min(Bounds.Width, Bounds.Height);
    private Point ToView(SKPoint p) => new(p.X / 255 * Side, (1 - p.Y / 255) * Side);
    private SKPoint ToCurve(Point v) => new((float)Math.Clamp(v.X / Side * 255, 0, 255), (float)Math.Clamp((1 - v.Y / Side) * 255, 0, 255));

    public override void Render(DrawingContext ctx)
    {
        double s = Side;
        var box = new Rect(0, 0, s, s);
        ctx.FillRectangle(new SolidColorBrush(SharedTheme.Get("bgInput")), box);
        var grid = new Pen(new SolidColorBrush(SharedTheme.Get("border")), 1);
        for (int i = 1; i < 4; i++)
        {
            ctx.DrawLine(grid, new Point(s * i / 4, 0), new Point(s * i / 4, s));
            ctx.DrawLine(grid, new Point(0, s * i / 4), new Point(s, s * i / 4));
        }
        ctx.DrawLine(new Pen(new SolidColorBrush(SharedTheme.Get("textDisabled")), 1), new Point(0, s), new Point(s, 0));
        var lut = Curves.Lut(_points);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, (1 - lut[0] / 255.0) * s), false);
            for (int x = 1; x < 256; x++) g.LineTo(new Point(x / 255.0 * s, (1 - lut[x] / 255.0) * s));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(SharedTheme.Get("text")), 1.5), geo);
        foreach (var p in _points)
        {
            var v = ToView(p);
            ctx.DrawRectangle(new SolidColorBrush(SharedTheme.Get("accent")), new Pen(Brushes.White, 1), new Rect(v.X - 4, v.Y - 4, 8, 8));
        }
        ctx.DrawRectangle(null, grid, box);
    }

    private int HitPoint(Point v) => _points.FindIndex(p => Math.Abs(ToView(p).X - v.X) < 7 && Math.Abs(ToView(p).Y - v.Y) < 7);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var v = e.GetPosition(this);
        int hit = HitPoint(v);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (hit > 0 && hit < _points.Count - 1) RemoveAt(hit);
            return;
        }
        if (hit < 0)
        {
            var c = ToCurve(v);
            _points.Add(c);
            _points = _points.OrderBy(p => p.X).ToList();
            hit = _points.IndexOf(c);
            Changed();
        }
        _drag = hit;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        var v = e.GetPosition(this);
        var c = ToCurve(v);
        bool endPoint = _drag == 0 || _drag == _points.Count - 1;
        // Keep the points in x order; end points may only move vertically... and a little inwards.
        float minX = _drag == 0 ? 0 : _points[_drag - 1].X + 1;
        float maxX = _drag == _points.Count - 1 ? 255 : _points[_drag + 1].X - 1;
        if (!endPoint && (v.Y < -20 || v.Y > Side + 20))
        {
            RemoveAt(_drag);
            _drag = -1;
            return;
        }
        _points[_drag] = new SKPoint(Math.Clamp(c.X, minX, maxX), c.Y);
        Changed();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = -1;
        e.Pointer.Capture(null);
    }

    private void RemoveAt(int i)
    {
        _points.RemoveAt(i);
        Changed();
    }

    private void Changed()
    {
        InvalidateVisual();
        PointsChanged?.Invoke(_points.ToArray());
    }
}
