using SkiaSharp;
using StrataPhoto.Core.Imaging;

namespace StrataPhoto.Core.Selections;

/// <summary>Creating and combining selections. All methods return new <see cref="Selection"/> objects.</summary>
public static class SelectionOps
{
    public static Selection All(int w, int h)
    {
        var mask = Bitmaps.CreateAlpha(w, h, 255);
        return new Selection(mask, new SKRectI(0, 0, w, h));
    }

    public static Selection Rectangle(int w, int h, SKRect rect, Selection? current = null,
        SelectionMode mode = SelectionMode.Replace, float feather = 0) =>
        FromShape(w, h, c => { using var p = Fill(false); c.DrawRect(Normalize(rect), p); }, current, mode, feather);

    public static Selection Ellipse(int w, int h, SKRect rect, Selection? current = null,
        SelectionMode mode = SelectionMode.Replace, float feather = 0) =>
        FromShape(w, h, c => { using var p = Fill(true); c.DrawOval(Normalize(rect), p); }, current, mode, feather);

    public static Selection Polygon(int w, int h, IReadOnlyList<SKPoint> points, Selection? current = null,
        SelectionMode mode = SelectionMode.Replace, float feather = 0)
    {
        if (points.Count < 3) return current ?? Selection.None;
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddPoly(points.ToArray(), close: true);
        return FromShape(w, h, c => { using var p = Fill(true); c.DrawPath(path, p); }, current, mode, feather);
    }

    /// <summary>Draws a shape into a fresh mask and combines it with the current selection.</summary>
    public static Selection FromShape(int w, int h, Action<SKCanvas> draw, Selection? current,
        SelectionMode mode, float feather)
    {
        var mask = Bitmaps.CreateAlpha(w, h);
        using (var canvas = new SKCanvas(mask))
            draw(canvas);
        if (feather > 0)
        {
            var feathered = FeatherMask(mask, feather);
            mask.Dispose();
            mask = feathered;
        }
        return Combine(current ?? Selection.None, mask, mode, w, h);
    }

    /// <summary>Combines a new coverage mask (ownership taken) with an existing selection.</summary>
    public static Selection Combine(Selection current, SKBitmap newMask, SelectionMode mode, int w, int h)
    {
        if (mode == SelectionMode.Replace || current.IsEmpty)
        {
            if (mode is SelectionMode.Subtract or SelectionMode.Intersect && current.IsEmpty)
            {
                newMask.Dispose();
                return Selection.None;
            }
            return Selection.FromMask(newMask);
        }
        var result = Bitmaps.Copy(current.Mask!);
        using (var canvas = new SKCanvas(result))
        {
            if (mode == SelectionMode.Intersect)
            {
                canvas.MaskIn(newMask, SKRect.Create(w, h));
            }
            else
            {
                using var paint = new SKPaint { BlendMode = mode == SelectionMode.Add ? SKBlendMode.Plus : SKBlendMode.DstOut };
                canvas.DrawPixels(newMask, 0, 0, paint);
            }
        }
        newMask.Dispose();
        return Selection.FromMask(result);
    }

    public static Selection Invert(Selection current, int w, int h)
    {
        if (current.IsEmpty) return All(w, h);
        var mask = Bitmaps.CreateAlpha(w, h);
        int rb = mask.RowBytes, srb = current.Mask!.RowBytes;
        unsafe
        {
            byte* d = (byte*)mask.GetPixels();
            byte* s = (byte*)current.Mask.GetPixels();
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                    d[(long)y * rb + x] = (byte)(255 - s[(long)y * srb + x]);
            });
        }
        return Selection.FromMask(mask);
    }

    public static Selection Feather(Selection current, float radius)
    {
        if (current.IsEmpty || radius <= 0) return current;
        return Selection.FromMask(FeatherMask(current.Mask!, radius));
    }

    /// <summary>Gaussian blur of a coverage mask (radius ~ 2 sigma, like common editors).</summary>
    public static SKBitmap FeatherMask(SKBitmap mask, float radius)
    {
        var result = Bitmaps.CreateAlpha(mask.Width, mask.Height);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(radius / 2f, radius / 2f, SKShaderTileMode.Decal) };
        canvas.DrawPixels(mask, 0, 0, paint);
        return result;
    }

    /// <summary>Magic wand: selects pixels of similar colour to the one at (x, y).</summary>
    public static Selection MagicWand(SKBitmap source, int x, int y, int tolerance, bool contiguous,
        bool antiAlias, Selection? current = null, SelectionMode mode = SelectionMode.Replace)
    {
        int w = source.Width, h = source.Height;
        if (x < 0 || y < 0 || x >= w || y >= h) return current ?? Selection.None;
        var mask = FloodFill.ColorRegion(source, x, y, tolerance, contiguous);
        if (antiAlias)
        {
            var soft = FeatherMask(mask, 1.0f);
            mask.Dispose();
            mask = soft;
        }
        return Combine(current ?? Selection.None, mask, mode, w, h);
    }

    /// <summary>Selection covering the non-transparent pixels of a bitmap (Ctrl+click a layer thumbnail).</summary>
    public static Selection FromAlpha(SKBitmap pixels)
    {
        int w = pixels.Width, h = pixels.Height;
        var mask = Bitmaps.CreateAlpha(w, h);
        unsafe
        {
            byte* d = (byte*)mask.GetPixels();
            byte* s = (byte*)pixels.GetPixels();
            int rb = mask.RowBytes, srb = pixels.RowBytes;
            Parallel.For(0, h, yy =>
            {
                for (int xx = 0; xx < w; xx++)
                    d[(long)yy * rb + xx] = s[(long)yy * srb + xx * 4 + 3];
            });
        }
        return Selection.FromMask(mask);
    }

    /// <summary>Resamples a selection to a new document size (Image Size).</summary>
    public static Selection Transform(Selection current, int newW, int newH, SKMatrix matrix)
    {
        if (current.IsEmpty) return current;
        var mask = Bitmaps.CreateAlpha(newW, newH);
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true })
        {
            canvas.SetMatrix(matrix);
            canvas.DrawPixels(current.Mask!, 0, 0, paint);
        }
        return Selection.FromMask(mask);
    }

    private static SKRect Normalize(SKRect r) => SKRect.Create(Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom),
        Math.Abs(r.Width), Math.Abs(r.Height));

    private static SKPaint Fill(bool antiAlias) => new()
    {
        Color = SKColors.White,
        Style = SKPaintStyle.Fill,
        IsAntialias = antiAlias,
    };
}
