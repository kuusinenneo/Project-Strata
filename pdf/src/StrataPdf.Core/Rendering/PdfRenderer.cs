using PDFiumCore;
using SkiaSharp;

namespace StrataPdf.Core.Rendering;

/// <summary>
/// Renders PDF pages with PDFium (PDFiumCore bindings). PDFium is not thread-safe, so every call
/// goes through one lock. Each file is opened once and stays open until <see cref="Forget"/>;
/// pages render straight into SkiaSharp bitmaps at any scale.
/// </summary>
public static class PdfRenderer
{
    private const int FlagAnnotations = 0x01;  // FPDF_ANNOT: draw annotation/form appearances
    private const int FormatBgra = 4;          // FPDFBitmap_BGRA

    private static readonly object Lock = new();
    private static readonly Dictionary<string, FpdfDocumentT> Docs = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    private static FpdfDocumentT Doc(string path)
    {
        if (!_initialized)
        {
            fpdfview.FPDF_InitLibrary();
            _initialized = true;
        }
        string key = Path.GetFullPath(path);
        if (Docs.TryGetValue(key, out var doc)) return doc;
        doc = fpdfview.FPDF_LoadDocument(key, null);
        if (doc == null || doc.__Instance == IntPtr.Zero)
            throw new InvalidDataException($"\"{Path.GetFileName(path)}\" could not be opened as a PDF (it may be damaged or password-protected).");
        Docs[key] = doc;
        return doc;
    }

    private static T WithPage<T>(string path, int index, Func<FpdfPageT, T> f)
    {
        var page = fpdfview.FPDF_LoadPage(Doc(path), index);
        if (page == null || page.__Instance == IntPtr.Zero) throw new ArgumentOutOfRangeException(nameof(index));
        try
        {
            return f(page);
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    public static int PageCount(string path)
    {
        lock (Lock) return fpdfview.FPDF_GetPageCount(Doc(path));
    }

    /// <summary>Page sizes in points, as displayed (the page's own /Rotate applied).</summary>
    public static IReadOnlyList<(double Width, double Height)> PageSizes(string path)
    {
        lock (Lock)
        {
            int n = fpdfview.FPDF_GetPageCount(Doc(path));
            var sizes = new List<(double, double)>(n);
            for (int i = 0; i < n; i++)
                sizes.Add(WithPage(path, i, p => ((double)fpdfview.FPDF_GetPageWidthF(p), (double)fpdfview.FPDF_GetPageHeightF(p))));
            return sizes;
        }
    }

    /// <summary>
    /// Renders a page at <paramref name="scale"/> pixels per point on white, rotated clockwise by
    /// <paramref name="extraRotation"/> degrees (0/90/180/270).
    /// </summary>
    public static SKBitmap RenderPage(string path, int index, double scale, int extraRotation = 0)
    {
        SKBitmap bmp;
        lock (Lock)
        {
            bmp = WithPage(path, index, page =>
            {
                int w = Math.Max(1, (int)Math.Round(fpdfview.FPDF_GetPageWidthF(page) * scale));
                int h = Math.Max(1, (int)Math.Round(fpdfview.FPDF_GetPageHeightF(page) * scale));
                var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                var fb = fpdfview.FPDFBitmapCreateEx(w, h, FormatBgra, b.GetPixels(), b.RowBytes);
                try
                {
                    fpdfview.FPDFBitmapFillRect(fb, 0, 0, w, h, 0xFFFFFFFF);
                    fpdfview.FPDF_RenderPageBitmap(fb, page, 0, 0, w, h, 0, FlagAnnotations);
                }
                finally
                {
                    fpdfview.FPDFBitmapDestroy(fb);
                }
                return b;
            });
        }
        return Rotate(bmp, extraRotation);
    }

    /// <summary>Rotates a bitmap clockwise by a multiple of 90° (returns the input for 0).</summary>
    public static SKBitmap Rotate(SKBitmap src, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0) return src;
        bool swap = degrees != 180;
        var dst = new SKBitmap(new SKImageInfo(swap ? src.Height : src.Width, swap ? src.Width : src.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(dst))
        {
            c.Translate(dst.Width / 2f, dst.Height / 2f);
            c.RotateDegrees(degrees);
            c.Translate(-src.Width / 2f, -src.Height / 2f);
            c.DrawBitmap(src, 0, 0);
        }
        src.Dispose();
        return dst;
    }

    /// <summary>Plain text of a page (for search and tests).</summary>
    public static string PageText(string path, int index)
    {
        lock (Lock)
        {
            return WithPage(path, index, page =>
            {
                var text = fpdf_text.FPDFTextLoadPage(page);
                try
                {
                    int n = fpdf_text.FPDFTextCountChars(text);
                    if (n <= 0) return "";
                    var buffer = new ushort[n + 1];
                    int written = fpdf_text.FPDFTextGetText(text, 0, n, ref buffer[0]);
                    var chars = new char[Math.Max(0, written - 1)];
                    for (int i = 0; i < chars.Length; i++) chars[i] = (char)buffer[i];
                    return new string(chars);
                }
                finally
                {
                    fpdf_text.FPDFTextClosePage(text);
                }
            });
        }
    }

    /// <summary>Closes these files (before deleting or overwriting them).</summary>
    public static void Forget(IEnumerable<string> paths)
    {
        lock (Lock)
        {
            foreach (var p in paths)
            {
                string key = Path.GetFullPath(p);
                if (Docs.Remove(key, out var doc)) fpdfview.FPDF_CloseDocument(doc);
            }
        }
    }
}
