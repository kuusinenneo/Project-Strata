using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace StrataPdf.Tests;

/// <summary>Builds small PDFs for the tests.</summary>
internal static class TestPdfs
{
    static TestPdfs()
    {
        StrataPdf.Core.IO.PdfFonts.Ensure();
    }

    public static string Temp(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "strata-pdf-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>Pages of the given stored size and /Rotate, each with the text "Page N" (plus extra words).</summary>
    public static string Make(string name, int pages, double w = 300, double h = 400, int rotate = 0, string extra = "")
    {
        string path = Temp(name);
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(w, h));
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawString($"Page {i + 1} {extra}".Trim(), new XFont("Arial", 14), XBrushes.Black, 20, 40);
            }
            page.Rotate = rotate;
        }
        doc.Save(path);
        return path;
    }

    /// <summary>One page with a JPEG and a raw (Flate) RGB image.</summary>
    public static string WithImages()
    {
        string path = Temp("images.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawImage(XImage.FromStream(new MemoryStream(Encode(SKEncodedImageFormat.Jpeg, SKColors.Red))), 20, 20, 100, 60);
            gfx.DrawImage(XImage.FromStream(new MemoryStream(Encode(SKEncodedImageFormat.Png, SKColors.Blue))), 20, 120, 100, 60);
        }
        doc.Save(path);
        return path;
    }

    private static byte[] Encode(SKEncodedImageFormat fmt, SKColor color)
    {
        using var bmp = new SKBitmap(40, 24);
        bmp.Erase(color);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(fmt, 90).ToArray();
    }

    /// <summary>A hand-written PDF with a text field "name" and a check box "agree".</summary>
    public static string WithForm()
    {
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R] /DA (/Helv 12 Tf 0 g) /DR << /Font << /Helv 6 0 R >> >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [4 0 R 5 0 R] /Resources << /Font << /Helv 6 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (Ann) /Rect [20 240 220 264] /P 3 0 R /F 4 /DA (/Helv 12 Tf 0 g) >>",
            "<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /Off /AS /Off /Rect [20 200 36 216] /P 3 0 R /F 4 " +
            "/AP << /N << /Yes 7 0 R /Off 8 0 R >> >> >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Length 0 /Type /XObject /Subtype /Form /BBox [0 0 16 16] >>\nstream\n\nendstream",
            "<< /Length 0 /Type /XObject /Subtype /Form /BBox [0 0 16 16] >>\nstream\n\nendstream",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objs.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        string path = Temp("form.pdf");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        return path;
    }
}
