using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace StrataPdf.App;

public static class BitmapConvert
{
    /// <summary>Copies a BGRA SkiaSharp bitmap into an Avalonia bitmap.</summary>
    public static WriteableBitmap ToAvalonia(SKBitmap src)
    {
        var wb = new WriteableBitmap(new PixelSize(src.Width, src.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)fb.Address;
            int row = Math.Min(src.RowBytes, fb.RowBytes);
            for (int y = 0; y < src.Height; y++)
                Buffer.MemoryCopy(s + (long)y * src.RowBytes, d + (long)y * fb.RowBytes, fb.RowBytes, row);
        }
        return wb;
    }

    public static Bitmap FromPng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }
}
