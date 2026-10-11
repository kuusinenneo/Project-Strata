using SkiaSharp;
using StrataPhoto.Core.Imaging;

namespace StrataPhoto.Core.Operations;

public sealed class GaussianBlur : ImageOperation
{
    public override string Id => "gaussian-blur";
    public override string Name => "Gaussian Blur";
    public override OperationKind Kind => OperationKind.Filter;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("radius", "Radius", 0.1, 250, 4, 0.1, " px", 1),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p) =>
        Blur(src, dst, rect, p.F("radius"));

    /// <summary>Gaussian blur of src into dst inside rect (radius ≈ standard deviation in px).</summary>
    public static void Blur(SKBitmap src, SKBitmap dst, SKRectI rect, float radius)
    {
        using var canvas = new SKCanvas(dst);
        canvas.ClipRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height));
        using var paint = new SKPaint
        {
            BlendMode = SKBlendMode.Src,
            ImageFilter = SKImageFilter.CreateBlur(radius, radius, SKShaderTileMode.Clamp),
        };
        canvas.DrawPixels(src, 0, 0, paint);
    }
}

/// <summary>Unsharp mask.</summary>
public sealed class Sharpen : ImageOperation
{
    public override string Id => "sharpen";
    public override string Name => "Sharpen (Unsharp Mask)";
    public override OperationKind Kind => OperationKind.Filter;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("amount", "Amount", 1, 500, 100, 1, "%"),
        new ParamDef("radius", "Radius", 0.1, 50, 1.5, 0.1, " px", 1),
        new ParamDef("threshold", "Threshold", 0, 255, 0, 1, " levels"),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        float amount = p.F("amount") / 100f;
        int threshold = (int)p.Get("threshold");
        using var blurred = Bitmaps.Copy(src);
        GaussianBlur.Blur(src, blurred, rect, p.F("radius"));
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* b = (byte*)blurred.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            int rb = src.RowBytes;
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                long row = (long)y * rb;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    long i = row + x * 4;
                    int a = s[i + 3];
                    for (int c = 0; c < 3; c++)
                    {
                        int diff = s[i + c] - b[i + c];
                        int v = Math.Abs(diff) <= threshold ? s[i + c] : (int)(s[i + c] + diff * amount);
                        d[i + c] = (byte)Math.Clamp(v, 0, a); // stay premultiplied
                    }
                    d[i + 3] = (byte)a;
                }
            });
        }
    }
}

public sealed class AddNoise : ImageOperation
{
    public override string Id => "noise";
    public override string Name => "Add Noise";
    public override OperationKind Kind => OperationKind.Filter;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("amount", "Amount", 0, 100, 12, 0.1, "%", 1),
        new ParamDef("gaussian", "Gaussian distribution", 0, 1, 1, 1, "bool"),
        new ParamDef("mono", "Monochromatic", 0, 1, 0, 1, "bool"),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        float amount = p.F("amount") / 100f * 255f;
        bool gaussian = p.Get("gaussian") >= 0.5, mono = p.Get("mono") >= 0.5;
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            int rb = src.RowBytes;
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                var rng = new Random(unchecked(y * 7919 + 17)); // deterministic: preview == result
                float Next() => gaussian
                    ? (float)(Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble())) * 0.5f
                    : (float)(rng.NextDouble() * 2 - 1);
                long row = (long)y * rb;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    long i = row + x * 4;
                    int a = s[i + 3];
                    float n0 = Next() * amount;
                    for (int c = 0; c < 3; c++)
                    {
                        float n = mono ? n0 : c == 0 ? n0 : Next() * amount;
                        d[i + c] = (byte)Math.Clamp((int)(s[i + c] + n * a / 255f), 0, a);
                    }
                    d[i + 3] = (byte)a;
                }
            });
        }
    }
}

public sealed class Pixelate : ImageOperation
{
    public override string Id => "pixelate";
    public override string Name => "Pixelate (Mosaic)";
    public override OperationKind Kind => OperationKind.Filter;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("cell", "Cell size", 2, 200, 10, 1, " px"),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        int cell = Math.Max(1, (int)p.Get("cell", 10));
        int x0 = rect.Left / cell * cell, y0 = rect.Top / cell * cell;
        int cols = (rect.Right - x0 + cell - 1) / cell, rows = (rect.Bottom - y0 + cell - 1) / cell;
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            int rb = src.RowBytes, w = src.Width, h = src.Height;
            Parallel.For(0, rows, cy =>
            {
                int top = y0 + cy * cell, bottom = Math.Min(h, top + cell);
                for (int cx = 0; cx < cols; cx++)
                {
                    int left = x0 + cx * cell, right = Math.Min(w, left + cell);
                    long sb = 0, sg = 0, sr = 0, sa = 0;
                    int n = 0;
                    for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                    {
                        long i = (long)y * rb + x * 4;
                        sb += s[i]; sg += s[i + 1]; sr += s[i + 2]; sa += s[i + 3];
                        n++;
                    }
                    if (n == 0) continue;
                    byte b = (byte)(sb / n), g = (byte)(sg / n), r = (byte)(sr / n), a = (byte)(sa / n);
                    for (int y = Math.Max(top, rect.Top); y < Math.Min(bottom, rect.Bottom); y++)
                    for (int x = Math.Max(left, rect.Left); x < Math.Min(right, rect.Right); x++)
                    {
                        long i = (long)y * rb + x * 4;
                        d[i] = b; d[i + 1] = g; d[i + 2] = r; d[i + 3] = a;
                    }
                }
            });
        }
    }
}

/// <summary>Grey relief from luminance differences along a light direction.</summary>
public sealed class Emboss : ImageOperation
{
    public override string Id => "emboss";
    public override string Name => "Emboss";
    public override OperationKind Kind => OperationKind.Filter;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("angle", "Angle", -180, 180, 135, 1, "°"),
        new ParamDef("height", "Height", 1, 10, 3, 1, " px"),
        new ParamDef("amount", "Amount", 1, 500, 100, 1, "%"),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        double ang = p.Get("angle", 135) * Math.PI / 180;
        float h = p.F("height");
        int dx = (int)Math.Round(Math.Cos(ang) * h), dy = (int)Math.Round(-Math.Sin(ang) * h);
        float amount = p.F("amount") / 100f;
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            int rb = src.RowBytes, w = src.Width, hh = src.Height;
            float Lum(int x, int y)
            {
                x = Math.Clamp(x, 0, w - 1);
                y = Math.Clamp(y, 0, hh - 1);
                long i = (long)y * rb + x * 4;
                int a = s[i + 3];
                if (a == 0) return 0;
                return (0.114f * s[i] + 0.587f * s[i + 1] + 0.299f * s[i + 2]) * 255f / a;
            }
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    long i = (long)y * rb + x * 4;
                    int a = s[i + 3];
                    float v = 128 + (Lum(x + dx, y + dy) - Lum(x - dx, y - dy)) * amount;
                    byte g = (byte)Math.Clamp((int)(v * a / 255f), 0, a);
                    d[i] = g; d[i + 1] = g; d[i + 2] = g; d[i + 3] = (byte)a;
                }
            });
        }
    }
}

/// <summary>The built-in adjustments and filters, in menu order.</summary>
public static class OperationCatalog
{
    public static IReadOnlyList<Func<ImageOperation>> Adjustments { get; } = new Func<ImageOperation>[]
    {
        () => new BrightnessContrast(), () => new Levels(), () => new Curves(), () => new Exposure(),
        () => new HueSaturation(), () => new ColorBalance(), () => new BlackAndWhite(), () => new Invert(),
    };

    public static IReadOnlyList<Func<ImageOperation>> Filters { get; } = new Func<ImageOperation>[]
    {
        () => new GaussianBlur(), () => new Sharpen(), () => new AddNoise(), () => new Pixelate(), () => new Emboss(),
    };
}
