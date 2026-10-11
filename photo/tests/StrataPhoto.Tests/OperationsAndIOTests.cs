using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.IO;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Operations;
using StrataPhoto.Core.Selections;
using static StrataPhoto.Tests.TestUtil;

namespace StrataPhoto.Tests;

public class OperationsAndIOTests
{
    private static SKColor ApplyTo(ImageOperation op, SKColor color, ParamValues? p = null)
    {
        var src = Bitmaps.Create(4, 4, color);
        var dst = Bitmaps.Copy(src);
        op.Apply(src, dst, new SKRectI(0, 0, 4, 4), p ?? op.Defaults());
        return Pixel(dst, 1, 1);
    }

    [Fact]
    public void AllOperationsAreIdentityAtDefaultsExceptInvertAndBw()
    {
        var c = new SKColor(200, 100, 50);
        foreach (var make in OperationCatalog.Adjustments)
        {
            var op = make();
            if (op is Invert or BlackAndWhite) continue;
            AssertColor(c, ApplyTo(op, c), 2);
        }
    }

    [Fact]
    public void InvertAndBlackWhite()
    {
        AssertColor(new SKColor(55, 155, 205), ApplyTo(new Invert(), new SKColor(200, 100, 50)));
        var bw = ApplyTo(new BlackAndWhite(), new SKColor(200, 100, 50));
        Assert.Equal(bw.Red, bw.Green);
        Assert.Equal(bw.Green, bw.Blue);
        // Pure white stays white, black stays black.
        AssertColor(SKColors.White, ApplyTo(new BlackAndWhite(), SKColors.White));
        AssertColor(SKColors.Black, ApplyTo(new BlackAndWhite(), SKColors.Black));
    }

    [Fact]
    public void BrightnessContrastLevelsCurvesExposure()
    {
        var gray = new SKColor(128, 128, 128);
        var bc = new BrightnessContrast();
        var p = bc.Defaults(); p["brightness"] = 100;
        Assert.True(ApplyTo(bc, gray, p).Red > 150);
        p["brightness"] = 0; p["contrast"] = 80;
        Assert.True(ApplyTo(bc, new SKColor(160, 160, 160), p).Red > 190);

        var levels = new Levels();
        var lp = levels.Defaults(); lp["inBlack"] = 100; lp["inWhite"] = 200;
        AssertColor(new SKColor(128, 128, 128), ApplyTo(levels, new SKColor(150, 150, 150), lp), 2); // (150-100)/(200-100)

        var curves = new Curves { Points = new[] { new SKPoint(0, 0), new SKPoint(128, 200), new SKPoint(255, 255) } };
        var cv = ApplyTo(curves, gray);
        AssertColor(new SKColor(200, 200, 200), cv, 2);
        var lut = Curves.Lut(curves.Points);
        for (int i = 1; i < 256; i++) Assert.True(lut[i] >= lut[i - 1], "curve must be monotone");

        var exp = new Exposure();
        var ep = exp.Defaults(); ep["exposure"] = 1;
        Assert.True(ApplyTo(exp, gray, ep).Red > 160);
    }

    [Fact]
    public void HueSaturationAndColorBalance()
    {
        var hs = new HueSaturation();
        var p = hs.Defaults(); p["hue"] = 120;
        AssertColor(SKColors.Lime, ApplyTo(hs, SKColors.Red, p), 2);
        p["hue"] = 0; p["saturation"] = -100;
        var g = ApplyTo(hs, new SKColor(200, 100, 50), p);
        Assert.Equal(g.Red, g.Green);

        var cb = new ColorBalance();
        var cp = cb.Defaults(); cp["mCR"] = 100; cp["preserve"] = 0;
        Assert.True(ApplyTo(cb, new SKColor(128, 128, 128), cp).Red > 150);
    }

    [Fact]
    public void FiltersRun()
    {
        var src = Bitmaps.Create(64, 64, SKColors.White);
        using (var c = new SKCanvas(src))
        using (var paint = new SKPaint { Color = SKColors.Black })
            c.DrawRect(0, 0, 32, 64, paint);
        var rect = new SKRectI(0, 0, 64, 64);
        foreach (var make in OperationCatalog.Filters)
        {
            var op = make();
            var dst = Bitmaps.Copy(src);
            op.Apply(src, dst, rect, op.Defaults());
            Assert.Equal(64, dst.Width);
        }
        var blur = Bitmaps.Copy(src);
        new GaussianBlur().Apply(src, blur, rect, new ParamValues { ["radius"] = 4 });
        Assert.InRange((int)Pixel(blur, 32, 32).Red, 60, 200); // edge softened
        var pix = Bitmaps.Copy(src);
        new Pixelate().Apply(src, pix, rect, new ParamValues { ["cell"] = 16 });
        Assert.Equal(Pixel(pix, 0, 0), Pixel(pix, 15, 15));
        var noise = Bitmaps.Copy(src);
        var np = new AddNoise().Defaults(); np["amount"] = 50;
        new AddNoise().Apply(src, noise, rect, np);
        Assert.NotEqual(Pixel(src, 40, 40), Pixel(noise, 40, 40));
    }

    [Fact]
    public void AdjustmentRespectsSelectionAndUndo()
    {
        var doc = Blank(100, 100);
        doc.Selection = SelectionOps.Rectangle(100, 100, new SKRect(0, 0, 50, 100));
        var inv = new Invert();
        Assert.True(PixelOps.Apply(doc, "Invert", (s, d, r) => inv.Apply(s, d, r, inv.Defaults())));
        var bg = (PixelLayer)doc.ActiveLayer!;
        AssertColor(SKColors.Black, Pixel(bg.Pixels, 10, 10));
        AssertColor(SKColors.White, Pixel(bg.Pixels, 80, 10));
        doc.History.Undo();
        AssertColor(SKColors.White, Pixel(bg.Pixels, 10, 10));
    }

    [Fact]
    public void ImageSizeCanvasSizeRotateFlip()
    {
        var doc = Blank(100, 50);
        var bg = (PixelLayer)doc.ActiveLayer!;
        using (var c = new SKCanvas(bg.Pixels))
        using (var p = new SKPaint { Color = SKColors.Red })
            c.DrawRect(0, 0, 10, 10, p); // red top-left corner
        doc.InvalidateAll();

        ImageTransforms.Rotate(doc, 90);
        Assert.Equal((50, 100), (doc.Width, doc.Height));
        AssertColor(SKColors.Red, Pixel(bg.Pixels, 45, 5)); // top-left -> top-right
        doc.History.Undo();
        Assert.Equal((100, 50), (doc.Width, doc.Height));

        ImageTransforms.Flip(doc, horizontal: true);
        AssertColor(SKColors.Red, Pixel(bg.Pixels, 95, 5));
        doc.History.Undo();

        ImageTransforms.ResizeImage(doc, 200, 100);
        Assert.Equal(200, bg.Pixels.Width);
        AssertColor(SKColors.Red, Pixel(bg.Pixels, 10, 10), 5);
        doc.History.Undo();

        ImageTransforms.ResizeCanvas(doc, 120, 70, 1, 1);
        Assert.Equal((120, 70), (doc.Width, doc.Height));
        AssertColor(SKColors.Red, Pixel(bg.Pixels, 15, 15));
        Assert.Equal(0, Pixel(bg.Pixels, 2, 2).Alpha);

        ImageTransforms.Rotate(doc, 30);
        Assert.True(doc.Width > 120);
    }

    [Fact]
    public void ProjectRoundTripKeepsLayersMasksAndText()
    {
        var doc = Blank(80, 60);
        var red = LayerOps.AddLayer(doc, "Red");
        red.Pixels.Erase(SKColors.Red);
        LayerOps.SetOpacity(doc, red, 0.6f);
        LayerOps.SetBlendMode(doc, red, BlendMode.Multiply);
        doc.Selection = SelectionOps.Rectangle(80, 60, new SKRect(0, 0, 40, 60));
        LayerOps.AddMask(doc);
        doc.Selection = Selection.None;
        var group = LayerOps.GroupActive(doc)!;
        LayerOps.Rename(doc, group, "Folder");
        var text = LayerOps.AddText(doc, new TextProps("Strata", "Arial", 20, SKColors.Blue, TextAlign.Center, true, true, 40, 10));
        LayerOps.SetVisible(doc, text, false);

        var path = TempFile(ProjectFile.Extension);
        ProjectFile.Save(doc, path);
        Assert.False(doc.IsDirty);
        var loaded = ProjectFile.Load(path);

        Assert.Equal((80, 60), (loaded.Width, loaded.Height));
        var names = loaded.AllLayers.Select(l => l.Name).ToList();
        Assert.Equal(doc.AllLayers.Select(l => l.Name), names);
        var lg = Assert.IsType<GroupLayer>(loaded.Root.Children.Single(l => l.Name == "Folder"));
        var lr = lg.Children.OfType<PixelLayer>().Single(l => l is not TextLayer); // the text went into the selected group
        Assert.Equal(0.6f, lr.Opacity, 2);
        Assert.Equal(BlendMode.Multiply, lr.BlendMode);
        Assert.NotNull(lr.Mask);
        var lt = Assert.IsType<TextLayer>(loaded.AllLayers.OfType<TextLayer>().Single());
        Assert.Equal("Strata", lt.Text.Text);
        Assert.True(lt.Text.Bold && lt.Text.Italic);
        Assert.Equal(TextAlign.Center, lt.Text.Align);
        Assert.False(lt.Visible);
        for (int x = 0; x < 80; x += 13)
            AssertColor(Composite(doc, x, 30), Composite(loaded, x, 30), 2);
    }

    [Theory]
    [InlineData(ExportFormat.Png, ".png")]
    [InlineData(ExportFormat.Jpeg, ".jpg")]
    [InlineData(ExportFormat.Webp, ".webp")]
    public void ExportAndReopen(ExportFormat format, string ext)
    {
        var doc = Blank(64, 48, SKColors.Orange);
        var path = TempFile(ext);
        ImageIO.Export(doc, path, new ExportOptions(format, 90, 32, 24));
        var reopened = ImageIO.Open(path);
        Assert.Equal((32, 24), (reopened.Width, reopened.Height));
        AssertColor(SKColors.Orange, Composite(reopened, 16, 12), format == ExportFormat.Png ? 1 : 12);
        Assert.True(ImageIO.EstimateSize(doc, new ExportOptions(format, 50)) > 0);
    }

    [Fact]
    public void JpegQualityAffectsSize()
    {
        var doc = Blank(256, 256);
        var bg = (PixelLayer)doc.ActiveLayer!;
        new AddNoise().Apply(bg.Pixels, bg.Pixels, doc.Bounds, new ParamValues { ["amount"] = 40, ["gaussian"] = 1 });
        doc.InvalidateAll();
        long low = ImageIO.EstimateSize(doc, new ExportOptions(ExportFormat.Jpeg, 20));
        long high = ImageIO.EstimateSize(doc, new ExportOptions(ExportFormat.Jpeg, 95));
        Assert.True(high > low * 2);
    }

    [Fact]
    public void OpensBmp()
    {
        var path = TempFile(".bmp");
        // Minimal 2x1 24-bit BMP: blue, red
        byte[] bmp =
        {
            0x42, 0x4D, 62, 0, 0, 0, 0, 0, 0, 0, 54, 0, 0, 0, 40, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 24, 0,
            0, 0, 0, 0, 8, 0, 0, 0, 0x13, 0x0B, 0, 0, 0x13, 0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            255, 0, 0, 0, 0, 255, 0, 0,
        };
        File.WriteAllBytes(path, bmp);
        var doc = ImageIO.Open(path);
        Assert.Equal((2, 1), (doc.Width, doc.Height));
        AssertColor(SKColors.Blue, Composite(doc, 0, 0));
        AssertColor(SKColors.Red, Composite(doc, 1, 0));
    }

    [Fact]
    public void HistoryTrimsOldEntriesAndTracksDirtyState()
    {
        var doc = Blank(10, 10);
        doc.History.MaxEntries = 5;
        for (int i = 0; i < 8; i++) LayerOps.AddLayer(doc);
        Assert.Equal(5, doc.History.Entries.Count);
        Assert.True(doc.IsDirty);
        doc.MarkSaved();
        Assert.False(doc.IsDirty);
        doc.History.Undo();
        Assert.True(doc.IsDirty);
        doc.History.Redo();
        LayerOps.AddLayer(doc);
        doc.History.Undo();
        doc.History.Undo();
        LayerOps.AddLayer(doc); // new branch drops redo
        Assert.False(doc.History.CanRedo);
    }
}
