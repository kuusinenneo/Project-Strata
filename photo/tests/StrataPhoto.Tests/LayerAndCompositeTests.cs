using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using static StrataPhoto.Tests.TestUtil;

namespace StrataPhoto.Tests;

public class LayerAndCompositeTests
{
    private static PixelLayer Solid(Document doc, SKColor c, string name = "solid")
    {
        var layer = LayerOps.AddLayer(doc, name);
        layer.Pixels.Erase(c);
        doc.InvalidateAll();
        return layer;
    }

    [Fact]
    public void BlankDocumentHasWhiteBackground()
    {
        var doc = Blank();
        Assert.Single(doc.Root.Children);
        AssertColor(SKColors.White, Composite(doc, 10, 10));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void AddDeleteDuplicateRenameAndUndo()
    {
        var doc = Blank();
        var l1 = LayerOps.AddLayer(doc);
        Assert.Equal("Layer 1", l1.Name);
        Assert.Equal(2, doc.Root.Children.Count);
        Assert.Same(l1, doc.ActiveLayer);
        Assert.True(doc.IsDirty);

        var dup = LayerOps.Duplicate(doc)!;
        Assert.Equal("Layer 1 copy", dup.Name);
        Assert.Equal(3, doc.Root.Children.Count);

        LayerOps.Rename(doc, dup, "Shadow");
        Assert.Equal("Shadow", dup.Name);

        LayerOps.Delete(doc, dup);
        Assert.Equal(2, doc.Root.Children.Count);

        doc.History.Undo(); // delete
        Assert.Equal(3, doc.Root.Children.Count);
        doc.History.Undo(); // rename
        Assert.Equal("Layer 1 copy", dup.Name);
        doc.History.GoTo(0);
        Assert.Single(doc.Root.Children);
        doc.History.GoTo(doc.History.Entries.Count);
        Assert.Equal(2, doc.Root.Children.Count); // back at the end: "Shadow" deleted again
        Assert.DoesNotContain(doc.AllLayers, l => l.Name == "Shadow");
        doc.History.Undo();
        Assert.Contains(doc.AllLayers, l => l.Name == "Shadow");
    }

    [Fact]
    public void CannotDeleteLastLayer()
    {
        var doc = Blank();
        LayerOps.Delete(doc);
        Assert.Single(doc.Root.Children);
    }

    [Fact]
    public void ReorderMovesLayerUpAndDown()
    {
        var doc = Blank();
        var a = LayerOps.AddLayer(doc, "A");
        var b = LayerOps.AddLayer(doc, "B");
        Assert.Equal(new[] { "Background", "A", "B" }, doc.Root.Children.Select(l => l.Name));
        LayerOps.Move(doc, -1, b);
        Assert.Equal(new[] { "Background", "B", "A" }, doc.Root.Children.Select(l => l.Name));
        LayerOps.Move(doc, +1, b);
        Assert.Equal(new[] { "Background", "A", "B" }, doc.Root.Children.Select(l => l.Name));
        doc.History.Undo();
        Assert.Equal(new[] { "Background", "B", "A" }, doc.Root.Children.Select(l => l.Name));
    }

    [Fact]
    public void HiddenLayerAndOpacityAffectComposite()
    {
        var doc = Blank();
        var red = Solid(doc, SKColors.Red);
        AssertColor(SKColors.Red, Composite(doc, 5, 5));
        LayerOps.SetOpacity(doc, red, 0.5f);
        AssertColor(new SKColor(255, 128, 128), Composite(doc, 5, 5), 2);
        LayerOps.SetVisible(doc, red, false);
        AssertColor(SKColors.White, Composite(doc, 5, 5));
        doc.History.Undo();
        AssertColor(new SKColor(255, 128, 128), Composite(doc, 5, 5), 2);
    }

    [Fact]
    public void OpacitySliderDragsMergeIntoOneHistoryStep()
    {
        var doc = Blank();
        var red = Solid(doc, SKColors.Red);
        int before = doc.History.Entries.Count;
        for (float o = 0.9f; o >= 0.5f; o -= 0.1f) LayerOps.SetOpacity(doc, red, o);
        Assert.Equal(before + 1, doc.History.Entries.Count);
        doc.History.Undo();
        Assert.Equal(1f, red.Opacity);
    }

    [Theory]
    [InlineData(BlendMode.Multiply, 128, 64, 32)]   // (128,128,128) x (255,128,64)/255
    [InlineData(BlendMode.Screen, 255, 192, 160)]
    [InlineData(BlendMode.Darken, 128, 128, 64)]
    [InlineData(BlendMode.Lighten, 255, 128, 128)]
    [InlineData(BlendMode.Difference, 127, 0, 64)]
    public void BlendModesMatchFormulas(BlendMode mode, int r, int g, int b)
    {
        var doc = Blank(8, 8, new SKColor(128, 128, 128));
        var top = Solid(doc, new SKColor(255, 128, 64));
        LayerOps.SetBlendMode(doc, top, mode);
        AssertColor(new SKColor((byte)r, (byte)g, (byte)b), Composite(doc, 2, 2), 3);
    }

    [Fact]
    public void EveryBlendModeComposites()
    {
        var doc = Blank(8, 8, new SKColor(100, 150, 200));
        var top = Solid(doc, new SKColor(200, 50, 120));
        foreach (var mode in BlendModes.All)
        {
            LayerOps.SetBlendMode(doc, top, mode);
            Assert.Equal(255, Composite(doc, 1, 1).Alpha);
        }
    }

    [Fact]
    public void OverlayMatchesFormula()
    {
        var doc = Blank(8, 8, new SKColor(64, 192, 128));
        var top = Solid(doc, new SKColor(200, 200, 200));
        LayerOps.SetBlendMode(doc, top, BlendMode.Overlay);
        // overlay: b<0.5 ? 2ab : 1-2(1-a)(1-b)
        static int Ov(int a, int bb) => bb < 128 ? 2 * a * bb / 255 : 255 - 2 * (255 - a) * (255 - bb) / 255;
        AssertColor(new SKColor((byte)Ov(200, 64), (byte)Ov(200, 192), (byte)Ov(200, 128)), Composite(doc, 1, 1), 3);
    }

    [Fact]
    public void LayerMaskHidesPixels()
    {
        var doc = Blank(16, 16);
        var red = Solid(doc, SKColors.Red);
        doc.Selection = Core.Selections.SelectionOps.Rectangle(16, 16, new SKRect(0, 0, 8, 16));
        LayerOps.AddMask(doc); // reveal selection only
        Assert.True(doc.EditingMask);
        AssertColor(SKColors.Red, Composite(doc, 2, 2));
        AssertColor(SKColors.White, Composite(doc, 12, 2));
        LayerOps.SetMaskEnabled(doc, red, false);
        AssertColor(SKColors.Red, Composite(doc, 12, 2));
        LayerOps.SetMaskEnabled(doc, red, true);
        LayerOps.ApplyMask(doc);
        Assert.Null(red.Mask);
        Assert.Equal(0, Pixel(red.Pixels, 12, 2).Alpha);
        doc.History.Undo();
        Assert.NotNull(red.Mask);
    }

    [Fact]
    public void GroupsCompositeChildrenWithGroupOpacity()
    {
        var doc = Blank(8, 8);
        var red = Solid(doc, SKColors.Red);
        var group = LayerOps.GroupActive(doc)!;
        Assert.Same(group, red.Parent);
        AssertColor(SKColors.Red, Composite(doc, 1, 1));
        LayerOps.SetOpacity(doc, group, 0.5f);
        AssertColor(new SKColor(255, 128, 128), Composite(doc, 1, 1), 2);
        LayerOps.SetVisible(doc, group, false);
        AssertColor(SKColors.White, Composite(doc, 1, 1));
        doc.History.GoTo(doc.History.Position - 2);
        doc.ActiveLayer = group;
        LayerOps.Ungroup(doc);
        Assert.Same(doc.Root, red.Parent);
        Assert.DoesNotContain(group, doc.Root.Children);
    }

    [Fact]
    public void MoveIntoAndOutOfGroup()
    {
        var doc = Blank(8, 8);
        var a = LayerOps.AddLayer(doc, "A");
        var g = LayerOps.GroupActive(doc)!; // [Background, Group[A]]
        doc.ActiveLayer = doc.Root.Children[0];
        var b = LayerOps.AddLayer(doc, "B"); // [Background, B, Group[A]]
        LayerOps.Move(doc, +1, b);            // enters group at the bottom
        Assert.Same(g, b.Parent);
        LayerOps.Move(doc, -1, b);            // leaves the group below it
        Assert.Same(doc.Root, b.Parent);
    }

    [Fact]
    public void MergeDownAndFlatten()
    {
        var doc = Blank(8, 8);
        var red = Solid(doc, SKColors.Red.WithAlpha(128));
        LayerOps.MergeDown(doc);
        Assert.Single(doc.Root.Children);
        AssertColor(new SKColor(255, 127, 127), Pixel(((PixelLayer)doc.Root.Children[0]).Pixels, 1, 1), 2);
        Solid(doc, SKColors.Blue);
        LayerOps.Flatten(doc);
        Assert.Single(doc.Root.Children);
        AssertColor(SKColors.Blue, Composite(doc, 1, 1));
    }

    [Fact]
    public void TextLayerIsEditableAfterCreation()
    {
        var doc = Blank(200, 100);
        var props = new TextProps("Hi", "Arial", 40, SKColors.Black, TextAlign.Left, true, false, 10, 10);
        var text = LayerOps.AddText(doc, props);
        Assert.Equal("Hi", text.Name);
        var bounds = Bitmaps.ContentBounds(text.Pixels);
        Assert.False(bounds.IsEmpty);
        LayerOps.EditText(doc, text, props with { Text = "Hello world", Color = SKColors.Red, Align = TextAlign.Center, X = 100 });
        Assert.Equal("Hello world", text.Name);
        var b2 = Bitmaps.ContentBounds(text.Pixels);
        Assert.True(b2.Width > bounds.Width);
        Assert.InRange((b2.Left + b2.Right) / 2, 95, 105); // centred on X
        doc.History.Undo();
        Assert.Equal("Hi", text.Text.Text);
        Assert.Equal(bounds, Bitmaps.ContentBounds(text.Pixels));
    }
}
