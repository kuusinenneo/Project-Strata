using SkiaSharp;

namespace StrataPhoto.Core.Operations;

/// <summary>One numeric parameter of an adjustment or filter (drives the generated UI).</summary>
public sealed record ParamDef(string Key, string Label, double Min, double Max, double Default,
    double Step = 1, string Unit = "", int Decimals = 0);

/// <summary>Parameter values by key.</summary>
public sealed class ParamValues : Dictionary<string, double>
{
    public ParamValues() { }
    public ParamValues(IEnumerable<ParamDef> defs)
    {
        foreach (var d in defs) this[d.Key] = d.Default;
    }

    public double Get(string key, double fallback = 0) => TryGetValue(key, out var v) ? v : fallback;
    public float F(string key) => (float)Get(key);
    public ParamValues Clone()
    {
        var c = new ParamValues();
        foreach (var kv in this) c[kv.Key] = kv.Value;
        return c;
    }
}

public enum OperationKind { Adjustment, Filter }

/// <summary>
/// An adjustment or filter: reads <c>src</c> and writes <c>dst</c> (same size) inside <c>rect</c>.
/// Implementations are thread-safe so previews can run in the background.
/// </summary>
public abstract class ImageOperation
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract OperationKind Kind { get; }
    public abstract IReadOnlyList<ParamDef> Params { get; }

    /// <summary>Optional extra data that isn't a plain number (curve points).</summary>
    public virtual object? Extra { get; set; }

    public ParamValues Defaults() => new(Params);

    public abstract void Apply(SKBitmap src, SKBitmap dst, SKRectI rect, ParamValues p);

    public override string ToString() => Name;
}

/// <summary>Pixel loops over premultiplied BGRA with un-premultiplied per-pixel callbacks.</summary>
public static class PixelLoop
{
    public delegate void RgbFunc(ref float r, ref float g, ref float b);

    /// <summary>Applies a per-channel 256-entry lookup table to un-premultiplied colour.</summary>
    public static void ApplyLut(SKBitmap src, SKBitmap dst, SKRectI rect, byte[] lutR, byte[] lutG, byte[] lutB)
    {
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            int srb = src.RowBytes, drb = dst.RowBytes;
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                byte* sr = s + (long)y * srb;
                byte* dr = d + (long)y * drb;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    int i = x * 4;
                    int a = sr[i + 3];
                    if (a == 0) { *(uint*)(dr + i) = 0; continue; }
                    if (a == 255)
                    {
                        dr[i] = lutB[sr[i]];
                        dr[i + 1] = lutG[sr[i + 1]];
                        dr[i + 2] = lutR[sr[i + 2]];
                        dr[i + 3] = 255;
                        continue;
                    }
                    int b = Math.Min(255, sr[i] * 255 / a), g = Math.Min(255, sr[i + 1] * 255 / a), r = Math.Min(255, sr[i + 2] * 255 / a);
                    dr[i] = (byte)(lutB[b] * a / 255);
                    dr[i + 1] = (byte)(lutG[g] * a / 255);
                    dr[i + 2] = (byte)(lutR[r] * a / 255);
                    dr[i + 3] = (byte)a;
                }
            });
        }
    }

    public static void ApplyLut(SKBitmap src, SKBitmap dst, SKRectI rect, byte[] lut) => ApplyLut(src, dst, rect, lut, lut, lut);

    /// <summary>Per-pixel function on un-premultiplied RGB in 0..1.</summary>
    public static void ApplyRgb(SKBitmap src, SKBitmap dst, SKRectI rect, RgbFunc f)
    {
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            int srb = src.RowBytes, drb = dst.RowBytes;
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                byte* sr = s + (long)y * srb;
                byte* dr = d + (long)y * drb;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    int i = x * 4;
                    int a = sr[i + 3];
                    if (a == 0) { *(uint*)(dr + i) = 0; continue; }
                    float inv = 1f / a;
                    float b = sr[i] * inv, g = sr[i + 1] * inv, r = sr[i + 2] * inv;
                    f(ref r, ref g, ref b);
                    float af = a;
                    dr[i] = ToByte(b * af);
                    dr[i + 1] = ToByte(g * af);
                    dr[i + 2] = ToByte(r * af);
                    dr[i + 3] = (byte)a;
                }
            });
        }
    }

    public static byte ToByte(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5f);
    public static byte ToByte(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5);

    public static byte[] BuildLut(Func<double, double> f)
    {
        var lut = new byte[256];
        for (int i = 0; i < 256; i++) lut[i] = ToByte(f(i / 255.0) * 255);
        return lut;
    }
}
