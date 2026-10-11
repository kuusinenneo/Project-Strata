using System.Diagnostics;
using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Painting;
using StrataPhoto.Core.Selections;
using StrataPhoto.Core.Tools;
using Xunit.Abstractions;
using static StrataPhoto.Tests.TestUtil;

namespace StrataPhoto.Tests;

public class PaintingAndSelectionTests
{
    private readonly ITestOutputHelper _out;
    public PaintingAndSelectionTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Alpha8BitmapIsDrawnInThePaintColour()
    {
        // The brush engine and selection fills rely on this Skia behaviour.
        var mask = Bitmaps.CreateAlpha(4, 4, 255);
        var dst = Bitmaps.Create(4, 4);
        using (var c = new SKCanvas(dst))
        using (var p = new SKPaint { Color = SKColors.Red })
            c.DrawBitmap(mask, 0, 0, p);
        AssertColor(SKColors.Red, Pixel(dst, 1, 1));
    }

    [Fact]
    public void Alpha8BitmapIsDrawnWithThePaintShader()
    {
        var mask = Bitmaps.CreateAlpha(4, 4, 255);
        var src = Bitmaps.Create(4, 4, SKColors.Lime);
        var dst = Bitmaps.Create(4, 4);
        using (var c = new SKCanvas(dst))
        using (var p = new SKPaint { Shader = SKShader.CreateBitmap(src) })
            c.DrawBitmap(mask, 0, 0, p);
        AssertColor(SKColors.Lime, Pixel(dst, 1, 1));
    }

    [Fact]
    public void BrushStrokePaintsAndUndoes()
    {
        var doc = Blank(100, 100);
        var layer = LayerOps.AddLayer(doc);
        var brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1 };
        var s = StrokeSession.Begin(doc, brush, StrokeMode.Paint, SKColors.Blue, new SKPoint(10, 50), 1)!;
        s.MoveTo(new SKPoint(90, 50), 1);
        s.End();
        AssertColor(SKColors.Blue, Pixel(layer.Pixels, 50, 50));
        Assert.Equal(0, Pixel(layer.Pixels, 50, 20).Alpha);
        AssertColor(SKColors.Blue, Composite(doc, 50, 50));
        Assert.Equal("Brush", doc.History.Entries[^1].Name);
        doc.History.Undo();
        Assert.Equal(0, Pixel(layer.Pixels, 50, 50).Alpha);
        AssertColor(SKColors.White, Composite(doc, 50, 50));
        doc.History.Redo();
        AssertColor(SKColors.Blue, Pixel(layer.Pixels, 50, 50));
    }

    [Fact]
    public void OpacityCapsTheWholeStroke()
    {
        var doc = Blank(100, 100);
        var layer = LayerOps.AddLayer(doc);
        var brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 0.5f, Flow = 1 };
        var s = StrokeSession.Begin(doc, brush, StrokeMode.Paint, SKColors.Black, new SKPoint(50, 50), 1)!;
        for (int i = 0; i < 10; i++) s.MoveTo(new SKPoint(50 + (i % 2), 50), 1); // scrub over the same spot
        s.End();
        Assert.InRange((int)Pixel(layer.Pixels, 50, 50).Alpha, 125, 130);
    }

    [Fact]
    public void LowFlowBuildsUpWithinAStroke()
    {
        var doc = Blank(100, 100);
        var layer = LayerOps.AddLayer(doc);
        var brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 0.2f, Spacing = 0.05f };
        var s = StrokeSession.Begin(doc, brush, StrokeMode.Paint, SKColors.Black, new SKPoint(20, 50), 1)!;
        s.MoveTo(new SKPoint(80, 50), 1);
        s.End();
        Assert.True(Pixel(layer.Pixels, 50, 50).Alpha > 200, "overlapping low-flow dabs should accumulate");
    }

    [Fact]
    public void PressureScalesBrushSize()
    {
        var doc = Blank(200, 100);
        var layer = LayerOps.AddLayer(doc);
        var brush = new BrushSettings { Size = 40, Hardness = 1, PressureSize = true };
        var s = StrokeSession.Begin(doc, brush, StrokeMode.Paint, SKColors.Black, new SKPoint(20, 50), 0.25f)!;
        s.MoveTo(new SKPoint(180, 50), 1f);
        s.End();
        // Thin at the light-pressure start, full width at the end.
        Assert.Equal(0, Pixel(layer.Pixels, 22, 50 - 15).Alpha);
        Assert.True(Pixel(layer.Pixels, 178, 50 - 15).Alpha > 0);
    }

    [Fact]
    public void EraserRemovesAlphaAndRespectsSelection()
    {
        var doc = Blank(100, 100);
        var bg = (PixelLayer)doc.ActiveLayer!;
        doc.Selection = SelectionOps.Rectangle(100, 100, new SKRect(0, 0, 50, 100));
        var brush = new BrushSettings { Size = 30, Hardness = 1 };
        var s = StrokeSession.Begin(doc, brush, StrokeMode.Erase, SKColors.Black, new SKPoint(10, 50), 1)!;
        s.MoveTo(new SKPoint(90, 50), 1);
        s.End();
        Assert.Equal(0, Pixel(bg.Pixels, 30, 50).Alpha);
        Assert.Equal(255, Pixel(bg.Pixels, 70, 50).Alpha); // outside the selection
    }

    [Fact]
    public void BrushOnMaskHidesLayer()
    {
        var doc = Blank(100, 100);
        var red = LayerOps.AddLayer(doc);
        red.Pixels.Erase(SKColors.Red);
        LayerOps.AddMask(doc);
        Assert.True(doc.EditingMask);
        var s = StrokeSession.Begin(doc, new BrushSettings { Size = 20, Hardness = 1 }, StrokeMode.Paint, SKColors.Black,
            new SKPoint(50, 50), 1)!;
        s.End();
        AssertColor(SKColors.White, Composite(doc, 50, 50));
        AssertColor(SKColors.Red, Composite(doc, 10, 10));
        AssertColor(SKColors.Red, Pixel(red.Pixels, 50, 50)); // pixels untouched
    }

    [Fact]
    public void CloneStampCopiesFromSourceOffset()
    {
        var doc = Blank(100, 100);
        var bg = (PixelLayer)doc.ActiveLayer!;
        using (var c = new SKCanvas(bg.Pixels))
        using (var p = new SKPaint { Color = SKColors.Green })
            c.DrawRect(0, 0, 20, 20, p);
        doc.InvalidateAll();
        var tool = new BrushTool(ToolKind.CloneStamp);
        var settings = new ToolSettings();
        settings.Clone.Size = 10;
        settings.Clone.Hardness = 1;
        var ctx = new ToolContext(doc, settings);
        tool.Down(ctx, new PointerInput(new SKPoint(10, 10), Alt: true));
        tool.Down(ctx, new PointerInput(new SKPoint(60, 60)));
        tool.Up(ctx, new PointerInput(new SKPoint(60, 60)));
        AssertColor(SKColors.Green, Pixel(bg.Pixels, 60, 60));
    }

    [Fact]
    public void LockedAndTextLayersRefusePainting()
    {
        var doc = Blank(50, 50);
        var l = LayerOps.AddLayer(doc);
        LayerOps.SetLocked(doc, l, true);
        Assert.Null(StrokeSession.Begin(doc, new BrushSettings(), StrokeMode.Paint, SKColors.Black, new SKPoint(5, 5), 1));
        LayerOps.AddText(doc, TextProps.Default(5, 5, SKColors.Black));
        Assert.Contains("rasterized", PixelOps.CannotEditReason(doc));
        LayerOps.Rasterize(doc);
        Assert.Null(PixelOps.CannotEditReason(doc));
    }

    [Fact]
    public void SelectionOperations()
    {
        int w = 100, h = 100;
        var rect = SelectionOps.Rectangle(w, h, new SKRect(10, 10, 50, 50));
        Assert.Equal(new SKRectI(10, 10, 50, 50), rect.Bounds);
        Assert.Equal(255, rect.CoverageAt(20, 20));
        Assert.Equal(0, rect.CoverageAt(60, 60));

        var added = SelectionOps.Rectangle(w, h, new SKRect(40, 40, 90, 90), rect, SelectionMode.Add);
        Assert.Equal(new SKRectI(10, 10, 90, 90), added.Bounds);
        var sub = SelectionOps.Rectangle(w, h, new SKRect(0, 0, 30, 100), rect, SelectionMode.Subtract);
        Assert.Equal(new SKRectI(30, 10, 50, 50), sub.Bounds);
        var inter = SelectionOps.Rectangle(w, h, new SKRect(40, 40, 90, 90), rect, SelectionMode.Intersect);
        Assert.Equal(new SKRectI(40, 40, 50, 50), inter.Bounds);

        var inv = SelectionOps.Invert(rect, w, h);
        Assert.Equal(0, inv.CoverageAt(20, 20));
        Assert.Equal(255, inv.CoverageAt(70, 70));

        var all = SelectionOps.All(w, h);
        Assert.Equal(new SKRectI(0, 0, w, h), all.Bounds);

        var ell = SelectionOps.Ellipse(w, h, new SKRect(0, 0, 100, 100));
        Assert.Equal(255, ell.CoverageAt(50, 50));
        Assert.Equal(0, ell.CoverageAt(2, 2));

        var lasso = SelectionOps.Polygon(w, h, new[] { new SKPoint(10, 10), new SKPoint(90, 10), new SKPoint(50, 90) });
        Assert.Equal(255, lasso.CoverageAt(50, 30));
        Assert.Equal(0, lasso.CoverageAt(15, 80));

        var feathered = SelectionOps.Feather(rect, 10);
        Assert.InRange(feathered.CoverageAt(10, 30), 60, 200); // soft edge
        Assert.Equal(255, feathered.CoverageAt(30, 30));
        Assert.True(feathered.Bounds.Left < 10);

        Assert.False(rect.Outline.IsEmpty);
    }

    [Fact]
    public void MagicWandSelectsSimilarColours()
    {
        var bmp = Bitmaps.Create(100, 100, SKColors.White);
        using (var c = new SKCanvas(bmp))
        using (var p = new SKPaint { Color = SKColors.Red })
        {
            c.DrawRect(10, 10, 30, 30, p);
            c.DrawRect(60, 60, 30, 30, p);
        }
        var contiguous = SelectionOps.MagicWand(bmp, 20, 20, 10, contiguous: true, antiAlias: false);
        Assert.Equal(new SKRectI(10, 10, 40, 40), contiguous.Bounds);
        var global = SelectionOps.MagicWand(bmp, 20, 20, 10, contiguous: false, antiAlias: false);
        Assert.Equal(new SKRectI(10, 10, 90, 90), global.Bounds);
        Assert.Equal(0, global.CoverageAt(50, 50));
    }

    [Fact]
    public void BucketFillFillsRegionOnly()
    {
        var doc = Blank(100, 100);
        var bg = (PixelLayer)doc.ActiveLayer!;
        using (var c = new SKCanvas(bg.Pixels))
        using (var p = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            c.DrawRect(20, 20, 40, 40, p);
        Assert.True(PaintOps.BucketFill(doc, 40, 40, SKColors.Yellow, 10, true, false));
        AssertColor(SKColors.Yellow, Pixel(bg.Pixels, 40, 40));
        AssertColor(SKColors.White, Pixel(bg.Pixels, 5, 5));
        doc.History.Undo();
        AssertColor(SKColors.White, Pixel(bg.Pixels, 40, 40));
    }

    [Fact]
    public void GradientGoesFromForegroundToBackground()
    {
        var doc = Blank(100, 10);
        var layer = LayerOps.AddLayer(doc);
        PaintOps.Gradient(doc, new SKPoint(0, 5), new SKPoint(100, 5), SKColors.Black, SKColors.White, GradientType.Linear);
        Assert.True(Pixel(layer.Pixels, 2, 5).Red < 20);
        Assert.True(Pixel(layer.Pixels, 97, 5).Red > 235);
        Assert.InRange((int)Pixel(layer.Pixels, 50, 5).Red, 110, 145);
    }

    [Fact]
    public void ShapesGoOnNewLayers()
    {
        var doc = Blank(100, 100);
        var rect = PaintOps.Shape(doc, ShapeKind.Rectangle, new SKPoint(10, 10), new SKPoint(50, 50), SKColors.Blue, true, 1)!;
        Assert.Equal("Rectangle 1", rect.Name);
        AssertColor(SKColors.Blue, Pixel(rect.Pixels, 30, 30));
        var ell = PaintOps.Shape(doc, ShapeKind.Ellipse, new SKPoint(0, 0), new SKPoint(100, 100), SKColors.Red, false, 4)!;
        Assert.Equal(0, Pixel(ell.Pixels, 50, 50).Alpha); // stroke only
        var line = PaintOps.Shape(doc, ShapeKind.Line, new SKPoint(0, 90), new SKPoint(100, 90), SKColors.Black, true, 4)!;
        Assert.True(Pixel(line.Pixels, 50, 90).Alpha > 200);
        Assert.Equal(4, doc.Root.Children.Count);
    }

    [Fact]
    public void CopyCutPasteToNewLayer()
    {
        var doc = Blank(100, 100);
        var bg = (PixelLayer)doc.ActiveLayer!;
        bg.Pixels.Erase(SKColors.Red);
        doc.Selection = SelectionOps.Rectangle(100, 100, new SKRect(10, 10, 30, 30));
        var clip = LayerOps.Cut(doc)!;
        Assert.Equal(20, clip.Pixels.Width);
        Assert.Equal(0, Pixel(bg.Pixels, 20, 20).Alpha);
        var pasted = LayerOps.Paste(doc, clip);
        AssertColor(SKColors.Red, Pixel(pasted.Pixels, 20, 20));
        Assert.Equal(0, Pixel(pasted.Pixels, 50, 50).Alpha);
        Assert.True(doc.Selection.IsEmpty);
        doc.History.Undo(); // paste
        doc.History.Undo(); // cut
        AssertColor(SKColors.Red, Pixel(bg.Pixels, 20, 20));
    }

    [Fact]
    public void MoveToolMovesSelectedPixels()
    {
        var doc = Blank(100, 100);
        var layer = LayerOps.AddLayer(doc);
        using (var c = new SKCanvas(layer.Pixels))
        using (var p = new SKPaint { Color = SKColors.Blue })
            c.DrawRect(10, 10, 10, 10, p);
        var tool = new MoveTool();
        var ctx = new ToolContext(doc, new ToolSettings());
        tool.Down(ctx, new PointerInput(new SKPoint(15, 15)));
        tool.Move(ctx, new PointerInput(new SKPoint(55, 25)));
        tool.Up(ctx, new PointerInput(new SKPoint(55, 25)));
        Assert.Equal(0, Pixel(layer.Pixels, 15, 15).Alpha);
        AssertColor(SKColors.Blue, Pixel(layer.Pixels, 55, 25));
        Assert.Null(layer.PreviewPixels);
        doc.History.Undo();
        AssertColor(SKColors.Blue, Pixel(layer.Pixels, 15, 15));
    }

    [Fact]
    public void FreeTransformScalesFromHandle()
    {
        var doc = Blank(100, 100);
        var layer = LayerOps.AddLayer(doc);
        using (var c = new SKCanvas(layer.Pixels))
        using (var p = new SKPaint { Color = SKColors.Blue })
            c.DrawRect(10, 10, 20, 20, p); // 10..30
        var ctx = new ToolContext(doc, new ToolSettings());
        var t = new FreeTransformTool();
        Assert.True(t.Begin(ctx));
        Assert.Equal(Handle.SE, t.HitTest(new SKPoint(30, 30), 3));
        t.Down(ctx, new PointerInput(new SKPoint(30, 30), HandleRadius: 3));
        t.Move(ctx, new PointerInput(new SKPoint(50, 50), HandleRadius: 3));
        t.Up(ctx, new PointerInput(new SKPoint(50, 50)));
        t.Commit(ctx);
        var b = Bitmaps.ContentBounds(layer.Pixels);
        Assert.InRange(b.Left, 9, 11);
        Assert.InRange(b.Right, 49, 51);
        Assert.InRange(b.Bottom, 49, 51);
    }

    [Fact]
    public void CropToolCropsDocument()
    {
        var doc = Blank(100, 80);
        var ctx = new ToolContext(doc, new ToolSettings());
        var crop = new CropTool();
        crop.Down(ctx, new PointerInput(new SKPoint(10, 10)));
        crop.Move(ctx, new PointerInput(new SKPoint(60, 40)));
        crop.Up(ctx, new PointerInput(new SKPoint(60, 40)));
        crop.Commit(ctx);
        Assert.Equal(50, doc.Width);
        Assert.Equal(30, doc.Height);
        doc.History.Undo();
        Assert.Equal(100, doc.Width);
        Assert.Equal(100, ((PixelLayer)doc.Root.Children[0]).Pixels.Width);
    }

    [Fact]
    public void BrushIsFastOn4000x4000()
    {
        // Strokes must only touch the dab area: a long stroke across a 16-megapixel,
        // three-layer document should stay well under a frame per pointer event.
        var doc = Document.CreateBlank(4000, 4000, "big", SKColors.White);
        LayerOps.AddLayer(doc);
        LayerOps.AddLayer(doc);
        var brush = new BrushSettings { Size = 60, Hardness = 0.7f, Flow = 0.8f };
        var s = StrokeSession.Begin(doc, brush, StrokeMode.Paint, SKColors.Black, new SKPoint(100, 100), 1)!;
        var sw = Stopwatch.StartNew();
        int events = 200;
        for (int i = 1; i <= events; i++)
            s.MoveTo(new SKPoint(100 + i * 15, 100 + i * 10), 1);
        sw.Stop();
        s.End();
        double perEvent = sw.Elapsed.TotalMilliseconds / events;
        _out.WriteLine($"4000x4000: {perEvent:F2} ms per pointer event");
        Assert.True(perEvent < 8, $"brush too slow: {perEvent:F2} ms per event");
    }
}
