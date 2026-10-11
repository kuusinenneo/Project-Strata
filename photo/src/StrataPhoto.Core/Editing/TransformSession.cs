using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Editing;

/// <summary>
/// Move tool drags and Free Transform (Ctrl+T). The selected pixels (or the whole layer) are
/// lifted into a floating bitmap and previewed through <see cref="PixelLayer.PreviewPixels"/>
/// while the matrix changes; <see cref="Commit"/> bakes the result as one undo step.
/// </summary>
public sealed class TransformSession : IDisposable
{
    private readonly Document _doc;
    private readonly PixelLayer _layer;
    private readonly SKBitmap _hole;      // layer pixels with the lifted area removed
    private readonly SKBitmap _floating;  // lifted pixels (document-sized, transparent elsewhere)
    private readonly SKBitmap _preview;
    private readonly Selection _selection;
    private SKRectI _shown;               // area of the preview that currently differs from _hole
    private bool _done;

    private TransformSession(Document doc, PixelLayer layer, SKRect bounds)
    {
        _doc = doc;
        _layer = layer;
        _selection = doc.Selection;
        SourceBounds = bounds;
        var src = layer.Pixels;
        if (_selection.IsEmpty)
        {
            _floating = Bitmaps.Copy(src);
            _hole = Bitmaps.Create(src.Width, src.Height);
        }
        else
        {
            _floating = Bitmaps.Copy(src);
            using (var c = new SKCanvas(_floating))
                c.MaskIn(_selection.Mask!, SKRect.Create(src.Width, src.Height));
            _hole = Bitmaps.Copy(src);
            using (var c = new SKCanvas(_hole))
            using (var p = new SKPaint { BlendMode = SKBlendMode.DstOut })
                c.DrawPixels(_selection.Mask!, 0, 0, p);
        }
        _preview = Bitmaps.Copy(src);
        _shown = Bitmaps.RoundOut(bounds);
        _layer.PreviewPixels = _preview;
    }

    /// <summary>Starts a transform of the active layer (or its selected pixels). Null if not possible.</summary>
    public static TransformSession? Begin(Document doc)
    {
        if (doc.ActiveLayer is not PixelLayer layer || layer.Locked || !layer.Visible) return null;
        SKRectI bounds = doc.Selection.IsEmpty
            ? Bitmaps.ContentBounds(layer.Pixels)
            : Bitmaps.Intersect(doc.Selection.Bounds, Bitmaps.ContentBounds(layer.Pixels));
        if (bounds.IsEmpty) return null;
        return new TransformSession(doc, layer, SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }

    public PixelLayer Layer => _layer;

    /// <summary>Untransformed bounds of the lifted content.</summary>
    public SKRect SourceBounds { get; }

    public SKMatrix Matrix { get; private set; } = SKMatrix.Identity;

    /// <summary>The four transformed corners (TL, TR, BR, BL) for drawing handles.</summary>
    public SKPoint[] Corners
    {
        get
        {
            var b = SourceBounds;
            return Matrix.MapPoints(new[] { new SKPoint(b.Left, b.Top), new SKPoint(b.Right, b.Top),
                new SKPoint(b.Right, b.Bottom), new SKPoint(b.Left, b.Bottom) });
        }
    }

    public bool IsIdentity => Matrix.Equals(SKMatrix.Identity);

    public void SetMatrix(SKMatrix m)
    {
        if (_done) return;
        Matrix = m;
        var newBounds = Bitmaps.Intersect(Bitmaps.RoundOut(m.MapRect(SourceBounds)).Inflate1(), _doc.Bounds);
        var region = Bitmaps.Union(_shown, newBounds);
        region = Bitmaps.Intersect(region, _doc.Bounds);
        if (region.IsEmpty) return;
        Bitmaps.CopyRect(_hole, _preview, region);
        using (var canvas = new SKCanvas(_preview))
        {
            canvas.ClipRect(SKRect.Create(region.Left, region.Top, region.Width, region.Height));
            canvas.SetMatrix(m);
            using var paint = new SKPaint
            {
                FilterQuality = IsTranslationOnly(m) ? SKFilterQuality.None : SKFilterQuality.High,
                IsAntialias = true,
            };
            canvas.DrawPixels(_floating, 0, 0, paint);
        }
        _shown = newBounds;
        _doc.Invalidate(region);
    }

    public void Commit(string name)
    {
        if (_done) return;
        _done = true;
        _layer.PreviewPixels = null;
        if (IsIdentity)
        {
            _doc.Invalidate(_shown);
            Dispose();
            return;
        }
        var m = Matrix;
        if (_layer is TextLayer text && _selection.IsEmpty && IsTranslationOnly(m))
        {
            var props = text.Text with { X = text.Text.X + m.TransX, Y = text.Text.Y + m.TransY };
            LayerOps.EditText(_doc, text, props, mergeKey: null);
            Dispose();
            return;
        }
        var pixels = Bitmaps.Copy(_preview);
        SKBitmap? mask = null;
        if (_layer.Mask != null && _selection.IsEmpty)
            mask = ImageTransforms.TransformBitmap(_layer.Mask, _doc.Width, _doc.Height, m,
                IsTranslationOnly(m) ? SKFilterQuality.None : SKFilterQuality.High, SKColors.White);
        var selection = _selection.IsEmpty ? _selection
            : SelectionOps.Transform(_selection, _doc.Width, _doc.Height, m);
        _doc.Commit(name, () =>
        {
            if (_layer is TextLayer t)
            {
                // Text can't hold arbitrary transforms: replace it with a pixel layer.
                var pixel = new PixelLayer(t.Name, pixels) { Opacity = t.Opacity, BlendMode = t.BlendMode, Mask = mask ?? t.Mask };
                var parent = t.Parent!;
                int i = parent.Children.IndexOf(t);
                parent.Remove(t);
                parent.Insert(i, pixel);
                _doc.ActiveLayer = pixel;
            }
            else
            {
                _layer.Pixels = pixels;
                if (mask != null) _layer.Mask = mask;
            }
            _doc.Selection = selection;
        });
        Dispose();
    }

    public void Cancel()
    {
        if (_done) return;
        _done = true;
        _layer.PreviewPixels = null;
        _doc.Invalidate(Bitmaps.Union(_shown, Bitmaps.RoundOut(SourceBounds)));
        Dispose();
    }

    public void Dispose()
    {
        _done = true;
        if (_layer.PreviewPixels == _preview) _layer.PreviewPixels = null;
        _hole.Dispose();
        _floating.Dispose();
        _preview.Dispose();
    }

    private static bool IsTranslationOnly(SKMatrix m) =>
        m.ScaleX == 1 && m.ScaleY == 1 && m.SkewX == 0 && m.SkewY == 0 &&
        m.TransX == MathF.Round(m.TransX) && m.TransY == MathF.Round(m.TransY);
}

internal static class RectExtensions
{
    public static SKRectI Inflate1(this SKRectI r) => new(r.Left - 1, r.Top - 1, r.Right + 1, r.Bottom + 1);
}
