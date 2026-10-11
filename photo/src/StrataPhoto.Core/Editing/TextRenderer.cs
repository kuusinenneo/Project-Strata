using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.Editing;

/// <summary>Renders <see cref="TextProps"/> to pixels. (X, Y) is the anchor: top of the first line,
/// at the left edge, centre or right edge depending on the alignment.</summary>
public static class TextRenderer
{
    public static SKBitmap Render(TextProps props, int width, int height)
    {
        var bmp = Bitmaps.Create(width, height);
        using var canvas = new SKCanvas(bmp);
        Draw(canvas, props);
        return bmp;
    }

    public static void Draw(SKCanvas canvas, TextProps props)
    {
        using var paint = CreatePaint(props);
        var metrics = paint.FontMetrics;
        float lineHeight = props.FontSize * props.LineSpacing;
        float y = props.Y - metrics.Ascent;
        foreach (var line in Lines(props))
        {
            float w = paint.MeasureText(line);
            float x = props.Align switch
            {
                TextAlign.Center => props.X - w / 2,
                TextAlign.Right => props.X - w,
                _ => props.X,
            };
            canvas.DrawText(line, x, y, paint);
            y += lineHeight;
        }
    }

    /// <summary>Bounding box of the text in document coordinates.</summary>
    public static SKRect Measure(TextProps props)
    {
        using var paint = CreatePaint(props);
        var lines = Lines(props);
        float maxW = lines.Select(l => paint.MeasureText(l)).DefaultIfEmpty(0).Max();
        float lineHeight = props.FontSize * props.LineSpacing;
        var m = paint.FontMetrics;
        float height = (lines.Length - 1) * lineHeight + (m.Descent - m.Ascent);
        float left = props.Align switch
        {
            TextAlign.Center => props.X - maxW / 2,
            TextAlign.Right => props.X - maxW,
            _ => props.X,
        };
        return SKRect.Create(left, props.Y, Math.Max(maxW, 1), Math.Max(height, props.FontSize));
    }

    private static string[] Lines(TextProps props) => props.Text.Replace("\r\n", "\n").Split('\n');

    private static SKPaint CreatePaint(TextProps props)
    {
        var typeface = SKTypeface.FromFamilyName(props.FontFamily,
            props.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            props.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        return new SKPaint
        {
            Typeface = typeface,
            TextSize = props.FontSize,
            Color = props.Color,
            IsAntialias = true,
            SubpixelText = true,
            LcdRenderText = false,
        };
    }
}
