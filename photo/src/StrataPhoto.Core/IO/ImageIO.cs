using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.IO;

public enum ExportFormat { Png, Jpeg, Webp }

public sealed record ExportOptions(ExportFormat Format, int Quality = 90, int? Width = null, int? Height = null,
    SKColor? JpegBackground = null);

/// <summary>Reading PNG/JPEG/WebP/BMP/GIF images and exporting flattened images.</summary>
public static class ImageIO
{
    public static readonly string[] OpenExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    public static bool IsImageFile(string path) =>
        OpenExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Decodes an image file into premultiplied BGRA, applying EXIF orientation.</summary>
    public static SKBitmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream) ?? throw new InvalidDataException($"\"{Path.GetFileName(path)}\" is not a supported image.");
    }

    public static SKBitmap? Load(Stream stream)
    {
        using var data = SKData.Create(stream);
        if (data == null) return null;
        using var codec = SKCodec.Create(data);
        if (codec == null) return null;
        var info = Bitmaps.ColorInfo(codec.Info.Width, codec.Info.Height);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            bmp.Dispose();
            return null;
        }
        return ApplyOrigin(bmp, codec.EncodedOrigin);
    }

    public static Document Open(string path)
    {
        var bmp = Load(path);
        var doc = Document.FromBitmap(bmp, Path.GetFileName(path));
        doc.MarkSaved();
        return doc;
    }

    private static SKBitmap ApplyOrigin(SKBitmap bmp, SKEncodedOrigin origin)
    {
        float sw = bmp.Width, sh = bmp.Height;
        // x' = a*x + b*y + c ; y' = d*x + e*y + f  (maps stored pixels to display orientation)
        SKMatrix? m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, sw, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, sw, 0, -1, sh, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, sh, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, sh, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, sh, -1, 0, sw, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, sw, 0, 0, 1),
            _ => null,
        };
        if (m == null) return bmp;
        bool swap = origin >= SKEncodedOrigin.LeftTop;
        var dst = Bitmaps.Create(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height);
        using (var canvas = new SKCanvas(dst))
        {
            canvas.SetMatrix(m.Value);
            canvas.DrawPixels(bmp, 0, 0);
        }
        bmp.Dispose();
        return dst;
    }

    /// <summary>Flattens the document and writes it as PNG, JPEG or WebP.</summary>
    public static void Export(Document doc, string path, ExportOptions options)
    {
        using var flat = Compositor.Flatten(doc.Root, doc.Width, doc.Height);
        using var stream = File.Create(path);
        Encode(flat, stream, options);
    }

    public static void Encode(SKBitmap source, Stream stream, ExportOptions options)
    {
        int w = Math.Max(1, options.Width ?? source.Width), h = Math.Max(1, options.Height ?? source.Height);
        SKBitmap bmp = source;
        if (w != source.Width || h != source.Height)
        {
            bmp = Bitmaps.Create(w, h);
            using var c = new SKCanvas(bmp);
            using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            c.DrawPixels(source, new SKRect(0, 0, w, h), p);
        }
        if (options.Format == ExportFormat.Jpeg)
        {
            // JPEG has no alpha: flatten onto the background colour.
            var opaque = Bitmaps.Create(w, h, options.JpegBackground ?? SKColors.White);
            using (var c = new SKCanvas(opaque)) c.DrawPixels(bmp, 0, 0);
            if (!ReferenceEquals(bmp, source)) bmp.Dispose();
            bmp = opaque;
        }
        try
        {
            var fmt = options.Format switch
            {
                ExportFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                ExportFormat.Webp => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png,
            };
            using var image = bmp.AsImage();
            using var data = image.Encode(fmt, Math.Clamp(options.Quality, 1, 100))
                             ?? throw new IOException($"Could not encode {options.Format}.");
            data.SaveTo(stream);
        }
        finally
        {
            if (!ReferenceEquals(bmp, source)) bmp.Dispose();
        }
    }

    public static ExportFormat FormatFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ExportFormat.Jpeg,
        ".webp" => ExportFormat.Webp,
        _ => ExportFormat.Png,
    };

    /// <summary>Encoded size in bytes (for the export dialog's estimate).</summary>
    public static long EstimateSize(Document doc, ExportOptions options)
    {
        using var flat = Compositor.Flatten(doc.Root, doc.Width, doc.Height);
        using var ms = new MemoryStream();
        Encode(flat, ms, options);
        return ms.Length;
    }
}
