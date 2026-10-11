using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.Imaging;

/// <summary>
/// Flattens the layer tree into <see cref="Composite"/>. Only the requested region is recomputed,
/// which keeps brush strokes cheap on large images. Readers on other threads (the GPU tile
/// uploader in the UI) must hold <see cref="Sync"/> while reading the composite.
/// </summary>
public sealed class Compositor : IDisposable
{
    private readonly List<SKBitmap> _scratch = new(); // one temp bitmap per group-nesting depth

    public Compositor(int width, int height)
    {
        Composite = Bitmaps.Create(width, height);
    }

    public object Sync { get; } = new();
    public SKBitmap Composite { get; private set; }

    public void Resize(int width, int height)
    {
        lock (Sync)
        {
            Composite.Dispose();
            Composite = Bitmaps.Create(width, height);
            ClearScratch();
        }
    }

    /// <summary>Recomposites <paramref name="region"/> (document coordinates).</summary>
    public void Update(GroupLayer root, SKRectI region)
    {
        region = Bitmaps.Intersect(region, Bitmaps.Bounds(Composite));
        if (region.IsEmpty) return;
        lock (Sync)
        {
            using var canvas = new SKCanvas(Composite);
            canvas.ClipRect(SKRect.Create(region.Left, region.Top, region.Width, region.Height));
            canvas.Clear(SKColors.Transparent);
            RenderGroup(canvas, root, region, 0);
        }
    }

    /// <summary>Renders the layer tree into a new bitmap (used for export and sampling without locking).</summary>
    public static SKBitmap Flatten(GroupLayer root, int width, int height)
    {
        using var comp = new Compositor(width, height);
        comp.Update(root, new SKRectI(0, 0, width, height));
        var result = comp.Composite;
        comp.Composite = Bitmaps.Create(1, 1);
        return result;
    }

    /// <summary>Renders a group's children into a bitmap (for "Sample all layers" and merging).</summary>
    public static SKBitmap RenderLayers(IEnumerable<Layer> layers, int width, int height)
    {
        var group = new GroupLayer("tmp");
        group.Children.AddRange(layers); // no reparenting: read-only use
        using var comp = new Compositor(width, height);
        comp.Update(group, new SKRectI(0, 0, width, height));
        var result = comp.Composite;
        comp.Composite = Bitmaps.Create(1, 1);
        return result;
    }

    private void RenderGroup(SKCanvas canvas, GroupLayer group, SKRectI region, int depth)
    {
        foreach (var layer in group.Children)
        {
            if (!layer.Visible || layer.Opacity <= 0) continue;
            switch (layer)
            {
                case GroupLayer g:
                    RenderIsolated(canvas, g, region, depth, c => RenderGroup(c, g, region, depth + 1));
                    break;
                case PixelLayer p:
                {
                    var pixels = p.PreviewPixels ?? p.Pixels;
                    if (p.Mask != null && p.MaskEnabled)
                    {
                        RenderIsolated(canvas, p, region, depth, c => DrawRegion(c, pixels, region, null));
                    }
                    else
                    {
                        using var paint = LayerPaint(p);
                        DrawRegion(canvas, pixels, region, paint);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Renders content into a scratch layer, applies the layer mask, then blends it down.</summary>
    private void RenderIsolated(SKCanvas canvas, Layer layer, SKRectI region, int depth, Action<SKCanvas> draw)
    {
        var scratch = Scratch(depth, region.Width, region.Height);
        using (var sc = new SKCanvas(scratch))
        {
            sc.ClipRect(new SKRect(0, 0, region.Width, region.Height));
            sc.Clear(SKColors.Transparent);
            sc.Translate(-region.Left, -region.Top);
            draw(sc);
            if (layer.Mask != null && layer.MaskEnabled)
            {
                using var maskPaint = new SKPaint
                {
                    BlendMode = SKBlendMode.DstIn,
                    ColorFilter = SKColorFilter.CreateLumaColor(),
                };
                DrawRegion(sc, layer.Mask, region, maskPaint);
            }
        }
        using var paint = LayerPaint(layer);
        var src = new SKRect(0, 0, region.Width, region.Height);
        var dst = SKRect.Create(region.Left, region.Top, region.Width, region.Height);
        canvas.DrawPixels(scratch, src, dst, paint);
    }

    private static void DrawRegion(SKCanvas canvas, SKBitmap bmp, SKRectI region, SKPaint? paint)
    {
        var r = SKRect.Create(region.Left, region.Top, region.Width, region.Height);
        canvas.DrawPixels(bmp, r, r, paint);
    }

    private static SKPaint LayerPaint(Layer layer) => new()
    {
        BlendMode = layer.BlendMode.ToSkia(),
        Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)),
        FilterQuality = SKFilterQuality.None,
    };

    private SKBitmap Scratch(int depth, int w, int h)
    {
        while (_scratch.Count <= depth) _scratch.Add(Bitmaps.Create(1, 1));
        var s = _scratch[depth];
        if (s.Width < w || s.Height < h)
        {
            int nw = Math.Max(w, s.Width), nh = Math.Max(h, s.Height);
            s.Dispose();
            s = Bitmaps.Create(nw, nh);
            _scratch[depth] = s;
        }
        return s;
    }

    private void ClearScratch()
    {
        foreach (var s in _scratch) s.Dispose();
        _scratch.Clear();
    }

    public void Dispose()
    {
        lock (Sync)
        {
            Composite.Dispose();
            ClearScratch();
        }
    }
}
