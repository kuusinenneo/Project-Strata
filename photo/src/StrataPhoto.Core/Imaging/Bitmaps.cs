using SkiaSharp;

namespace StrataPhoto.Core.Imaging;

/// <summary>
/// Helpers for the two bitmap kinds Strata Photo uses: premultiplied BGRA (layers, masks, the
/// composite) and Alpha8 (selections, stroke coverage).
/// </summary>
public static class Bitmaps
{
    public static SKImageInfo ColorInfo(int w, int h) => new(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
    public static SKImageInfo AlphaInfo(int w, int h) => new(w, h, SKColorType.Alpha8, SKAlphaType.Premul);

    public static SKBitmap Create(int w, int h, SKColor? fill = null)
    {
        var bmp = new SKBitmap(ColorInfo(Math.Max(1, w), Math.Max(1, h)));
        bmp.Erase(fill ?? SKColors.Transparent);
        return bmp;
    }

    public static SKBitmap CreateAlpha(int w, int h, byte value = 0)
    {
        var bmp = new SKBitmap(AlphaInfo(Math.Max(1, w), Math.Max(1, h)));
        unsafe { new Span<byte>((void*)bmp.GetPixels(), bmp.RowBytes * bmp.Height).Fill(value); }
        return bmp;
    }

    public static SKBitmap Copy(SKBitmap src)
    {
        var dst = new SKBitmap(src.Info);
        unsafe
        {
            Buffer.MemoryCopy((void*)src.GetPixels(), (void*)dst.GetPixels(), (long)dst.RowBytes * dst.Height,
                (long)src.RowBytes * src.Height);
        }
        return dst;
    }

    /// <summary>Copy of <paramref name="rect"/> (must lie inside the bitmap) as a new bitmap.</summary>
    public static SKBitmap CopyRegion(SKBitmap src, SKRectI rect)
    {
        var dst = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, src.ColorType, src.AlphaType));
        int bpp = src.BytesPerPixel;
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            for (int y = 0; y < rect.Height; y++)
                Buffer.MemoryCopy(s + (long)(rect.Top + y) * src.RowBytes + rect.Left * bpp, d + (long)y * dst.RowBytes,
                    dst.RowBytes, rect.Width * bpp);
        }
        return dst;
    }

    /// <summary>Writes <paramref name="patch"/> into <paramref name="dst"/> at the rect's position (plain copy).</summary>
    public static void WriteRegion(SKBitmap dst, SKRectI rect, SKBitmap patch)
    {
        int bpp = dst.BytesPerPixel;
        unsafe
        {
            byte* s = (byte*)patch.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            for (int y = 0; y < rect.Height; y++)
                Buffer.MemoryCopy(s + (long)y * patch.RowBytes, d + (long)(rect.Top + y) * dst.RowBytes + rect.Left * bpp,
                    rect.Width * bpp, rect.Width * bpp);
        }
    }

    /// <summary>Copies a region between two same-sized bitmaps.</summary>
    public static void CopyRect(SKBitmap src, SKBitmap dst, SKRectI rect)
    {
        int bpp = dst.BytesPerPixel;
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            for (int y = rect.Top; y < rect.Bottom; y++)
                Buffer.MemoryCopy(s + (long)y * src.RowBytes + rect.Left * bpp, d + (long)y * dst.RowBytes + rect.Left * bpp,
                    rect.Width * bpp, rect.Width * bpp);
        }
    }

    public static SKRectI Bounds(SKBitmap bmp) => new(0, 0, bmp.Width, bmp.Height);

    public static SKRectI Intersect(SKRectI a, SKRectI b)
    {
        var r = SKRectI.Intersect(a, b);
        return r.Width <= 0 || r.Height <= 0 ? SKRectI.Empty : r;
    }

    public static SKRectI Union(SKRectI a, SKRectI b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return SKRectI.Union(a, b);
    }

    /// <summary>Integer rect that fully covers a float rect.</summary>
    public static SKRectI RoundOut(SKRect r) =>
        new((int)MathF.Floor(r.Left), (int)MathF.Floor(r.Top), (int)MathF.Ceiling(r.Right), (int)MathF.Ceiling(r.Bottom));

    /// <summary>Bounding box of pixels with alpha &gt; 0 (empty rect if fully transparent).</summary>
    public static SKRectI ContentBounds(SKBitmap bmp)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        unsafe
        {
            byte* p = (byte*)bmp.GetPixels();
            bool alphaOnly = bmp.ColorType == SKColorType.Alpha8;
            int bpp = bmp.BytesPerPixel;
            int aOff = alphaOnly ? 0 : 3;
            for (int y = 0; y < bmp.Height; y++)
            {
                byte* row = p + (long)y * bmp.RowBytes;
                int first = -1, last = -1;
                for (int x = 0; x < bmp.Width; x++)
                    if (row[x * bpp + aOff] != 0) { first = x; break; }
                if (first < 0) continue;
                for (int x = bmp.Width - 1; x >= first; x--)
                    if (row[x * bpp + aOff] != 0) { last = x; break; }
                minY = Math.Min(minY, y); maxY = y;
                minX = Math.Min(minX, first); maxX = Math.Max(maxX, last);
            }
        }
        return maxY < 0 ? SKRectI.Empty : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    public static SKColor GetPixel(SKBitmap bmp, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return SKColors.Transparent;
        return bmp.GetPixel(x, y); // SkiaSharp un-premultiplies
    }

    public static unsafe Span<uint> Row(SKBitmap bmp, int y) =>
        new((byte*)bmp.GetPixels() + (long)y * bmp.RowBytes, bmp.Width);

    public static unsafe Span<byte> AlphaRow(SKBitmap bmp, int y) =>
        new((byte*)bmp.GetPixels() + (long)y * bmp.RowBytes, bmp.Width);

    public static long ByteSize(SKBitmap? bmp) => bmp == null ? 0 : (long)bmp.RowBytes * bmp.Height;
}
