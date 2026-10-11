using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Editing;

/// <summary>
/// Pixel edits on the active layer (or its mask), limited to the selection. Every edit records
/// a before/after patch of the affected rectangle only.
/// </summary>
public static class PixelOps
{
    /// <summary>Why the active target can't be painted on, or null if it can.</summary>
    public static string? CannotEditReason(Document doc)
    {
        var layer = doc.ActiveLayer;
        if (layer == null) return "No layer selected.";
        if (layer.Locked) return $"Layer \"{layer.Name}\" is locked.";
        if (doc.EditingMask && layer.Mask != null) return null;
        if (layer is GroupLayer) return "Select a pixel layer (groups have no pixels).";
        if (layer is TextLayer) return "Text layers must be rasterized before painting.";
        if (!layer.Visible) return $"Layer \"{layer.Name}\" is hidden.";
        return null;
    }

    public static bool CanEdit(Document doc) => CannotEditReason(doc) == null;

    /// <summary>Deletes the selected pixels (or the whole layer). On a mask, fills with black.</summary>
    public static bool ClearSelection(Document doc, string name = "Clear")
    {
        if (!CanEdit(doc)) return false;
        var target = doc.EditTarget(out var layer, out bool mask)!;
        if (mask) return Fill(doc, SKColors.Black, 1f, name);
        var rect = doc.Selection.EffectiveBounds(doc.Width, doc.Height);
        var before = Bitmaps.CopyRegion(target, rect);
        using (var canvas = new SKCanvas(target))
        {
            if (doc.Selection.IsEmpty)
            {
                canvas.ClipRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height));
                canvas.Clear(SKColors.Transparent);
            }
            else
            {
                using var paint = new SKPaint { BlendMode = SKBlendMode.DstOut };
                canvas.DrawPixels(doc.Selection.Mask!, 0, 0, paint);
            }
        }
        doc.RecordPixels(name, layer!, mask, rect, before);
        doc.Invalidate(rect);
        return true;
    }

    /// <summary>Fills the selection (or whole layer) with a colour.</summary>
    public static bool Fill(Document doc, SKColor color, float opacity = 1f, string name = "Fill")
    {
        if (!CanEdit(doc)) return false;
        var target = doc.EditTarget(out var layer, out bool mask)!;
        if (mask) color = ToGray(color);
        var rect = doc.Selection.EffectiveBounds(doc.Width, doc.Height);
        var before = Bitmaps.CopyRegion(target, rect);
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { Color = color.WithAlpha((byte)(color.Alpha * opacity)) })
        {
            if (doc.Selection.IsEmpty)
                canvas.DrawRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height), paint);
            else
                canvas.DrawPixels(doc.Selection.Mask!, 0, 0, paint); // Alpha8 drawn in the paint colour
        }
        doc.RecordPixels(name, layer!, mask, rect, before);
        doc.Invalidate(rect);
        return true;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> (reads <c>src</c>, writes <c>dst</c> inside <c>rect</c>) on the
    /// edit target and returns a full-size result blended with the selection. Doesn't modify the
    /// document, so it can be used for live previews (also from a background thread).
    /// </summary>
    public static SKBitmap Compute(SKBitmap target, Selection selection, Action<SKBitmap, SKBitmap, SKRectI> operation)
    {
        var rect = selection.EffectiveBounds(target.Width, target.Height);
        var result = Bitmaps.Copy(target);
        operation(target, result, rect);
        if (!selection.IsEmpty) BlendBySelection(target, result, selection.Mask!, rect);
        return result;
    }

    /// <summary>Writes a result from <see cref="Compute"/> into the edit target as one undoable step.</summary>
    public static bool CommitComputed(Document doc, string name, SKBitmap result)
    {
        if (!CanEdit(doc)) return false;
        var target = doc.EditTarget(out var layer, out bool mask)!;
        if (result.Width != target.Width || result.Height != target.Height) return false;
        var rect = doc.Selection.EffectiveBounds(doc.Width, doc.Height);
        var before = Bitmaps.CopyRegion(target, rect);
        Bitmaps.CopyRect(result, target, rect);
        if (layer is PixelLayer p) p.PreviewPixels = null;
        doc.RecordPixels(name, layer!, mask, rect, before);
        doc.Invalidate(rect);
        return true;
    }

    /// <summary>Compute + commit in one go.</summary>
    public static bool Apply(Document doc, string name, Action<SKBitmap, SKBitmap, SKRectI> operation)
    {
        if (!CanEdit(doc)) return false;
        var target = doc.EditTarget(out _, out _)!;
        using var result = Compute(target, doc.Selection, operation);
        return CommitComputed(doc, name, result);
    }

    /// <summary>dst = lerp(src, dst, selection) inside rect.</summary>
    public static void BlendBySelection(SKBitmap src, SKBitmap dst, SKBitmap selMask, SKRectI rect)
    {
        unsafe
        {
            byte* s = (byte*)src.GetPixels();
            byte* d = (byte*)dst.GetPixels();
            byte* m = (byte*)selMask.GetPixels();
            int srb = src.RowBytes, drb = dst.RowBytes, mrb = selMask.RowBytes;
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                byte* sr = s + (long)y * srb;
                byte* dr = d + (long)y * drb;
                byte* mr = m + (long)y * mrb;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    int a = mr[x];
                    if (a == 255) continue;
                    int i = x * 4;
                    if (a == 0)
                    {
                        *(uint*)(dr + i) = *(uint*)(sr + i);
                        continue;
                    }
                    for (int c = 0; c < 4; c++)
                        dr[i + c] = (byte)((sr[i + c] * (255 - a) + dr[i + c] * a + 127) / 255);
                }
            });
        }
    }

    public static SKColor ToGray(SKColor c)
    {
        byte l = (byte)Math.Round(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue);
        return new SKColor(l, l, l, 255);
    }
}
