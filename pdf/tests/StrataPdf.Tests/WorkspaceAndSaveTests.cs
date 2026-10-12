using PdfSharp.Pdf.IO;
using SkiaSharp;
using StrataPdf.Core.IO;
using StrataPdf.Core.Model;
using StrataPdf.Core.Rendering;

namespace StrataPdf.Tests;

public class WorkspaceAndSaveTests
{
    private static string Text(string path, int page) => PdfRenderer.PageText(path, page);

    [Fact]
    public void OpenRenderAndPageOperationsWithUndo()
    {
        using var ws = PdfWorkspace.Open(TestPdfs.Make("a.pdf", 3));
        Assert.Equal(3, ws.Pages.Count);
        Assert.Equal((300.0, 400.0), (ws.Pages[0].Width, ws.Pages[0].Height));
        Assert.False(ws.IsDirty);
        using (var bmp = PdfRenderer.RenderPage(ws.SourceOf(ws.Pages[0]).Path, 0, 0.5))
            Assert.Equal((150, 200), (bmp.Width, bmp.Height));

        var p = ws.Pages.Select(x => x.Id).ToArray();
        ws.RotatePages(new[] { p[0] }, 90);
        Assert.Equal((400.0, 300.0), (ws.Pages[0].Width, ws.Pages[0].Height));
        ws.MovePages(new[] { p[2] }, 0);
        Assert.Equal(new[] { p[2], p[0], p[1] }, ws.Pages.Select(x => x.Id));
        Assert.True(ws.DeletePages(new[] { p[1] }));
        Assert.Equal(2, ws.Pages.Count);
        Assert.False(ws.DeletePages(ws.Pages.Select(x => x.Id)));  // can't delete every page
        Assert.True(ws.IsDirty);

        ws.Undo();
        Assert.Equal(3, ws.Pages.Count);
        ws.Undo();
        ws.Undo();
        Assert.Equal(0, ws.Pages[0].Rotation);
        Assert.False(ws.IsDirty);
        ws.Redo();
        Assert.Equal(90, ws.Pages[0].Rotation);

        var added = ws.InsertPdf(TestPdfs.Make("b.pdf", 2, extra: "from B"), 1);
        Assert.Equal(5, ws.Pages.Count);
        Assert.Equal(added[0].Id, ws.Pages[1].Id);
    }

    [Fact]
    public void MergeReorderRotateDeleteSaveRoundTrip()
    {
        using var ws = PdfWorkspace.Open(TestPdfs.Make("a.pdf", 2, extra: "alpha"));
        ws.InsertPdf(TestPdfs.Make("b.pdf", 2, extra: "beta"));
        var ids = ws.Pages.Select(p => p.Id).ToArray();
        ws.MovePages(new[] { ids[3] }, 0);       // b2, a1, a2, b1
        ws.DeletePages(new[] { ids[1] });        // b2, a1, b1
        ws.RotatePages(new[] { ids[3] }, 90);    // the page now first in the document
        string outPath = TestPdfs.Temp("out.pdf");
        PdfSaver.Save(ws, outPath);
        Assert.Equal(3, PdfRenderer.PageCount(outPath));
        Assert.Contains("Page 2 beta", Text(outPath, 0));
        Assert.Contains("Page 1 alpha", Text(outPath, 1));
        Assert.Contains("Page 1 beta", Text(outPath, 2));
        var sizes = PdfRenderer.PageSizes(outPath);
        Assert.Equal((400.0, 300.0), (Math.Round(sizes[0].Width), Math.Round(sizes[0].Height)));  // rotated
        using var check = PdfReader.Open(outPath, PdfDocumentOpenMode.Import);
        Assert.Equal(90, check.Pages[0].Rotate);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 0)]
    [InlineData(180, 0)]
    [InlineData(270, 0)]
    [InlineData(0, 90)]
    [InlineData(90, 270)]
    [InlineData(270, 180)]
    public void AnnotationsLandWhereTheyWereDrawn(int storedRotate, int extraRotation)
    {
        // A landscape-stored page with its own /Rotate, plus a rotation added in Strata PDF.
        using var ws = PdfWorkspace.Open(TestPdfs.Make("r.pdf", 1, 400, 200, storedRotate));
        var page = ws.Pages[0];
        var rect = new PageRect(10, 20, 40, 30);  // display space of the page as opened
        ws.AddAnnotation(page.Id, new HighlightAnnotation(rect, new Rgba(255, 0, 0)));
        ws.RotatePages(new[] { page.Id }, extraRotation);
        page = ws.Pages[0];
        string outPath = TestPdfs.Temp("r-out.pdf");
        PdfSaver.Save(ws, outPath);

        // Where should the centre of the box be on the saved page (as PDFium displays it)?
        double bx = rect.X + rect.Width / 2, by = rect.Y + rect.Height / 2;
        double bw = page.BaseWidth, bh = page.BaseHeight;
        (double x, double y) expected = extraRotation switch
        {
            90 => (bh - by, bx),
            180 => (bw - bx, bh - by),
            270 => (by, bw - bx),
            _ => (bx, by),
        };
        using var bmp = PdfRenderer.RenderPage(outPath, 0, 1);
        Assert.Equal((Math.Round(page.Width), Math.Round(page.Height)),
            (Math.Round(bmp.Width * 1.0), Math.Round(bmp.Height * 1.0)));  // page size survives
        Assert.InRange(expected.x, 0, bmp.Width - 1);
        Assert.InRange(expected.y, 0, bmp.Height - 1);
        var c = bmp.GetPixel((int)expected.x, (int)expected.y);
        Assert.True(c.Red > 200 && c.Green < 60 && c.Blue < 60, $"expected red at {expected}, got {c}");
        // and not in the opposite corner
        var far = bmp.GetPixel(bmp.Width - 1 - (int)expected.x, bmp.Height - 1 - (int)expected.y);
        Assert.True(far.Green > 200, $"unexpected colour {far}");
    }

    [Fact]
    public void AllAnnotationKindsSave()
    {
        using var ws = PdfWorkspace.Open(TestPdfs.Make("a.pdf", 1));
        var id = ws.Pages[0].Id;
        ws.AddAnnotation(id, new InkAnnotation(new[] { new PagePoint(10, 100), new PagePoint(60, 120), new PagePoint(90, 90) }, Rgba.Red, 3));
        ws.AddAnnotation(id, new TextBoxAnnotation(new PageRect(20, 200, 200, 40), "Reviewed by Strata", 14, Rgba.Black, Border: true));
        ws.AddAnnotation(id, new ShapeAnnotation(ShapeKind.Rectangle, new(100, 250), new(200, 300), Rgba.Red, 2));
        ws.AddAnnotation(id, new ShapeAnnotation(ShapeKind.Ellipse, new(100, 300), new(200, 350), Rgba.Red, 2, true));
        ws.AddAnnotation(id, new ShapeAnnotation(ShapeKind.Arrow, new(20, 350), new(90, 380), Rgba.Black, 2));
        ws.AddAnnotation(id, new NoteAnnotation(new PagePoint(250, 30), "Check this"));
        using var sig = new SKBitmap(60, 20);
        sig.Erase(SKColors.Blue);
        ws.AddAnnotation(id, new ImageAnnotation(new PageRect(200, 360, 60, 20), SKImage.FromBitmap(sig).Encode().ToArray()));
        string outPath = TestPdfs.Temp("ann.pdf");
        PdfSaver.Save(ws, outPath);
        Assert.Contains("Reviewed by Strata", Text(outPath, 0));
        using var doc = PdfReader.Open(outPath, PdfDocumentOpenMode.Import);
        var annots = doc.Pages[0].Annotations;
        Assert.Equal(1, annots.Count);  // the sticky note is a real PDF annotation
        using var bmp = PdfRenderer.RenderPage(outPath, 0, 1);
        var blue = bmp.GetPixel(230, 370);
        Assert.True(blue.Blue > 200 && blue.Red < 60, $"signature image missing: {blue}");
    }

    [Fact]
    public void PageNumbersAndWatermark()
    {
        using var ws = PdfWorkspace.Open(TestPdfs.Make("a.pdf", 3));
        ws.SetPageNumbers(new PageNumberSettings(true, "Page {n} of {total}", SkipFirstPage: true));
        ws.SetWatermark(new WatermarkSettings(true, "DRAFT"));
        ws.RotatePages(new[] { ws.Pages[2].Id }, 90);
        string outPath = TestPdfs.Temp("stamps.pdf");
        PdfSaver.Save(ws, outPath);
        Assert.DoesNotContain("of", Text(outPath, 0).Replace("Page 1", ""));
        Assert.Contains("Page 1 of 2", Text(outPath, 1));
        Assert.Contains("Page 2 of 2", Text(outPath, 2));
        Assert.Contains("DRAFT", Text(outPath, 0));
        Assert.Equal("Page 3 of 3", new PageNumberSettings(true).TextFor(2, 3));
    }

    [Fact]
    public void RedactionRemovesTheUnderlyingText()
    {
        using var ws = PdfWorkspace.Open(TestPdfs.Make("secret.pdf", 2, extra: "SECRET-1234"));
        ws.AddAnnotation(ws.Pages[0].Id, new RedactionAnnotation(new PageRect(10, 20, 280, 30)));
        string outPath = TestPdfs.Temp("redacted.pdf");
        PdfSaver.Save(ws, outPath);
        Assert.DoesNotContain("SECRET", Text(outPath, 0));  // gone, not just covered
        Assert.Contains("SECRET-1234", Text(outPath, 1));   // other pages untouched
        using var bmp = PdfRenderer.RenderPage(outPath, 0, 1);
        Assert.True(bmp.GetPixel(100, 35).Red < 30);
    }

    [Fact]
    public void ExtractPagesAndCompress()
    {
        using var ws = PdfWorkspace.Open(TestPdfs.Make("a.pdf", 4));
        string part = TestPdfs.Temp("part.pdf");
        PdfSaver.Save(ws, part, new SaveOptions(OnlyPages: new[] { ws.Pages[1].Id, ws.Pages[3].Id }));
        Assert.Equal(2, PdfRenderer.PageCount(part));
        Assert.Contains("Page 4", Text(part, 1));
        string strong = TestPdfs.Temp("strong.pdf");
        PdfSaver.Save(ws, strong, new SaveOptions(CompressionLevel.Strong, RasterDpi: 72));
        Assert.Equal(4, PdfRenderer.PageCount(strong));
        Assert.Equal("", Text(strong, 0).Trim());  // pages are images now
    }

    [Fact]
    public void FormFieldsReadAndFill()
    {
        string path = TestPdfs.WithForm();
        var fields = PdfForms.Read(path);
        Assert.Contains(fields, f => f.Name == "name" && f.Kind == FieldKind.Text && f.Value == "Ann");
        Assert.Contains(fields, f => f.Name == "agree" && f.Kind == FieldKind.CheckBox && f.Value == "false");
        string filled = TestPdfs.Temp("filled.pdf");
        PdfForms.Fill(path, filled, new Dictionary<string, string> { ["name"] = "Bob Strata", ["agree"] = "true" });
        var again = PdfForms.Read(filled);
        Assert.Equal("Bob Strata", again.Single(f => f.Name == "name").Value);
        Assert.Equal("true", again.Single(f => f.Name == "agree").Value);

        // in a workspace: filling replaces the source and is undoable
        using var ws = PdfWorkspace.Open(path);
        var src = ws.Pages[0].SourceId;
        ws.ReplaceSourceFile(src, filled, "Fill Form");
        Assert.NotEqual(src, ws.Pages[0].SourceId);
        ws.Undo();
        Assert.Equal(src, ws.Pages[0].SourceId);
    }

    [Fact]
    public void ExtractImages()
    {
        string path = TestPdfs.WithImages();
        var dir = Path.Combine(Path.GetTempPath(), "strata-pdf-tests", "img-" + Guid.NewGuid().ToString("N")[..6]);
        var images = PdfImages.Extract(path, new[] { 1 }, dir, out int skipped);
        Assert.Equal(2, images.Count);
        Assert.Contains(images, i => i.FileName.EndsWith(".jpg"));
        var png = images.Single(i => i.FileName.EndsWith(".png"));
        using var bmp = SKBitmap.Decode(png.FileName);
        Assert.Equal((40, 24), (bmp.Width, bmp.Height));
        Assert.True(bmp.GetPixel(5, 5).Blue > 200);
        Assert.Equal(0, skipped);
    }
}
