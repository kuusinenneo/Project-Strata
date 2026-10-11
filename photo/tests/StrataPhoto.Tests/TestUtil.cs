using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Tests;

internal static class TestUtil
{
    public static Document Blank(int w = 64, int h = 64, SKColor? bg = null) =>
        Document.CreateBlank(w, h, "test", bg ?? SKColors.White);

    /// <summary>Un-premultiplied pixel of the composite.</summary>
    public static SKColor Composite(Document doc, int x, int y) => Bitmaps.GetPixel(doc.Compositor.Composite, x, y);

    public static SKColor Pixel(SKBitmap bmp, int x, int y) => Bitmaps.GetPixel(bmp, x, y);

    public static void AssertColor(SKColor expected, SKColor actual, int tolerance = 2)
    {
        bool ok = Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance;
        Assert.True(ok, $"expected {expected}, got {actual}");
    }

    public static string TempFile(string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(), "strataphoto-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
    }
}
