using SkiaSharp;

namespace StrataPhoto.Core.Imaging;

/// <summary>
/// Zero-copy drawing of mutable bitmaps.
///
/// SkiaSharp's <c>DrawBitmap</c> and <c>SKShader.CreateBitmap</c> go through
/// <c>SKImage.FromBitmap</c>, which copies a mutable bitmap's pixels on every call — 64 MB per
/// draw on a 4000×4000 layer. Wrapping the pixels with <c>SKImage.FromPixels</c> shares them
/// instead. Callers must not modify the bitmap while the image or shader is in use.
/// </summary>
public static class SkiaExtensions
{
    public static SKImage AsImage(this SKBitmap bmp) => SKImage.FromPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes);

    public static void DrawPixels(this SKCanvas canvas, SKBitmap bmp, float x, float y, SKPaint? paint = null)
    {
        using var image = bmp.AsImage();
        canvas.DrawImage(image, x, y, paint);
    }

    public static void DrawPixels(this SKCanvas canvas, SKBitmap bmp, SKRect src, SKRect dst, SKPaint? paint = null)
    {
        using var image = bmp.AsImage();
        canvas.DrawImage(image, src, dst, paint);
    }

    public static void DrawPixels(this SKCanvas canvas, SKBitmap bmp, SKRect dst, SKPaint? paint = null)
    {
        using var image = bmp.AsImage();
        canvas.DrawImage(image, dst, paint);
    }

    public static SKShader ToPixelShader(this SKBitmap bmp, SKShaderTileMode tileX, SKShaderTileMode tileY, SKMatrix localMatrix)
    {
        using var image = bmp.AsImage();
        return image.ToShader(tileX, tileY, localMatrix); // the shader keeps its own reference to the image
    }

    /// <summary>
    /// Multiplies the canvas content inside <paramref name="area"/> by an Alpha8 mask (drawn at
    /// <paramref name="dx"/>, <paramref name="dy"/>), clearing where the mask is 0.
    /// (Drawing the Alpha8 bitmap directly would treat it as coverage and leave 0-coverage pixels untouched.)
    /// </summary>
    public static void MaskIn(this SKCanvas canvas, SKBitmap mask, SKRect area, float dx = 0, float dy = 0)
    {
        using var shader = mask.ToPixelShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, SKMatrix.CreateTranslation(dx, dy));
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.DstIn };
        canvas.DrawRect(area, paint);
    }
}
