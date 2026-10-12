using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace StrataPdf.Core.IO;

public sealed record ExtractedImage(string FileName, int PageNumber, int Width, int Height);

/// <summary>Extracts embedded raster images from a PDF.</summary>
public static class PdfImages
{
    /// <summary>
    /// Saves the images found on the given pages (1-based page numbers into <paramref name="path"/>)
    /// to <paramref name="folder"/>: JPEGs as-is, 8-bit RGB/grey images as PNG. Other kinds (CMYK,
    /// indexed, JPEG 2000, masks) are skipped and counted in <paramref name="skipped"/>.
    /// </summary>
    public static IReadOnlyList<ExtractedImage> Extract(string path, IEnumerable<int> pageNumbers, string folder,
        out int skipped)
    {
        Directory.CreateDirectory(folder);
        var result = new List<ExtractedImage>();
        skipped = 0;
        var seen = new HashSet<int>();
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        string stem = Path.GetFileNameWithoutExtension(path);
        foreach (int pageNo in pageNumbers)
        {
            if (pageNo < 1 || pageNo > doc.PageCount) continue;
            var resources = doc.Pages[pageNo - 1].Elements.GetDictionary("/Resources");
            var xobjects = resources?.Elements.GetDictionary("/XObject");
            if (xobjects == null) continue;
            int n = 0;
            foreach (var item in xobjects.Elements.Values)
            {
                if (item is not PdfReference { Value: PdfDictionary img } reference) continue;
                if (img.Elements.GetName("/Subtype") != "/Image") continue;
                if (!seen.Add(reference.ObjectNumber)) continue;
                n++;
                int w = img.Elements.GetInteger("/Width"), h = img.Elements.GetInteger("/Height");
                string baseName = $"{stem}-p{pageNo}-{n}";
                string? file = SaveImage(img, w, h, Path.Combine(folder, baseName));
                if (file == null) skipped++;
                else result.Add(new ExtractedImage(file, pageNo, w, h));
            }
        }
        return result;
    }

    private static string? SaveImage(PdfDictionary img, int w, int h, string basePath)
    {
        var filter = img.Elements["/Filter"];
        string? filterName = filter switch
        {
            PdfName n => n.Value,
            PdfArray a when a.Elements.Count == 1 => (a.Elements[0] as PdfName)?.Value,
            null => null,
            _ => "multiple",
        };
        if (filterName == "/DCTDecode")
        {
            string file = basePath + ".jpg";
            File.WriteAllBytes(file, img.Stream.Value);
            return file;
        }
        if (filterName is not ("/FlateDecode" or null)) return null;
        if (img.Elements.GetInteger("/BitsPerComponent") != 8) return null;
        string? cs = (img.Elements["/ColorSpace"] as PdfName)?.Value;
        int channels = cs switch { "/DeviceRGB" => 3, "/DeviceGray" => 1, _ => 0 };
        if (channels == 0 || img.Elements.ContainsKey("/DecodeParms")) return null;
        byte[] data;
        try
        {
            data = img.Stream.UnfilteredValue;
        }
        catch
        {
            return null;
        }
        if (data.Length < w * h * channels) return null;
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var px = new byte[w * h * 4];
        for (int i = 0, j = 0; i < w * h; i++, j += channels)
        {
            px[i * 4] = data[j];
            px[i * 4 + 1] = channels == 3 ? data[j + 1] : data[j];
            px[i * 4 + 2] = channels == 3 ? data[j + 2] : data[j];
            px[i * 4 + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        string png = basePath + ".png";
        using var image = SKImage.FromBitmap(bmp);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using (var f = File.Create(png)) encoded.SaveTo(f);
        return png;
    }
}
