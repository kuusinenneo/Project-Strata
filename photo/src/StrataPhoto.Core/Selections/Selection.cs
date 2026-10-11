using SkiaSharp;
using StrataPhoto.Core.Imaging;

namespace StrataPhoto.Core.Selections;

public enum SelectionMode { Replace, Add, Subtract, Intersect }

/// <summary>
/// An immutable selection: a document-sized Alpha8 coverage mask (255 = fully selected) plus its
/// bounds. <see cref="None"/> means "nothing selected", which editing operations treat as
/// "the whole layer".
/// </summary>
public sealed class Selection
{
    public static readonly Selection None = new(null, SKRectI.Empty);

    private SKPath? _outline;

    internal Selection(SKBitmap? mask, SKRectI bounds)
    {
        Mask = mask;
        Bounds = bounds;
    }

    public SKBitmap? Mask { get; }
    public SKRectI Bounds { get; }
    public bool IsEmpty => Mask == null || Bounds.IsEmpty;

    /// <summary>Creates a selection from a mask (takes ownership). Returns <see cref="None"/> if the mask is empty.</summary>
    public static Selection FromMask(SKBitmap mask)
    {
        var bounds = Bitmaps.ContentBounds(mask);
        if (bounds.IsEmpty)
        {
            mask.Dispose();
            return None;
        }
        return new Selection(mask, bounds);
    }

    /// <summary>Coverage 0..255 at a pixel.</summary>
    public byte CoverageAt(int x, int y)
    {
        if (Mask == null || x < 0 || y < 0 || x >= Mask.Width || y >= Mask.Height) return 0;
        unsafe { return ((byte*)Mask.GetPixels())[(long)y * Mask.RowBytes + x]; }
    }

    /// <summary>Outline in document coordinates (pixel-boundary edges where coverage crosses 50%).</summary>
    public SKPath Outline => _outline ??= Mask == null ? new SKPath() : SelectionOutline.Build(Mask, Bounds);

    /// <summary>The area editing should affect: the selection bounds, or the whole document.</summary>
    public SKRectI EffectiveBounds(int docWidth, int docHeight) =>
        IsEmpty ? new SKRectI(0, 0, docWidth, docHeight) : Bounds;
}

/// <summary>Builds the marching-ants outline of a selection mask.</summary>
internal static class SelectionOutline
{
    public static SKPath Build(SKBitmap mask, SKRectI bounds)
    {
        var path = new SKPath();
        int w = mask.Width, h = mask.Height;
        var r = new SKRectI(Math.Max(0, bounds.Left - 1), Math.Max(0, bounds.Top - 1),
            Math.Min(w, bounds.Right + 1), Math.Min(h, bounds.Bottom + 1));
        unsafe
        {
            byte* p = (byte*)mask.GetPixels();
            int rb = mask.RowBytes;
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && p[(long)y * rb + x] >= 128;

            // Horizontal edges between row y-1 and y, merged into runs.
            for (int y = r.Top; y <= r.Bottom; y++)
            {
                int runStart = -1;
                for (int x = r.Left; x <= r.Right; x++)
                {
                    bool edge = x < r.Right && In(x, y) != In(x, y - 1);
                    if (edge && runStart < 0) runStart = x;
                    else if (!edge && runStart >= 0)
                    {
                        path.MoveTo(runStart, y);
                        path.LineTo(x, y);
                        runStart = -1;
                    }
                }
            }
            // Vertical edges between column x-1 and x.
            for (int x = r.Left; x <= r.Right; x++)
            {
                int runStart = -1;
                for (int y = r.Top; y <= r.Bottom; y++)
                {
                    bool edge = y < r.Bottom && In(x, y) != In(x - 1, y);
                    if (edge && runStart < 0) runStart = y;
                    else if (!edge && runStart >= 0)
                    {
                        path.MoveTo(x, runStart);
                        path.LineTo(x, y);
                        runStart = -1;
                    }
                }
            }
        }
        return path;
    }
}
