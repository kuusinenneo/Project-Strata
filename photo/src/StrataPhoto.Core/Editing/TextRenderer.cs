using System.Text;
using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.Editing;

/// <summary>Renders <see cref="TextProps"/> to pixels. (X, Y) is the anchor: top of the first line,
/// at the left edge, centre or right edge depending on the alignment. Characters the chosen font
/// lacks (symbols, emoji, other scripts) fall back to a system font that has them.</summary>
public static class TextRenderer
{
    private sealed record Run(string Text, SKTypeface Typeface);

    public static SKBitmap Render(TextProps props, int width, int height)
    {
        var bmp = Bitmaps.Create(width, height);
        using var canvas = new SKCanvas(bmp);
        Draw(canvas, props);
        return bmp;
    }

    public static void Draw(SKCanvas canvas, TextProps props)
    {
        using var paint = CreatePaint(props, out var style);
        var metrics = paint.FontMetrics;
        float lineHeight = props.FontSize * props.LineSpacing;
        float y = props.Y - metrics.Ascent;
        foreach (var line in Lines(props))
        {
            var runs = Runs(line, paint.Typeface, props.FontFamily, style);
            float w = Width(runs, paint);
            float x = props.Align switch
            {
                TextAlign.Center => props.X - w / 2,
                TextAlign.Right => props.X - w,
                _ => props.X,
            };
            foreach (var run in runs)
            {
                paint.Typeface = run.Typeface;
                canvas.DrawText(run.Text, x, y, paint);
                x += paint.MeasureText(run.Text);
            }
            y += lineHeight;
        }
    }

    /// <summary>Bounding box of the text in document coordinates.</summary>
    public static SKRect Measure(TextProps props)
    {
        using var paint = CreatePaint(props, out var style);
        var lines = Lines(props);
        float maxW = lines.Select(l => Width(Runs(l, paint.Typeface, props.FontFamily, style), paint)).DefaultIfEmpty(0).Max();
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

    private static float Width(IReadOnlyList<Run> runs, SKPaint paint)
    {
        var primary = paint.Typeface;
        float w = 0;
        foreach (var r in runs)
        {
            paint.Typeface = r.Typeface;
            w += paint.MeasureText(r.Text);
        }
        paint.Typeface = primary;
        return w;
    }

    /// <summary>Splits a line into runs of characters that the same typeface can draw.</summary>
    private static List<Run> Runs(string line, SKTypeface primary, string family, SKFontStyle style)
    {
        var runs = new List<Run>();
        var current = new StringBuilder();
        SKTypeface? currentFace = null;
        foreach (var rune in line.EnumerateRunes())
        {
            var face = primary;
            if (!Rune.IsWhiteSpace(rune) && !primary.ContainsGlyph(rune.Value))
                face = SKFontManager.Default.MatchCharacter(family, style, null, rune.Value) ?? primary;
            if (currentFace != null && face.FamilyName != currentFace.FamilyName)
            {
                runs.Add(new Run(current.ToString(), currentFace));
                current.Clear();
            }
            currentFace = face;
            current.Append(rune.ToString());
        }
        if (current.Length > 0 && currentFace != null) runs.Add(new Run(current.ToString(), currentFace));
        return runs;
    }

    private static string[] Lines(TextProps props) => props.Text.Replace("\r\n", "\n").Split('\n');

    private static SKPaint CreatePaint(TextProps props, out SKFontStyle style)
    {
        style = new SKFontStyle(props.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, props.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var typeface = SKTypeface.FromFamilyName(props.FontFamily, style) ?? SKTypeface.Default;
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
