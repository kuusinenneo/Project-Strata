using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.Painting;

public enum StrokeMode { Paint, Erase, Clone }

/// <summary>
/// One brush stroke from pointer down to pointer up.
///
/// Dabs accumulate (with flow) into an Alpha8 coverage buffer. After each batch of dabs only the
/// touched rectangle of the target is rebuilt: base pixels + colour × coverage × opacity. This
/// gives Photoshop's opacity/flow behaviour (opacity caps the whole stroke) and keeps the cost
/// proportional to the brush size, not the image size. The undo patch covers just the union of
/// touched rectangles.
/// </summary>
public sealed class StrokeSession : IDisposable
{
    [ThreadStatic] private static SKBitmap? _sharedCoverage;

    private readonly Document _doc;
    private readonly Layer _layer;
    private readonly bool _mask;
    private readonly SKBitmap _target;
    private readonly SKBitmap _base;      // target as it was when the stroke started
    private readonly SKBitmap _coverage;  // stroke coverage (Alpha8, document size)
    private readonly BrushSettings _brush;
    private readonly StrokeMode _mode;
    private readonly SKColor _color;
    private readonly SKPointI _cloneOffset;
    private readonly string _name;

    private SKPoint _last;
    private float _lastPressure;
    private float _carry;  // distance travelled since the last dab
    private SKRectI _dirty = SKRectI.Empty;
    private bool _ended;

    private StrokeSession(Document doc, Layer layer, bool mask, SKBitmap target, BrushSettings brush,
        StrokeMode mode, SKColor color, SKPointI cloneOffset, string name)
    {
        _doc = doc;
        _layer = layer;
        _mask = mask;
        _target = target;
        _brush = brush;
        _mode = mode;
        _color = mask ? PixelOps.ToGray(color) : color;
        _cloneOffset = cloneOffset;
        _name = name;
        _base = Bitmaps.Copy(target);
        _coverage = RentCoverage(target.Width, target.Height);
    }

    /// <summary>Starts a stroke on the document's edit target, or returns null if it can't be edited.</summary>
    /// <param name="cloneOffset">Clone stamp: source = destination + offset.</param>
    public static StrokeSession? Begin(Document doc, BrushSettings brush, StrokeMode mode, SKColor color,
        SKPoint start, float pressure, SKPointI cloneOffset = default)
    {
        if (!PixelOps.CanEdit(doc)) return null;
        var target = doc.EditTarget(out var layer, out bool mask);
        if (target == null || layer == null) return null;
        // Erasing a mask paints black (hide); erasing pixels removes alpha.
        var name = mode switch { StrokeMode.Erase => "Eraser", StrokeMode.Clone => "Clone Stamp", _ => "Brush" };
        if (mask && mode == StrokeMode.Erase)
        {
            mode = StrokeMode.Paint;
            color = SKColors.Black;
        }
        var s = new StrokeSession(doc, layer, mask, target, brush, mode, color, cloneOffset, name);
        s._last = start;
        s._lastPressure = pressure;
        s.Dab(start, pressure);
        s.Flush();
        return s;
    }

    public SKRectI DirtyBounds => _dirty;

    /// <summary>Continues the stroke to <paramref name="to"/>, placing dabs along the way.</summary>
    public void MoveTo(SKPoint to, float pressure) => MoveTo(new[] { (to, pressure) });

    /// <summary>Adds several points (e.g. coalesced pointer events) and updates the image once.</summary>
    public void MoveTo(IEnumerable<(SKPoint pos, float pressure)> points)
    {
        if (_ended) return;
        foreach (var (pos, pressure) in points)
        {
            var d = pos - _last;
            float dist = d.Length;
            if (dist <= 0.0001f) continue;
            float t = 0;
            while (true)
            {
                float spacing = Math.Max(1f, DabDiameter(Lerp(_lastPressure, pressure, t)) * _brush.Spacing);
                float need = spacing - _carry;
                if (t * dist + need > dist) break;
                t += need / dist;
                _carry = 0;
                Dab(_last + new SKPoint(d.X * t, d.Y * t), Lerp(_lastPressure, pressure, t));
            }
            _carry += dist * (1 - t);
            _last = pos;
            _lastPressure = pressure;
        }
        Flush();
    }

    private SKRectI _pending = SKRectI.Empty;

    private float DabDiameter(float pressure) =>
        Math.Max(1f, _brush.Size * (_brush.PressureSize ? Math.Clamp(pressure, 0.05f, 1f) : 1f));

    private void Dab(SKPoint p, float pressure)
    {
        float radius = DabDiameter(pressure) / 2f;
        float flow = _brush.Flow * (_brush.PressureOpacity ? Math.Clamp(pressure, 0f, 1f) : 1f);
        byte a = (byte)Math.Clamp(MathF.Round(flow * 255), 1, 255);
        using var canvas = new SKCanvas(_coverage);
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.SrcOver };
        float hard = _brush.Hardness;
        if (hard >= 0.99f || radius < 1.5f)
        {
            paint.Color = SKColors.Black.WithAlpha(a);
            canvas.DrawCircle(p, radius, paint);
        }
        else
        {
            var c = SKColors.Black.WithAlpha(a);
            paint.Shader = SKShader.CreateRadialGradient(p, radius,
                new[] { c, c, SKColors.Black.WithAlpha(0) },
                new[] { 0f, hard, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawCircle(p, radius, paint);
        }
        var r = Bitmaps.RoundOut(new SKRect(p.X - radius - 1, p.Y - radius - 1, p.X + radius + 1, p.Y + radius + 1));
        _pending = Bitmaps.Union(_pending, Bitmaps.Intersect(r, Bitmaps.Bounds(_target)));
    }

    /// <summary>Rebuilds the pending rectangle of the target from base + coverage, then recomposites it.</summary>
    private void Flush()
    {
        var r = _pending;
        _pending = SKRectI.Empty;
        if (r.IsEmpty) return;
        var rf = SKRect.Create(r.Left, r.Top, r.Width, r.Height);

        // Limit coverage to the selection.
        var sel = _doc.Selection;
        if (!sel.IsEmpty)
        {
            using var cc = new SKCanvas(_coverage);
            cc.MaskIn(sel.Mask!, rf);
        }

        using (var canvas = new SKCanvas(_target))
        {
            canvas.ClipRect(rf);
            using (var copy = new SKPaint { BlendMode = SKBlendMode.Src })
                canvas.DrawPixels(_base, rf, rf, copy);
            byte alpha = (byte)Math.Round(_brush.Opacity * 255);
            using var paint = new SKPaint();
            switch (_mode)
            {
                case StrokeMode.Paint:
                    paint.Color = _color.WithAlpha((byte)(_color.Alpha * alpha / 255));
                    break;
                case StrokeMode.Erase:
                    paint.Color = SKColors.Black.WithAlpha(alpha);
                    paint.BlendMode = SKBlendMode.DstOut;
                    break;
                case StrokeMode.Clone:
                    paint.Color = SKColors.Black.WithAlpha(alpha);
                    paint.Shader = _base.ToPixelShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal,
                        SKMatrix.CreateTranslation(-_cloneOffset.X, -_cloneOffset.Y));
                    break;
            }
            // An Alpha8 bitmap is drawn with the paint's colour/shader, modulated by its coverage.
            canvas.DrawPixels(_coverage, rf, rf, paint);
        }
        _dirty = Bitmaps.Union(_dirty, r);
        _doc.Invalidate(r);
    }

    /// <summary>Finishes the stroke and records it in history.</summary>
    public void End()
    {
        if (_ended) return;
        _ended = true;
        if (!_dirty.IsEmpty)
        {
            var before = Bitmaps.CopyRegion(_base, _dirty);
            _doc.RecordPixels(_name, _layer, _mask, _dirty, before);
        }
        Dispose();
    }

    /// <summary>Abandons the stroke and restores the original pixels.</summary>
    public void Cancel()
    {
        if (_ended) return;
        _ended = true;
        if (!_dirty.IsEmpty)
        {
            Bitmaps.CopyRect(_base, _target, _dirty);
            _doc.Invalidate(_dirty);
        }
        Dispose();
    }

    public void Dispose()
    {
        _base.Dispose();
        ReturnCoverage(_coverage, _dirty);
    }

    private static SKBitmap RentCoverage(int w, int h)
    {
        var c = _sharedCoverage;
        _sharedCoverage = null;
        if (c == null || c.Width != w || c.Height != h)
        {
            c?.Dispose();
            c = Bitmaps.CreateAlpha(w, h);
        }
        return c;
    }

    /// <summary>Keeps the coverage buffer for the next stroke; only the used area needs clearing.</summary>
    private static void ReturnCoverage(SKBitmap c, SKRectI used)
    {
        if (!used.IsEmpty)
        {
            using var canvas = new SKCanvas(c);
            canvas.ClipRect(SKRect.Create(used.Left, used.Top, used.Width, used.Height));
            canvas.Clear(SKColors.Transparent);
        }
        if (_sharedCoverage == null) _sharedCoverage = c;
        else c.Dispose();
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
