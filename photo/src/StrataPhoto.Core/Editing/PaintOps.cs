using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Editing;

public enum GradientType { Linear, Radial }

public enum ShapeKind { Rectangle, Ellipse, Line }

/// <summary>Fill bucket, gradient and shape drawing.</summary>
public static class PaintOps
{
    /// <summary>Paint bucket: fills the similar-coloured region around (x, y).</summary>
    public static bool BucketFill(Document doc, int x, int y, SKColor color, int tolerance, bool contiguous,
        bool sampleAllLayers, float opacity = 1f)
    {
        if (!PixelOps.CanEdit(doc)) return false;
        var target = doc.EditTarget(out var layer, out bool isMask)!;
        if (x < 0 || y < 0 || x >= doc.Width || y >= doc.Height) return false;
        if (isMask) color = PixelOps.ToGray(color);

        SKBitmap sample = target;
        if (sampleAllLayers && !isMask) sample = Compositor.Flatten(doc.Root, doc.Width, doc.Height);
        var region = FloodFill.ColorRegion(sample, x, y, tolerance, contiguous);
        if (!ReferenceEquals(sample, target)) sample.Dispose();
        if (!doc.Selection.IsEmpty)
        {
            using var c = new SKCanvas(region);
            c.MaskIn(doc.Selection.Mask!, SKRect.Create(doc.Width, doc.Height));
        }
        var rect = Bitmaps.ContentBounds(region);
        if (rect.IsEmpty) { region.Dispose(); return false; }
        var before = Bitmaps.CopyRegion(target, rect);
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { Color = color.WithAlpha((byte)(color.Alpha * opacity)) })
            canvas.DrawPixels(region, 0, 0, paint);
        region.Dispose();
        doc.RecordPixels("Paint Bucket", layer!, isMask, rect, before);
        doc.Invalidate(rect);
        return true;
    }

    /// <summary>Draws a gradient from <paramref name="start"/> to <paramref name="end"/> into the selection.</summary>
    public static bool Gradient(Document doc, SKPoint start, SKPoint end, SKColor from, SKColor to,
        GradientType type, float opacity = 1f)
    {
        if (!PixelOps.CanEdit(doc)) return false;
        var target = doc.EditTarget(out var layer, out bool isMask)!;
        if (isMask) { from = PixelOps.ToGray(from); to = PixelOps.ToGray(to); }
        if (SKPoint.Distance(start, end) < 0.5f) return false;
        var rect = doc.Selection.EffectiveBounds(doc.Width, doc.Height);
        var before = Bitmaps.CopyRegion(target, rect);

        using var layerGradient = Bitmaps.Create(doc.Width, doc.Height);
        using (var gc = new SKCanvas(layerGradient))
        using (var paint = new SKPaint())
        {
            paint.Shader = type == GradientType.Linear
                ? SKShader.CreateLinearGradient(start, end, new[] { from, to }, null, SKShaderTileMode.Clamp)
                : SKShader.CreateRadialGradient(start, SKPoint.Distance(start, end), new[] { from, to }, null, SKShaderTileMode.Clamp);
            gc.ClipRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height));
            gc.DrawPaint(paint);
            if (!doc.Selection.IsEmpty)
                gc.MaskIn(doc.Selection.Mask!, SKRect.Create(doc.Width, doc.Height));
        }
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(opacity * 255)) })
        {
            var r = SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height);
            canvas.DrawPixels(layerGradient, r, r, paint);
        }
        doc.RecordPixels("Gradient", layer!, isMask, rect, before);
        doc.Invalidate(rect);
        return true;
    }

    /// <summary>Draws a rectangle, ellipse or line on a new layer named after the shape.</summary>
    public static PixelLayer? Shape(Document doc, ShapeKind kind, SKPoint a, SKPoint b, SKColor color,
        bool fill, float strokeWidth)
    {
        var bounds = new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        if (kind != ShapeKind.Line && (bounds.Width < 1 || bounds.Height < 1)) return null;
        if (kind == ShapeKind.Line && SKPoint.Distance(a, b) < 1) return null;
        var pixels = Bitmaps.Create(doc.Width, doc.Height);
        using (var canvas = new SKCanvas(pixels))
            DrawShape(canvas, kind, a, b, color, fill, strokeWidth);
        var name = doc.NextLayerName(kind.ToString());
        return LayerOps.AddLayerFromBitmap(doc, pixels, name, kind switch
        {
            ShapeKind.Rectangle => "Rectangle Tool",
            ShapeKind.Ellipse => "Ellipse Tool",
            _ => "Line Tool",
        });
    }

    public static void DrawShape(SKCanvas canvas, ShapeKind kind, SKPoint a, SKPoint b, SKColor color, bool fill, float strokeWidth)
    {
        var bounds = new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = fill && kind != ShapeKind.Line ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, strokeWidth),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Miter,
        };
        switch (kind)
        {
            case ShapeKind.Rectangle: canvas.DrawRect(bounds, paint); break;
            case ShapeKind.Ellipse: canvas.DrawOval(bounds, paint); break;
            default: canvas.DrawLine(a, b, paint); break;
        }
    }

    /// <summary>Colour under a point: the merged image or the active layer.</summary>
    public static SKColor SampleColor(Document doc, int x, int y, bool allLayers)
    {
        if (allLayers)
            lock (doc.Compositor.Sync)
                return Bitmaps.GetPixel(doc.Compositor.Composite, x, y);
        var px = doc.ActivePixelLayer?.Pixels;
        return px == null ? SKColors.Transparent : Bitmaps.GetPixel(px, x, y);
    }
}
