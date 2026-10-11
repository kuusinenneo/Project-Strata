using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Editing;

/// <summary>Whole-document geometry changes: Image Size, Canvas Size, rotate, flip, crop.</summary>
public static class ImageTransforms
{
    public static void ResizeImage(Document doc, int width, int height)
    {
        if (width < 1 || height < 1 || (width == doc.Width && height == doc.Height)) return;
        var m = SKMatrix.CreateScale((float)width / doc.Width, (float)height / doc.Height);
        Apply(doc, "Image Size", width, height, m, SKFilterQuality.High);
    }

    /// <summary>Changes the canvas size. Anchor 0..2 per axis (0 = left/top, 1 = centre, 2 = right/bottom).</summary>
    public static void ResizeCanvas(Document doc, int width, int height, int anchorX = 1, int anchorY = 1,
        SKColor? backgroundFill = null)
    {
        if (width < 1 || height < 1 || (width == doc.Width && height == doc.Height)) return;
        float dx = (width - doc.Width) * anchorX / 2f, dy = (height - doc.Height) * anchorY / 2f;
        var m = SKMatrix.CreateTranslation(MathF.Round(dx), MathF.Round(dy));
        Apply(doc, "Canvas Size", width, height, m, SKFilterQuality.None, backgroundFill);
    }

    /// <summary>Rotates the image. Multiples of 90° are lossless; other angles enlarge the canvas.</summary>
    public static void Rotate(Document doc, float degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0) return;
        bool right = Math.Abs(degrees % 90) < 1e-4;
        double rad = degrees * Math.PI / 180;
        double cos = Math.Abs(Math.Cos(rad)), sin = Math.Abs(Math.Sin(rad));
        int w = (int)Math.Round(doc.Width * cos + doc.Height * sin);
        int h = (int)Math.Round(doc.Width * sin + doc.Height * cos);
        var m = SKMatrix.CreateTranslation(-doc.Width / 2f, -doc.Height / 2f);
        m = m.PostConcat(SKMatrix.CreateRotationDegrees(degrees));
        m = m.PostConcat(SKMatrix.CreateTranslation(w / 2f, h / 2f));
        string name = degrees switch { 90 => "Rotate 90° Clockwise", 270 => "Rotate 90° Counter Clockwise", 180 => "Rotate 180°", _ => "Rotate Canvas" };
        Apply(doc, name, w, h, m, right ? SKFilterQuality.None : SKFilterQuality.High);
    }

    public static void Flip(Document doc, bool horizontal)
    {
        var m = horizontal
            ? new SKMatrix(-1, 0, doc.Width, 0, 1, 0, 0, 0, 1)
            : new SKMatrix(1, 0, 0, 0, -1, doc.Height, 0, 0, 1);
        Apply(doc, horizontal ? "Flip Canvas Horizontal" : "Flip Canvas Vertical", doc.Width, doc.Height, m, SKFilterQuality.None);
    }

    public static void Crop(Document doc, SKRectI rect)
    {
        rect = Bitmaps.Intersect(rect, doc.Bounds);
        if (rect.Width < 1 || rect.Height < 1 || rect == doc.Bounds) return;
        Apply(doc, "Crop", rect.Width, rect.Height, SKMatrix.CreateTranslation(-rect.Left, -rect.Top), SKFilterQuality.None);
    }

    /// <summary>Re-renders every layer through <paramref name="m"/> onto a new canvas, as one undo step.</summary>
    private static void Apply(Document doc, string name, int w, int h, SKMatrix m, SKFilterQuality quality,
        SKColor? backgroundFill = null)
    {
        bool scaleOnly = m.SkewX == 0 && m.SkewY == 0 && m.ScaleX > 0 && m.ScaleY > 0;
        float textScale = scaleOnly ? (m.ScaleX + m.ScaleY) / 2 : 1;
        var plan = new List<Action>();
        foreach (var layer in doc.AllLayers.ToList())
        {
            SKBitmap? mask = layer.Mask == null ? null : TransformBitmap(layer.Mask, w, h, m, quality, SKColors.White);
            switch (layer)
            {
                case TextLayer t when scaleOnly:
                {
                    var p = t.Text;
                    var pt = m.MapPoint(p.X, p.Y);
                    var props = p with { X = pt.X, Y = pt.Y, FontSize = p.FontSize * textScale };
                    var px = TextRenderer.Render(props, w, h);
                    plan.Add(() => { t.Text = props; t.Pixels = px; t.Mask = mask; });
                    break;
                }
                case TextLayer t:
                {
                    // Rotations/flips can't be expressed in text properties: rasterize.
                    var px = TransformBitmap(t.Pixels, w, h, m, quality, null);
                    var pixel = new PixelLayer(t.Name, px) { Opacity = t.Opacity, BlendMode = t.BlendMode, Visible = t.Visible, Locked = t.Locked };
                    plan.Add(() =>
                    {
                        pixel.Mask = mask;
                        var parent = t.Parent!;
                        int i = parent.Children.IndexOf(t);
                        parent.Remove(t);
                        parent.Insert(i, pixel);
                        if (doc.ActiveLayer == t) doc.ActiveLayer = pixel;
                    });
                    break;
                }
                case PixelLayer p:
                {
                    SKColor? fill = backgroundFill.HasValue && p.Name == "Background" ? backgroundFill : null;
                    var px = TransformBitmap(p.Pixels, w, h, m, quality, fill);
                    plan.Add(() => { p.Pixels = px; p.Mask = mask; });
                    break;
                }
                default:
                    plan.Add(() => layer.Mask = mask);
                    break;
            }
        }
        var selection = doc.Selection.IsEmpty ? Selection.None : SelectionOps.Transform(doc.Selection, w, h, m);
        doc.Commit(name, () =>
        {
            doc.SetSize(w, h);
            foreach (var step in plan) step();
            doc.Selection = selection;
        });
    }

    public static SKBitmap TransformBitmap(SKBitmap src, int w, int h, SKMatrix m, SKFilterQuality quality, SKColor? background)
    {
        var dst = Bitmaps.Create(w, h, background);
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(m);
        using var paint = new SKPaint { FilterQuality = quality, IsAntialias = quality != SKFilterQuality.None };
        canvas.DrawPixels(src, 0, 0, paint);
        return dst;
    }
}
