using SkiaSharp;

namespace StrataPhoto.Core.Operations;

public sealed class BrightnessContrast : ImageOperation
{
    public override string Id => "brightness-contrast";
    public override string Name => "Brightness/Contrast";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("brightness", "Brightness", -150, 150, 0),
        new ParamDef("contrast", "Contrast", -50, 100, 0),
    };

    public static byte[] Lut(double brightness, double contrast)
    {
        double k = contrast >= 0 ? 1 / (1 - Math.Min(contrast, 99.5) / 101) : 1 + contrast / 100;
        return PixelLoop.BuildLut(v =>
        {
            v += brightness / 255 * (brightness > 0 ? 1 - v * 0.5 : 0.5 + v * 0.5);
            return (v - 0.5) * k + 0.5;
        });
    }

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p) =>
        PixelLoop.ApplyLut(src, dst, rect, Lut(p.Get("brightness"), p.Get("contrast")));
}

public sealed class Levels : ImageOperation
{
    public override string Id => "levels";
    public override string Name => "Levels";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("inBlack", "Input black", 0, 253, 0),
        new ParamDef("gamma", "Midtones (gamma)", 0.1, 9.99, 1, 0.01, "", 2),
        new ParamDef("inWhite", "Input white", 2, 255, 255),
        new ParamDef("outBlack", "Output black", 0, 255, 0),
        new ParamDef("outWhite", "Output white", 0, 255, 255),
    };

    public static byte[] Lut(double inBlack, double gamma, double inWhite, double outBlack, double outWhite)
    {
        if (inWhite <= inBlack) inWhite = inBlack + 1;
        return PixelLoop.BuildLut(v =>
        {
            double x = Math.Clamp((v * 255 - inBlack) / (inWhite - inBlack), 0, 1);
            x = Math.Pow(x, 1 / Math.Max(0.01, gamma));
            return (outBlack + x * (outWhite - outBlack)) / 255;
        });
    }

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p) =>
        PixelLoop.ApplyLut(src, dst, rect, Lut(p.Get("inBlack"), p.Get("gamma", 1), p.Get("inWhite", 255),
            p.Get("outBlack"), p.Get("outWhite", 255)));
}

/// <summary>Tone curve through control points (0-255 both axes), monotone cubic interpolation.
/// The points live in <see cref="ImageOperation.Extra"/> as an <c>IReadOnlyList&lt;SKPoint&gt;</c>.</summary>
public sealed class Curves : ImageOperation
{
    public static readonly IReadOnlyList<SKPoint> Identity = new[] { new SKPoint(0, 0), new SKPoint(255, 255) };

    public override string Id => "curves";
    public override string Name => "Curves";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = Array.Empty<ParamDef>();

    public override object? Extra { get; set; } = Identity;

    public IReadOnlyList<SKPoint> Points
    {
        get => Extra as IReadOnlyList<SKPoint> ?? Identity;
        set => Extra = value;
    }

    public static byte[] Lut(IReadOnlyList<SKPoint> points)
    {
        var pts = points.OrderBy(p => p.X).GroupBy(p => (int)p.X).Select(g => g.First()).ToArray();
        if (pts.Length == 0) pts = Identity.ToArray();
        if (pts.Length == 1) pts = new[] { new SKPoint(0, pts[0].Y), new SKPoint(255, pts[0].Y) };
        int n = pts.Length;
        var xs = pts.Select(p => (double)p.X).ToArray();
        var ys = pts.Select(p => (double)p.Y).ToArray();
        // Fritsch-Carlson monotone tangents
        var d = new double[n - 1];
        for (int i = 0; i < n - 1; i++) d[i] = (ys[i + 1] - ys[i]) / Math.Max(1e-9, xs[i + 1] - xs[i]);
        var m = new double[n];
        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (int i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
        for (int i = 0; i < n - 1; i++)
        {
            if (Math.Abs(d[i]) < 1e-12) { m[i] = m[i + 1] = 0; continue; }
            double a = m[i] / d[i], b = m[i + 1] / d[i], s = a * a + b * b;
            if (s > 9)
            {
                double t = 3 / Math.Sqrt(s);
                m[i] = t * a * d[i];
                m[i + 1] = t * b * d[i];
            }
        }
        var lut = new byte[256];
        for (int x = 0; x < 256; x++)
        {
            double y;
            if (x <= xs[0]) y = ys[0];
            else if (x >= xs[n - 1]) y = ys[n - 1];
            else
            {
                int k = 0;
                while (k < n - 2 && x > xs[k + 1]) k++;
                double h = xs[k + 1] - xs[k], t = (x - xs[k]) / h;
                double t2 = t * t, t3 = t2 * t;
                y = (2 * t3 - 3 * t2 + 1) * ys[k] + (t3 - 2 * t2 + t) * h * m[k]
                    + (-2 * t3 + 3 * t2) * ys[k + 1] + (t3 - t2) * h * m[k + 1];
            }
            lut[x] = PixelLoop.ToByte(y);
        }
        return lut;
    }

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p) =>
        PixelLoop.ApplyLut(src, dst, rect, Lut(Points));
}

public sealed class HueSaturation : ImageOperation
{
    public override string Id => "hue-saturation";
    public override string Name => "Hue/Saturation";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("hue", "Hue", -180, 180, 0, 1, "°"),
        new ParamDef("saturation", "Saturation", -100, 100, 0),
        new ParamDef("lightness", "Lightness", -100, 100, 0),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        float hueShift = p.F("hue") / 360f, sat = p.F("saturation") / 100f, light = p.F("lightness") / 100f;
        PixelLoop.ApplyRgb(src, dst, rect, (ref float r, ref float g, ref float b) =>
        {
            ColorMath.RgbToHsl(r, g, b, out float h, out float s, out float l);
            h += hueShift;
            if (h < 0) h += 1; else if (h >= 1) h -= 1;
            s = sat >= 0 ? s + (1 - s) * sat * (s > 0 ? 1 : 0) : s * (1 + sat);
            ColorMath.HslToRgb(h, Math.Clamp(s, 0, 1), l, out r, out g, out b);
            if (light > 0) { r += (1 - r) * light; g += (1 - g) * light; b += (1 - b) * light; }
            else if (light < 0) { r *= 1 + light; g *= 1 + light; b *= 1 + light; }
        });
    }
}

public sealed class ColorBalance : ImageOperation
{
    public override string Id => "color-balance";
    public override string Name => "Color Balance";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("sCR", "Shadows: Cyan ↔ Red", -100, 100, 0),
        new ParamDef("sMG", "Shadows: Magenta ↔ Green", -100, 100, 0),
        new ParamDef("sYB", "Shadows: Yellow ↔ Blue", -100, 100, 0),
        new ParamDef("mCR", "Midtones: Cyan ↔ Red", -100, 100, 0),
        new ParamDef("mMG", "Midtones: Magenta ↔ Green", -100, 100, 0),
        new ParamDef("mYB", "Midtones: Yellow ↔ Blue", -100, 100, 0),
        new ParamDef("hCR", "Highlights: Cyan ↔ Red", -100, 100, 0),
        new ParamDef("hMG", "Highlights: Magenta ↔ Green", -100, 100, 0),
        new ParamDef("hYB", "Highlights: Yellow ↔ Blue", -100, 100, 0),
        new ParamDef("preserve", "Preserve luminosity", 0, 1, 1, 1, "bool"),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        // Per-channel transfer (GIMP's colour balance model), built as lookup tables.
        static double Weight(double v, int range)
        {
            const double a = 0.25, b = 0.333, scale = 0.7;
            return range switch
            {
                0 => Math.Clamp((v - b) / -a + 0.5, 0, 1) * scale,
                1 => Math.Clamp((v - b) / a + 0.5, 0, 1) * Math.Clamp((v + b - 1) / -a + 0.5, 0, 1) * scale,
                _ => Math.Clamp((v + b - 1) / a + 0.5, 0, 1) * scale,
            };
        }
        byte[] Lut(string s, string m, string h) => PixelLoop.BuildLut(v =>
            v + p.Get(s) / 100 * Weight(v, 0) + p.Get(m) / 100 * Weight(v, 1) + p.Get(h) / 100 * Weight(v, 2));
        var lr = Lut("sCR", "mCR", "hCR");
        var lg = Lut("sMG", "mMG", "hMG");
        var lb = Lut("sYB", "mYB", "hYB");
        bool preserve = p.Get("preserve", 1) >= 0.5;
        PixelLoop.ApplyRgb(src, dst, rect, (ref float r, ref float g, ref float b) =>
        {
            float r2 = lr[PixelLoop.ToByte(r * 255)] / 255f;
            float g2 = lg[PixelLoop.ToByte(g * 255)] / 255f;
            float b2 = lb[PixelLoop.ToByte(b * 255)] / 255f;
            if (preserve)
            {
                ColorMath.RgbToHsl(r, g, b, out _, out _, out float l0);
                ColorMath.RgbToHsl(r2, g2, b2, out float h, out float s, out _);
                ColorMath.HslToRgb(h, s, l0, out r2, out g2, out b2);
            }
            r = r2; g = g2; b = b2;
        });
    }
}

public sealed class Exposure : ImageOperation
{
    public override string Id => "exposure";
    public override string Name => "Exposure";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("exposure", "Exposure", -5, 5, 0, 0.01, " EV", 2),
        new ParamDef("offset", "Offset", -0.5, 0.5, 0, 0.001, "", 3),
        new ParamDef("gamma", "Gamma correction", 0.01, 9.99, 1, 0.01, "", 2),
    };

    public static byte[] Lut(double exposure, double offset, double gamma) => PixelLoop.BuildLut(v =>
    {
        double lin = ColorMath.SrgbToLinear(v) * Math.Pow(2, exposure) + offset;
        double s = ColorMath.LinearToSrgb(Math.Clamp(lin, 0, 1));
        return Math.Pow(s, 1 / Math.Max(0.01, gamma));
    });

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p) =>
        PixelLoop.ApplyLut(src, dst, rect, Lut(p.Get("exposure"), p.Get("offset"), p.Get("gamma", 1)));
}

/// <summary>Converts to greyscale with per-hue weights (like Photoshop's Black &amp; White).</summary>
public sealed class BlackAndWhite : ImageOperation
{
    public override string Id => "black-white";
    public override string Name => "Black & White";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = new[]
    {
        new ParamDef("reds", "Reds", -200, 300, 40, 1, "%"),
        new ParamDef("yellows", "Yellows", -200, 300, 60, 1, "%"),
        new ParamDef("greens", "Greens", -200, 300, 40, 1, "%"),
        new ParamDef("cyans", "Cyans", -200, 300, 60, 1, "%"),
        new ParamDef("blues", "Blues", -200, 300, 20, 1, "%"),
        new ParamDef("magentas", "Magentas", -200, 300, 80, 1, "%"),
    };

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p)
    {
        float wr = p.F("reds") / 100, wy = p.F("yellows") / 100, wg = p.F("greens") / 100;
        float wc = p.F("cyans") / 100, wb = p.F("blues") / 100, wm = p.F("magentas") / 100;
        PixelLoop.ApplyRgb(src, dst, rect, (ref float r, ref float g, ref float b) =>
        {
            float min = Math.Min(r, Math.Min(g, b));
            float rr = r - min, gg = g - min, bb = b - min;
            float gray = min;
            // The largest remaining channel is a primary; the two non-zero channels form a secondary.
            if (rr >= gg && rr >= bb)
                gray += gg >= bb ? gg * wy + (rr - gg) * wr : bb * wm + (rr - bb) * wr;
            else if (gg >= rr && gg >= bb)
                gray += rr >= bb ? rr * wy + (gg - rr) * wg : bb * wc + (gg - bb) * wg;
            else
                gray += rr >= gg ? rr * wm + (bb - rr) * wb : gg * wc + (bb - gg) * wb;
            gray = Math.Clamp(gray, 0, 1);
            r = g = b = gray;
        });
    }
}

public sealed class Invert : ImageOperation
{
    private static readonly byte[] InvertLut = Enumerable.Range(0, 256).Select(i => (byte)(255 - i)).ToArray();

    public override string Id => "invert";
    public override string Name => "Invert";
    public override OperationKind Kind => OperationKind.Adjustment;
    public override IReadOnlyList<ParamDef> Params { get; } = Array.Empty<ParamDef>();

    public override void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p) =>
        PixelLoop.ApplyLut(src, dst, rect, InvertLut);
}

public static class ColorMath
{
    public static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max - min < 1e-6f) { h = 0; s = 0; return; }
        float d = max - min;
        s = l > 0.5f ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6;
    }

    public static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        if (s <= 0) { r = g = b = l; return; }
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        r = Hue(p, q, h + 1f / 3);
        g = Hue(p, q, h);
        b = Hue(p, q, h - 1f / 3);
    }

    private static float Hue(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6) return p + (q - p) * 6 * t;
        if (t < 0.5f) return q;
        if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
        return p;
    }

    public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        v = max;
        float d = max - min;
        s = max <= 0 ? 0 : d / max;
        if (d < 1e-6f) { h = 0; return; }
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6;
    }

    public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        h = (h % 1 + 1) % 1 * 6;
        int i = (int)MathF.Floor(h);
        float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }

    public static double SrgbToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    public static double LinearToSrgb(double v) => v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
}
