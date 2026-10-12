using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using StrataPdf.Core.Model;
using StrataPdf.Core.Rendering;

namespace StrataPdf.Core.IO;

public enum CompressionLevel
{
    /// <summary>Compress all streams (lossless).</summary>
    Standard,
    /// <summary>Re-render every page as a JPEG image at <see cref="SaveOptions.RasterDpi"/> (text becomes pixels).</summary>
    Strong,
}

public sealed record SaveOptions(CompressionLevel Compression = CompressionLevel.Standard, int RasterDpi = 150,
    int JpegQuality = 75, IReadOnlyCollection<Guid>? OnlyPages = null);

/// <summary>Writes a <see cref="PdfWorkspace"/> to a PDF file with PDFsharp.</summary>
public static class PdfSaver
{
    public const double RedactionDpi = 200;

    static PdfSaver()
    {
        // PDFsharp needs to be told where fonts come from; on Windows use the installed fonts.
        if (OperatingSystem.IsWindows() && GlobalFontSettings.FontResolver == null)
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public static void Save(PdfWorkspace ws, string path, SaveOptions? options = null)
    {
        options ??= new SaveOptions();
        string tmp = path + ".saving.pdf";
        var pages = ws.Pages.Where(p => options.OnlyPages == null || options.OnlyPages.Contains(p.Id)).ToList();
        if (pages.Count == 0) throw new InvalidOperationException("There are no pages to save.");

        var sources = new Dictionary<string, PdfDocument>();
        try
        {
            using (var dst = new PdfDocument())
            {
                dst.Info.Creator = "Strata PDF";
                for (int i = 0; i < pages.Count; i++)
                {
                    var item = pages[i];
                    int indexInDoc = ws.Pages.ToList().IndexOf(item);
                    bool raster = options.Compression == CompressionLevel.Strong
                                  || item.Annotations.OfType<RedactionAnnotation>().Any();
                    if (raster)
                        AddRasterPage(dst, ws, item, indexInDoc, options);
                    else
                        AddVectorPage(dst, ws, item, indexInDoc, sources);
                }
                dst.Options.CompressContentStreams = true;
                dst.Options.NoCompression = false;
                dst.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                dst.Save(tmp);
            }
        }
        finally
        {
            foreach (var d in sources.Values) d.Dispose();
        }
        PdfRenderer.Forget(new[] { path });
        File.Move(tmp, path, overwrite: true);
    }

    // ------------------------------------------------------------------ pages

    private static void AddVectorPage(PdfDocument dst, PdfWorkspace ws, PageItem item, int indexInDoc,
        Dictionary<string, PdfDocument> sources)
    {
        if (!sources.TryGetValue(item.SourceId, out var src))
        {
            src = PdfReader.Open(ws.SourceOf(item).Path, PdfDocumentOpenMode.Import);
            sources[item.SourceId] = src;
        }
        var page = dst.AddPage(src.Pages[item.SourceIndex]);
        int r0 = ((page.Rotate % 360) + 360) % 360;
        var media = page.MediaBox;
        var crop = page.Elements.ContainsKey("/CropBox") ? page.CropBox : media;
        double w = crop.Width, h = crop.Height;  // unrotated (as stored) page size

        // Draw with /Rotate cleared so XGraphics works in the page's stored orientation, then
        // map Strata's display coordinates onto it ourselves. (/Rotate is set through the
        // dictionary: PDFsharp's Orientation setter would swap the MediaBox.)
        page.Elements.SetInteger("/Rotate", 0);
        var toStored = XMatrix.Identity;
        toStored.TranslatePrepend(crop.X1 - media.X1, media.Y2 - crop.Y2);
        toStored.Prepend(DisplayToStored(r0, w, h));
        DrawOverlay(page, item, indexInDoc, ws, toStored, item.BaseWidth, item.BaseHeight);
        AddNotes(page, item, p =>
        {
            var s = DisplayToStored(r0, w, h).Transform(new XPoint(p.X, p.Y));
            return new XPoint(crop.X1 + s.X, crop.Y2 - s.Y);  // PDF user space, y up
        });
        page.Elements.SetInteger("/Rotate", (r0 + item.Rotation) % 360);
    }

    /// <summary>Page replaced by an image: used for redaction (removes the content under the boxes)
    /// and for strong compression.</summary>
    private static void AddRasterPage(PdfDocument dst, PdfWorkspace ws, PageItem item, int indexInDoc, SaveOptions options)
    {
        bool redact = item.Annotations.OfType<RedactionAnnotation>().Any();
        double dpi = redact ? Math.Max(RedactionDpi, options.RasterDpi) : options.RasterDpi;
        double scale = dpi / 72.0;
        using var bmp = PdfRenderer.RenderPage(ws.SourceOf(item).Path, item.SourceIndex, scale);
        using (var c = new SKCanvas(bmp))
        using (var black = new SKPaint { Color = SKColors.Black })
            foreach (var r in item.Annotations.OfType<RedactionAnnotation>())
                c.DrawRect(SKRect.Create((float)(r.Rect.X * scale), (float)(r.Rect.Y * scale),
                    (float)(r.Rect.Width * scale), (float)(r.Rect.Height * scale)), black);
        using var image = SKImage.FromBitmap(bmp);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, redact ? 90 : options.JpegQuality);
        using var ms = new MemoryStream(jpeg.ToArray());

        var page = dst.AddPage();
        double w = item.BaseWidth, h = item.BaseHeight;
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(w, h));
        using (var gfx = XGraphics.FromPdfPage(page))
        using (var x = XImage.FromStream(ms))
            gfx.DrawImage(x, 0, 0, w, h);
        DrawOverlay(page, item, indexInDoc, ws, XMatrix.Identity, w, h);
        AddNotes(page, item, p => new XPoint(p.X, h - p.Y));
        page.Elements.SetInteger("/Rotate", item.Rotation);
    }

    /// <summary>
    /// Maps display coordinates (y down, the page shown rotated clockwise by <paramref name="rotation"/>)
    /// to the unrotated page's coordinates (y down, size <paramref name="w"/> × <paramref name="h"/>).
    /// </summary>
    internal static XMatrix DisplayToStored(int rotation, double w, double h) => (((rotation % 360) + 360) % 360) switch
    {
        90 => new XMatrix(0, -1, 1, 0, 0, h),
        180 => new XMatrix(-1, 0, 0, -1, w, h),
        270 => new XMatrix(0, 1, -1, 0, w, 0),
        _ => XMatrix.Identity,
    };

    // ------------------------------------------------------------------ drawing

    private static void DrawOverlay(PdfPage page, PageItem item, int indexInDoc, PdfWorkspace ws,
        XMatrix displayToGfx, double baseW, double baseH)
    {
        bool hasStamps = ws.PageNumbers.AppliesTo(indexInDoc) || ws.Watermark.Enabled;
        if (item.Annotations.Count == 0 && !hasStamps) return;
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        gfx.MultiplyTransform(displayToGfx, XMatrixOrder.Prepend);
        foreach (var a in item.Annotations) DrawAnnotation(gfx, a);
        if (!hasStamps) return;
        // Stamps are placed on the page as finally shown (after Strata's extra rotation).
        gfx.Save();
        gfx.MultiplyTransform(DisplayToStored(item.Rotation, baseW, baseH), XMatrixOrder.Prepend);
        DrawStamps(gfx, ws, indexInDoc, item.Width, item.Height);
        gfx.Restore();
    }

    internal static XColor ToX(Rgba c) => XColor.FromArgb((int)Math.Round(c.Opacity * 255), c.R, c.G, c.B);

    private static void DrawAnnotation(XGraphics gfx, Annotation a)
    {
        switch (a)
        {
            case HighlightAnnotation hl:
                gfx.DrawRectangle(new XSolidBrush(ToX(hl.Color)), hl.Rect.X, hl.Rect.Y, hl.Rect.Width, hl.Rect.Height);
                break;
            case InkAnnotation ink when ink.Points.Count > 0:
            {
                var pen = new XPen(ToX(ink.Color), ink.Width) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
                if (ink.Points.Count == 1)
                {
                    var p = ink.Points[0];
                    gfx.DrawEllipse(new XSolidBrush(ToX(ink.Color)), p.X - ink.Width / 2, p.Y - ink.Width / 2, ink.Width, ink.Width);
                }
                else gfx.DrawLines(pen, ink.Points.Select(p => new XPoint(p.X, p.Y)).ToArray());
                break;
            }
            case TextBoxAnnotation t:
            {
                if (t.Border)
                    gfx.DrawRectangle(new XPen(ToX(t.Color), 0.8), t.Rect.X, t.Rect.Y, t.Rect.Width, t.Rect.Height);
                var font = new XFont(t.FontFamily, t.FontSize, XFontStyleEx.Regular);
                var tf = new XTextFormatter(gfx);
                tf.DrawString(t.Text, font, new XSolidBrush(ToX(t.Color)),
                    new XRect(t.Rect.X + 2, t.Rect.Y + 1, Math.Max(1, t.Rect.Width - 4), Math.Max(1, t.Rect.Height)), XStringFormats.TopLeft);
                break;
            }
            case ShapeAnnotation s:
                DrawShape(gfx, s);
                break;
            case ImageAnnotation img:
            {
                using var ms = new MemoryStream(img.Png);
                using var x = XImage.FromStream(ms);
                gfx.DrawImage(x, img.Rect.X, img.Rect.Y, img.Rect.Width, img.Rect.Height);
                break;
            }
            case RedactionAnnotation r:  // already burned into the page image; draw the box too
                gfx.DrawRectangle(XBrushes.Black, r.Rect.X, r.Rect.Y, r.Rect.Width, r.Rect.Height);
                break;
        }
    }

    private static void DrawShape(XGraphics gfx, ShapeAnnotation s)
    {
        var pen = new XPen(ToX(s.Color), s.Width) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
        var brush = s.Filled ? new XSolidBrush(ToX(s.Color)) : null;
        var r = PageRect.FromPoints(s.From, s.To);
        switch (s.Kind)
        {
            case ShapeKind.Rectangle:
                if (brush != null) gfx.DrawRectangle(brush, r.X, r.Y, r.Width, r.Height);
                gfx.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                break;
            case ShapeKind.Ellipse:
                if (brush != null) gfx.DrawEllipse(brush, r.X, r.Y, r.Width, r.Height);
                gfx.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                break;
            default:
                gfx.DrawLine(pen, s.From.X, s.From.Y, s.To.X, s.To.Y);
                if (s.Kind == ShapeKind.Arrow)
                    gfx.DrawPolygon(new XSolidBrush(ToX(s.Color)), ArrowHead(s.From, s.To, s.Width).Select(p => new XPoint(p.X, p.Y)).ToArray(), XFillMode.Winding);
                break;
        }
    }

    /// <summary>Triangle at <paramref name="to"/> pointing away from <paramref name="from"/>.</summary>
    public static PagePoint[] ArrowHead(PagePoint from, PagePoint to, double width)
    {
        double ang = Math.Atan2(to.Y - from.Y, to.X - from.X);
        double len = Math.Max(8, width * 4), spread = Math.PI / 7;
        return new[]
        {
            to,
            new PagePoint(to.X - len * Math.Cos(ang - spread), to.Y - len * Math.Sin(ang - spread)),
            new PagePoint(to.X - len * Math.Cos(ang + spread), to.Y - len * Math.Sin(ang + spread)),
        };
    }

    private static void DrawStamps(XGraphics gfx, PdfWorkspace ws, int index, double w, double h)
    {
        var wm = ws.Watermark;
        if (wm.Enabled && !string.IsNullOrWhiteSpace(wm.Text))
        {
            var font = new XFont("Arial", wm.FontSize, XFontStyleEx.Bold);
            gfx.Save();
            gfx.RotateAtTransform(wm.Angle, new XPoint(w / 2, h / 2));
            gfx.DrawString(wm.Text, font, new XSolidBrush(ToX(wm.Color)), new XRect(0, 0, w, h), XStringFormats.Center);
            gfx.Restore();
        }
        var pn = ws.PageNumbers;
        if (pn.AppliesTo(index))
        {
            var font = new XFont("Arial", pn.FontSize, XFontStyleEx.Regular);
            string text = pn.TextFor(index, ws.Pages.Count);
            var size = gfx.MeasureString(text, font);
            var (x, y) = StampOrigin(pn.Position, w, h, size.Width, size.Height, pn.Margin);
            gfx.DrawString(text, font, new XSolidBrush(ToX(pn.Color)), new XRect(x, y, size.Width, size.Height), XStringFormats.TopLeft);
        }
    }

    /// <summary>Top-left of a stamp of size (tw, th) on a page of size (w, h).</summary>
    public static (double X, double Y) StampOrigin(StampPosition pos, double w, double h, double tw, double th, double margin)
    {
        double y = pos is StampPosition.TopCenter or StampPosition.TopLeft or StampPosition.TopRight ? margin : h - margin - th;
        double x = pos switch
        {
            StampPosition.BottomLeft or StampPosition.TopLeft => margin,
            StampPosition.BottomRight or StampPosition.TopRight => w - margin - tw,
            _ => (w - tw) / 2,
        };
        return (x, y);
    }

    private static void AddNotes(PdfPage page, PageItem item, Func<PagePoint, XPoint> toUser)
    {
        foreach (var n in item.Annotations.OfType<NoteAnnotation>())
        {
            var a = toUser(n.At);
            var b = toUser(new PagePoint(n.At.X + NoteAnnotation.IconSize, n.At.Y + NoteAnnotation.IconSize));
            var note = new PdfTextAnnotation
            {
                Title = n.Author,
                Contents = n.Text,
                Icon = PdfTextAnnotationIcon.Note,
                Color = XColor.FromArgb(255, 214, 0),
            };
            note.Rectangle = new PdfRectangle(new XPoint(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
                new XPoint(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            page.Annotations.Add(note);
        }
    }
}
