using SkiaSharp;
using StrataPhoto.Core.Imaging;

namespace StrataPhoto.Core.Selections;

/// <summary>Colour-similarity regions for the magic wand and the fill bucket.</summary>
public static class FloodFill
{
    /// <summary>
    /// Alpha8 mask (255 = inside) of pixels whose colour is within <paramref name="tolerance"/>
    /// (0-255, per channel) of the seed pixel. Contiguous mode uses a scanline flood fill.
    /// </summary>
    public static SKBitmap ColorRegion(SKBitmap source, int seedX, int seedY, int tolerance, bool contiguous)
    {
        int w = source.Width, h = source.Height;
        var mask = Bitmaps.CreateAlpha(w, h);
        unsafe
        {
            uint* src = (uint*)source.GetPixels();
            byte* dst = (byte*)mask.GetPixels();
            int srcStride = source.RowBytes / 4, dstStride = mask.RowBytes;
            uint seed = src[(long)seedY * srcStride + seedX];

            if (!contiguous)
            {
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                        if (Similar(src[(long)y * srcStride + x], seed, tolerance))
                            dst[(long)y * dstStride + x] = 255;
                });
                return mask;
            }

            var stack = new Stack<(int x, int y)>();
            stack.Push((seedX, seedY));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                long rowSrc = (long)y * srcStride, rowDst = (long)y * dstStride;
                if (dst[rowDst + x] != 0 || !Similar(src[rowSrc + x], seed, tolerance)) continue;
                int left = x, right = x;
                while (left > 0 && dst[rowDst + left - 1] == 0 && Similar(src[rowSrc + left - 1], seed, tolerance)) left--;
                while (right < w - 1 && dst[rowDst + right + 1] == 0 && Similar(src[rowSrc + right + 1], seed, tolerance)) right++;
                for (int i = left; i <= right; i++) dst[rowDst + i] = 255;
                foreach (int ny in new[] { y - 1, y + 1 })
                {
                    if (ny < 0 || ny >= h) continue;
                    long nSrc = (long)ny * srcStride, nDst = (long)ny * dstStride;
                    bool inRun = false;
                    for (int i = left; i <= right; i++)
                    {
                        bool ok = dst[nDst + i] == 0 && Similar(src[nSrc + i], seed, tolerance);
                        if (ok && !inRun) { stack.Push((i, ny)); inRun = true; }
                        else if (!ok) inRun = false;
                    }
                }
            }
        }
        return mask;
    }

    private static bool Similar(uint a, uint b, int tol)
    {
        if (a == b) return true;
        int d0 = Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF));
        int d1 = Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF));
        int d2 = Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));
        int d3 = Math.Abs((int)(a >> 24) - (int)(b >> 24));
        return Math.Max(Math.Max(d0, d1), Math.Max(d2, d3)) <= tol;
    }
}
