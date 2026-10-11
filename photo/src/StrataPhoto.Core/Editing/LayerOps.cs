using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Editing;

/// <summary>Undoable layer-panel operations.</summary>
public static class LayerOps
{
    /// <summary>Parent group and index where a new layer goes: just above the active layer.</summary>
    private static (GroupLayer parent, int index) InsertionPoint(Document doc)
    {
        var active = doc.ActiveLayer;
        if (active is GroupLayer g && g.Expanded) return (g, g.Children.Count);
        if (active?.Parent != null) return (active.Parent, active.Parent.Children.IndexOf(active) + 1);
        return (doc.Root, doc.Root.Children.Count);
    }

    public static PixelLayer AddLayer(Document doc, string? name = null)
    {
        var layer = new PixelLayer(name ?? doc.NextLayerName(), Bitmaps.Create(doc.Width, doc.Height));
        Insert(doc, layer, "New Layer");
        return layer;
    }

    public static void Insert(Document doc, Layer layer, string historyName)
    {
        doc.Commit(historyName, () =>
        {
            var (parent, index) = InsertionPoint(doc);
            parent.Insert(index, layer);
            doc.ActiveLayer = layer;
        });
    }

    public static PixelLayer AddLayerFromBitmap(Document doc, SKBitmap pixels, string name, string historyName)
    {
        var layer = new PixelLayer(name, pixels);
        Insert(doc, layer, historyName);
        return layer;
    }

    public static GroupLayer AddGroup(Document doc)
    {
        var group = new GroupLayer(doc.NextLayerName("Group"));
        Insert(doc, group, "New Group");
        return group;
    }

    /// <summary>Puts the active layer into a new group in its place (Ctrl+G).</summary>
    public static GroupLayer? GroupActive(Document doc)
    {
        var layer = doc.ActiveLayer;
        if (layer?.Parent == null) return null;
        var group = new GroupLayer(doc.NextLayerName("Group"));
        doc.Commit("Group Layers", () =>
        {
            var parent = layer.Parent!;
            int index = parent.Children.IndexOf(layer);
            parent.Remove(layer);
            parent.Insert(index, group);
            group.Add(layer);
            doc.ActiveLayer = group;
        });
        return group;
    }

    /// <summary>Moves a group's children up into its parent and removes the group.</summary>
    public static void Ungroup(Document doc)
    {
        if (doc.ActiveLayer is not GroupLayer group || group.Parent == null) return;
        doc.Commit("Ungroup Layers", () =>
        {
            var parent = group.Parent!;
            int index = parent.Children.IndexOf(group);
            parent.Remove(group);
            var children = group.Children.ToList();
            foreach (var child in children)
                parent.Insert(index++, child);
            doc.ActiveLayer = children.LastOrDefault();
        });
    }

    public static void Delete(Document doc, Layer? layer = null)
    {
        layer ??= doc.ActiveLayer;
        if (layer?.Parent == null) return;
        if (doc.Root.Descendants().Count(l => l is not GroupLayer && l != layer && !(layer is GroupLayer g && g.Contains(l))) == 0)
            return; // keep at least one pixel layer
        doc.Commit("Delete Layer", () =>
        {
            var parent = layer.Parent!;
            int index = parent.Children.IndexOf(layer);
            parent.Remove(layer);
            doc.ActiveLayer = parent.Children.Count > 0
                ? parent.Children[Math.Clamp(index - 1, 0, parent.Children.Count - 1)]
                : parent == doc.Root ? null : parent;
        });
    }

    public static Layer? Duplicate(Document doc)
    {
        var layer = doc.ActiveLayer;
        if (layer?.Parent == null) return null;
        var copy = layer.CloneLayer(layer.Name + " copy");
        doc.Commit("Duplicate Layer", () =>
        {
            var parent = layer.Parent!;
            parent.Insert(parent.Children.IndexOf(layer) + 1, copy);
            doc.ActiveLayer = copy;
        });
        return copy;
    }

    /// <summary>Moves a layer one step up (+1) or down (-1) in the stack, entering and leaving groups.</summary>
    public static void Move(Document doc, int direction, Layer? layer = null)
    {
        layer ??= doc.ActiveLayer;
        if (layer?.Parent == null) return;
        var parent = layer.Parent;
        int index = parent.Children.IndexOf(layer);
        int target = index + direction;
        doc.Commit(direction > 0 ? "Move Layer Up" : "Move Layer Down", () =>
        {
            if (target >= 0 && target < parent.Children.Count)
            {
                var neighbour = parent.Children[target];
                parent.Remove(layer);
                if (neighbour is GroupLayer g && g.Expanded)
                    g.Insert(direction > 0 ? 0 : g.Children.Count, layer); // enter the group
                else
                    parent.Insert(target, layer);
            }
            else if (parent.Parent != null)
            {
                // leave the group
                var grand = parent.Parent;
                int groupIndex = grand.Children.IndexOf(parent);
                parent.Remove(layer);
                grand.Insert(direction > 0 ? groupIndex + 1 : groupIndex, layer);
            }
            doc.ActiveLayer = layer;
        });
    }

    /// <summary>Drag and drop in the Layers panel: put <paramref name="layer"/> into <paramref name="parent"/> at <paramref name="index"/>.</summary>
    public static void MoveTo(Document doc, Layer layer, GroupLayer parent, int index)
    {
        if (layer == parent || (layer is GroupLayer g && g.Contains(parent) ) || layer.Parent == null) return;
        doc.Commit("Move Layer", () =>
        {
            if (layer.Parent == parent && parent.Children.IndexOf(layer) < index) index--;
            layer.Parent!.Remove(layer);
            parent.Insert(index, layer);
            doc.ActiveLayer = layer;
        });
    }

    public static void Rename(Document doc, Layer layer, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == layer.Name) return;
        doc.Commit("Rename Layer", () => layer.Name = name.Trim());
    }

    public static void SetVisible(Document doc, Layer layer, bool visible) =>
        doc.Commit(visible ? "Show Layer" : "Hide Layer", () => layer.Visible = visible);

    public static void SetLocked(Document doc, Layer layer, bool locked) =>
        doc.Commit(locked ? "Lock Layer" : "Unlock Layer", () => layer.Locked = locked);

    public static void SetOpacity(Document doc, Layer layer, float opacity) =>
        doc.Commit("Layer Opacity", () => layer.Opacity = opacity, $"opacity:{layer.Id}");

    public static void SetBlendMode(Document doc, Layer layer, BlendMode mode) =>
        doc.Commit("Blend Mode", () => layer.BlendMode = mode);

    // ------------------------------------------------------------------ masks

    /// <summary>Adds a layer mask. With a selection the mask reveals only the selection.</summary>
    public static void AddMask(Document doc, bool hideAll = false)
    {
        var layer = doc.ActiveLayer;
        if (layer == null || layer.Mask != null) return;
        var mask = Bitmaps.Create(doc.Width, doc.Height, hideAll ? SKColors.Black : SKColors.White);
        if (!doc.Selection.IsEmpty && !hideAll)
        {
            mask.Erase(SKColors.Black);
            using var canvas = new SKCanvas(mask);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawPixels(doc.Selection.Mask!, 0, 0, paint);
        }
        doc.Commit("Add Layer Mask", () =>
        {
            layer.Mask = mask;
            layer.MaskEnabled = true;
            doc.EditingMask = true;
        });
    }

    public static void DeleteMask(Document doc)
    {
        var layer = doc.ActiveLayer;
        if (layer?.Mask == null) return;
        doc.Commit("Delete Layer Mask", () =>
        {
            layer.Mask = null;
            doc.EditingMask = false;
        });
    }

    /// <summary>Bakes the mask into the layer's alpha and removes it.</summary>
    public static void ApplyMask(Document doc)
    {
        if (doc.ActiveLayer is not PixelLayer layer || layer.Mask == null) return;
        var pixels = Bitmaps.Copy(layer.Pixels);
        using (var canvas = new SKCanvas(pixels))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = SKColorFilter.CreateLumaColor() })
            canvas.DrawPixels(layer.Mask, 0, 0, paint);
        doc.Commit("Apply Layer Mask", () =>
        {
            layer.Pixels = pixels;
            layer.Mask = null;
            doc.EditingMask = false;
        });
    }

    public static void SetMaskEnabled(Document doc, Layer layer, bool enabled) =>
        doc.Commit(enabled ? "Enable Layer Mask" : "Disable Layer Mask", () => layer.MaskEnabled = enabled);

    // ------------------------------------------------------------------ merge

    /// <summary>Merges the active layer into the layer below it (Ctrl+E).</summary>
    public static void MergeDown(Document doc)
    {
        var top = doc.ActiveLayer;
        if (top?.Parent == null) return;
        var parent = top.Parent;
        int index = parent.Children.IndexOf(top);
        if (index <= 0 || parent.Children[index - 1] is not PixelLayer below || below is TextLayer) return;
        var merged = Compositor.RenderLayers(new[] { below, top }, doc.Width, doc.Height);
        var result = new PixelLayer(below.Name, merged);
        doc.Commit("Merge Down", () =>
        {
            parent.Remove(top);
            int i = parent.Children.IndexOf(below);
            parent.Remove(below);
            parent.Insert(i, result);
            doc.ActiveLayer = result;
        });
    }

    /// <summary>Flattens all layers into one.</summary>
    public static void Flatten(Document doc)
    {
        var flat = Compositor.Flatten(doc.Root, doc.Width, doc.Height);
        var result = new PixelLayer("Background", flat);
        doc.Commit("Flatten Image", () =>
        {
            foreach (var c in doc.Root.Children.ToList()) doc.Root.Remove(c);
            doc.Root.Add(result);
            doc.ActiveLayer = result;
        });
    }

    /// <summary>Turns a text layer into a plain pixel layer.</summary>
    public static PixelLayer? Rasterize(Document doc, Layer? layer = null)
    {
        if ((layer ?? doc.ActiveLayer) is not TextLayer text || text.Parent == null) return null;
        var pixel = new PixelLayer(text.Name, text.Pixels);
        doc.Commit("Rasterize Type", () =>
        {
            var parent = text.Parent!;
            int i = parent.Children.IndexOf(text);
            parent.Remove(text);
            pixel.Opacity = text.Opacity;
            pixel.BlendMode = text.BlendMode;
            pixel.Visible = text.Visible;
            pixel.Mask = text.Mask;
            parent.Insert(i, pixel);
            doc.ActiveLayer = pixel;
        });
        return pixel;
    }

    // ------------------------------------------------------------------ text layers

    public static TextLayer AddText(Document doc, TextProps props)
    {
        var layer = new TextLayer(TextLayerName(props), props, TextRenderer.Render(props, doc.Width, doc.Height));
        Insert(doc, layer, "Type Layer");
        return layer;
    }

    /// <summary>Changes a text layer's text or style (re-renders into a new bitmap).</summary>
    public static void EditText(Document doc, TextLayer layer, TextProps props, string? mergeKey = null)
    {
        if (props == layer.Text) return;
        var pixels = TextRenderer.Render(props, doc.Width, doc.Height);
        bool autoName = layer.Name == TextLayerName(layer.Text);
        doc.Commit("Edit Type", () =>
        {
            layer.Text = props;
            layer.Pixels = pixels;
            if (autoName) layer.Name = TextLayerName(props);
        }, mergeKey ?? $"text:{layer.Id}");
    }

    public static string TextLayerName(TextProps p)
    {
        var first = p.Text.Split('\n')[0].Trim();
        if (first.Length == 0) return "Text";
        return first.Length > 30 ? first[..30] : first;
    }

    // ------------------------------------------------------------------ clipboard

    /// <summary>Copies the selected pixels of the active layer (or the whole layer).</summary>
    public static ClipboardImage? Copy(Document doc, bool merged = false)
    {
        SKBitmap? source = merged ? Compositor.Flatten(doc.Root, doc.Width, doc.Height) : doc.ActivePixelLayer?.Pixels;
        if (source == null) return null;
        var sel = doc.Selection;
        var rect = sel.EffectiveBounds(doc.Width, doc.Height);
        var clip = Bitmaps.CopyRegion(source, rect);
        if (!sel.IsEmpty)
        {
            using var canvas = new SKCanvas(clip);
            canvas.MaskIn(sel.Mask!, new SKRect(0, 0, rect.Width, rect.Height), -rect.Left, -rect.Top);
        }
        if (merged) source.Dispose();
        return new ClipboardImage(clip, new SKPointI(rect.Left, rect.Top));
    }

    public static ClipboardImage? Cut(Document doc)
    {
        if (doc.ActivePixelLayer is not { Locked: false } || doc.ActiveLayer is TextLayer) return Copy(doc);
        var clip = Copy(doc);
        if (clip != null) PixelOps.ClearSelection(doc, "Cut");
        return clip;
    }

    /// <summary>Pastes as a new layer, at the original position when it fits, else centred.</summary>
    public static PixelLayer Paste(Document doc, ClipboardImage clip)
    {
        var pixels = Bitmaps.Create(doc.Width, doc.Height);
        int x = clip.Origin.X, y = clip.Origin.Y;
        if (x < 0 || y < 0 || x + clip.Pixels.Width > doc.Width || y + clip.Pixels.Height > doc.Height)
        {
            x = (doc.Width - clip.Pixels.Width) / 2;
            y = (doc.Height - clip.Pixels.Height) / 2;
        }
        using (var canvas = new SKCanvas(pixels))
            canvas.DrawPixels(clip.Pixels, x, y);
        var layer = new PixelLayer(doc.NextLayerName(), pixels);
        doc.Commit("Paste", () =>
        {
            var (parent, index) = InsertionPoint(doc);
            parent.Insert(index, layer);
            doc.ActiveLayer = layer;
            doc.Selection = Selection.None;
        });
        return layer;
    }
}

/// <summary>Pixels on the clipboard plus where they came from.</summary>
public sealed record ClipboardImage(SKBitmap Pixels, SKPointI Origin);
