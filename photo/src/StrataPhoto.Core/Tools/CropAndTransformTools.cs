using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Imaging;

namespace StrataPhoto.Core.Tools;

/// <summary>Handle positions on a box: corners and edge midpoints, as (-1/0/1, -1/0/1).</summary>
public enum Handle { None, Inside, Outside, N, S, E, W, NE, NW, SE, SW }

internal static class Handles
{
    public static (int dx, int dy) Dir(Handle h) => h switch
    {
        Handle.N => (0, -1), Handle.S => (0, 1), Handle.E => (1, 0), Handle.W => (-1, 0),
        Handle.NE => (1, -1), Handle.NW => (-1, -1), Handle.SE => (1, 1), Handle.SW => (-1, 1),
        _ => (0, 0),
    };

    public static readonly Handle[] All = { Handle.NW, Handle.N, Handle.NE, Handle.E, Handle.SE, Handle.S, Handle.SW, Handle.W };
}

/// <summary>Crop: drag a box, adjust it with handles or move it, then Enter (or double-click) to crop.</summary>
public sealed class CropTool : Tool
{
    private SKRect? _rect;
    private Handle _drag = Handle.None;
    private SKPoint _start;
    private SKRect _startRect;

    public override ToolKind Kind => ToolKind.Crop;
    public override bool IsBusy => _rect != null;
    public SKRect? Rect => _rect;
    public override ToolOverlay? Overlay => _rect is { } r ? new CropOverlay(r) : null;

    public override void Down(ToolContext ctx, PointerInput p)
    {
        if (p.DoubleClick && _rect != null)
        {
            Commit(ctx);
            return;
        }
        _start = p.Pos;
        _drag = _rect is { } r ? HitTest(r, p.Pos, p.HandleRadius) : Handle.Outside;
        if (_drag == Handle.Outside)
        {
            _rect = new SKRect(p.Pos.X, p.Pos.Y, p.Pos.X, p.Pos.Y);
            _drag = Handle.SE;
        }
        _startRect = _rect!.Value;
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_drag == Handle.None || _rect == null) return;
        var d = p.Pos - _start;
        var r = _startRect;
        if (_drag == Handle.Inside)
        {
            r.Offset(d);
        }
        else
        {
            var (dx, dy) = Handles.Dir(_drag);
            if (dx < 0) r.Left += d.X; else if (dx > 0) r.Right += d.X;
            if (dy < 0) r.Top += d.Y; else if (dy > 0) r.Bottom += d.Y;
        }
        _rect = r;
    }

    public override void Up(ToolContext ctx, PointerInput p)
    {
        _drag = Handle.None;
        if (_rect is { } r)
        {
            r = r.Standardized;
            _rect = r.Width < 1 || r.Height < 1 ? null : r;
            if (_rect != null) ctx.Message = "Press Enter to crop, Esc to cancel.";
        }
    }

    public override void Commit(ToolContext ctx)
    {
        if (_rect is not { } r) return;
        _rect = null;
        var ri = Bitmaps.Intersect(new SKRectI((int)MathF.Round(r.Left), (int)MathF.Round(r.Top),
            (int)MathF.Round(r.Right), (int)MathF.Round(r.Bottom)), ctx.Doc.Bounds);
        if (!ri.IsEmpty) ImageTransforms.Crop(ctx.Doc, ri);
    }

    public override void Cancel(ToolContext ctx)
    {
        _rect = null;
        _drag = Handle.None;
    }

    public static Handle HitTest(SKRect r, SKPoint p, float radius)
    {
        foreach (var h in Handles.All)
        {
            var (dx, dy) = Handles.Dir(h);
            var hp = new SKPoint(r.MidX + dx * r.Width / 2, r.MidY + dy * r.Height / 2);
            if (SKPoint.Distance(hp, p) <= radius * 1.5f) return h;
        }
        return r.Contains(p) ? Handle.Inside : Handle.Outside;
    }
}

/// <summary>
/// Free Transform (Ctrl+T): drag inside to move, handles to scale (Shift = keep proportions),
/// outside to rotate (Shift = 15° steps). Enter applies, Esc cancels.
/// </summary>
public sealed class FreeTransformTool : Tool
{
    private TransformSession? _session;
    private SKPoint _c0;              // source centre
    private float _srcW, _srcH;
    private SKPoint _offset;          // centre translation
    private float _angle;             // degrees
    private float _sx = 1, _sy = 1;

    private Handle _drag = Handle.None;
    private SKPoint _dragStart;
    private SKPoint _offset0;
    private float _angle0, _sx0, _sy0;

    public override ToolKind Kind => ToolKind.Move; // not on the tools strip; shows the move cursor
    public override bool IsBusy => _session != null;
    public bool IsActive => _session != null;
    public override ToolOverlay? Overlay => _session == null ? null : new TransformOverlay(_session.Corners);

    public bool Begin(ToolContext ctx)
    {
        _session = TransformSession.Begin(ctx.Doc);
        if (_session == null)
        {
            ctx.Message = "Nothing to transform on this layer.";
            return false;
        }
        var b = _session.SourceBounds;
        _c0 = new SKPoint(b.MidX, b.MidY);
        _srcW = Math.Max(1, b.Width);
        _srcH = Math.Max(1, b.Height);
        _offset = default;
        _angle = 0;
        _sx = _sy = 1;
        ctx.Message = "Free Transform: drag handles to scale, outside to rotate. Enter to apply, Esc to cancel.";
        return true;
    }

    private SKPoint Center => _c0 + _offset;

    private SKMatrix BuildMatrix() =>
        SKMatrix.CreateTranslation(-_c0.X, -_c0.Y)
            .PostConcat(SKMatrix.CreateScale(_sx, _sy))
            .PostConcat(SKMatrix.CreateRotationDegrees(_angle))
            .PostConcat(SKMatrix.CreateTranslation(Center.X, Center.Y));

    public Handle HitTest(SKPoint p, float radius)
    {
        if (_session == null) return Handle.None;
        var m = BuildMatrix();
        var b = _session.SourceBounds;
        foreach (var h in Handles.All)
        {
            var (dx, dy) = Handles.Dir(h);
            var hp = m.MapPoint(new SKPoint(_c0.X + dx * b.Width / 2, _c0.Y + dy * b.Height / 2));
            if (SKPoint.Distance(hp, p) <= radius * 1.5f) return h;
        }
        var local = ToLocal(p);
        return Math.Abs(local.X) <= Math.Abs(_srcW * _sx) / 2 && Math.Abs(local.Y) <= Math.Abs(_srcH * _sy) / 2
            ? Handle.Inside : Handle.Outside;
    }

    /// <summary>Point in the box's unrotated frame, relative to its centre.</summary>
    private SKPoint ToLocal(SKPoint p)
    {
        var d = p - Center;
        double a = -_angle * Math.PI / 180;
        return new SKPoint((float)(d.X * Math.Cos(a) - d.Y * Math.Sin(a)), (float)(d.X * Math.Sin(a) + d.Y * Math.Cos(a)));
    }

    private SKPoint FromLocal(SKPoint l)
    {
        double a = _angle * Math.PI / 180;
        return Center + new SKPoint((float)(l.X * Math.Cos(a) - l.Y * Math.Sin(a)), (float)(l.X * Math.Sin(a) + l.Y * Math.Cos(a)));
    }

    public override void Down(ToolContext ctx, PointerInput p)
    {
        if (_session == null) return;
        if (p.DoubleClick)
        {
            Commit(ctx);
            return;
        }
        _drag = HitTest(p.Pos, p.HandleRadius);
        _dragStart = p.Pos;
        _offset0 = _offset;
        _angle0 = _angle;
        _sx0 = _sx;
        _sy0 = _sy;
    }

    public override void Move(ToolContext ctx, PointerInput p)
    {
        if (_session == null || _drag == Handle.None) return;
        switch (_drag)
        {
            case Handle.Inside:
                _offset = _offset0 + (p.Pos - _dragStart);
                break;
            case Handle.Outside:
            {
                var c = Center;
                double a0 = Math.Atan2(_dragStart.Y - c.Y, _dragStart.X - c.X);
                double a1 = Math.Atan2(p.Pos.Y - c.Y, p.Pos.X - c.X);
                float ang = _angle0 + (float)((a1 - a0) * 180 / Math.PI);
                if (p.Shift) ang = MathF.Round(ang / 15) * 15;
                _angle = ang;
                break;
            }
            default:
                ScaleFromHandle(p);
                break;
        }
        _session.SetMatrix(BuildMatrix());
    }

    private void ScaleFromHandle(PointerInput p)
    {
        var (dx, dy) = Handles.Dir(_drag);
        // Restore the state at drag start, then keep the opposite handle fixed.
        _offset = _offset0; _sx = _sx0; _sy = _sy0; _angle = _angle0;
        float hw = _srcW * _sx0 / 2, hh = _srcH * _sy0 / 2;
        var anchorLocal = new SKPoint(-dx * hw, -dy * hh);
        var anchorWorld = FromLocal(anchorLocal);
        var pl = ToLocal(p.Pos);
        float nsx = _sx0, nsy = _sy0;
        if (dx != 0) nsx = (pl.X - anchorLocal.X) / (dx * _srcW);
        if (dy != 0) nsy = (pl.Y - anchorLocal.Y) / (dy * _srcH);
        if (p.Shift && dx != 0 && dy != 0)
        {
            float k = Math.Max(Math.Abs(nsx / _sx0), Math.Abs(nsy / _sy0));
            nsx = _sx0 * k * Math.Sign(nsx == 0 ? 1 : nsx);
            nsy = _sy0 * k * Math.Sign(nsy == 0 ? 1 : nsy);
        }
        if (Math.Abs(nsx * _srcW) < 1) nsx = Math.Sign(nsx == 0 ? 1 : nsx) / _srcW;
        if (Math.Abs(nsy * _srcH) < 1) nsy = Math.Sign(nsy == 0 ? 1 : nsy) / _srcH;
        _sx = nsx;
        _sy = nsy;
        // New centre: anchor + half the new extent towards the dragged handle, in the rotated frame.
        var halfLocal = new SKPoint(dx * _srcW * _sx / 2, dy * _srcH * _sy / 2);
        double a = _angle * Math.PI / 180;
        var halfWorld = new SKPoint((float)(halfLocal.X * Math.Cos(a) - halfLocal.Y * Math.Sin(a)),
            (float)(halfLocal.X * Math.Sin(a) + halfLocal.Y * Math.Cos(a)));
        var newCenter = anchorWorld + halfWorld;
        _offset = newCenter - _c0;
    }

    public override void Up(ToolContext ctx, PointerInput p) => _drag = Handle.None;

    public override void Commit(ToolContext ctx)
    {
        _session?.Commit("Free Transform");
        _session = null;
        _drag = Handle.None;
    }

    public override void Cancel(ToolContext ctx)
    {
        _session?.Cancel();
        _session = null;
        _drag = Handle.None;
    }
}
