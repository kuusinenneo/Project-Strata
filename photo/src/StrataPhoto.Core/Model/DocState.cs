using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Model;

/// <summary>
/// Snapshot of a document's layer tree, layer properties and selection. Bitmaps are shared by
/// reference (not copied), so snapshots are cheap; see <see cref="Layer"/> for the rules that make
/// this safe.
/// </summary>
public sealed class DocState
{
    private sealed record LayerState(
        Layer Layer, string Name, bool Visible, bool Locked, float Opacity, BlendMode Blend,
        SKBitmap? Mask, bool MaskEnabled, SKBitmap? Pixels, TextProps? Text, bool Expanded,
        List<LayerState>? Children);

    private readonly LayerState _root;
    private readonly int _width, _height;
    private readonly Layer? _active;
    private readonly bool _editingMask;
    private readonly Selection _selection;

    private DocState(Document doc)
    {
        _width = doc.Width;
        _height = doc.Height;
        _root = CaptureLayer(doc.Root);
        _active = doc.ActiveLayer;
        _editingMask = doc.EditingMask;
        _selection = doc.Selection;
    }

    public static DocState Capture(Document doc) => new(doc);

    public void Restore(Document doc)
    {
        bool resized = doc.Width != _width || doc.Height != _height;
        if (resized) doc.SetSize(_width, _height);
        RestoreLayer(_root);
        doc.SetActiveLayerSilently(_active != null && (doc.Root.Contains(_active)) ? _active : null, _editingMask);
        doc.Selection = _selection;
        doc.NotifyLayersChanged();
    }

    /// <summary>Approximate memory held only by this snapshot (bitmaps the other state doesn't share).</summary>
    public long NewPixelBytes(DocState other)
    {
        var mine = new HashSet<SKBitmap>(Bitmaps(_root));
        mine.ExceptWith(Bitmaps(other._root));
        return mine.Sum(Imaging.Bitmaps.ByteSize);
    }

    private static IEnumerable<SKBitmap> Bitmaps(LayerState s)
    {
        if (s.Pixels != null) yield return s.Pixels;
        if (s.Mask != null) yield return s.Mask;
        if (s.Children != null)
            foreach (var c in s.Children)
            foreach (var b in Bitmaps(c))
                yield return b;
    }

    private static LayerState CaptureLayer(Layer layer) => new(
        layer, layer.Name, layer.Visible, layer.Locked, layer.Opacity, layer.BlendMode,
        layer.Mask, layer.MaskEnabled,
        (layer as PixelLayer)?.Pixels,
        (layer as TextLayer)?.Text,
        (layer as GroupLayer)?.Expanded ?? false,
        layer is GroupLayer g ? g.Children.Select(CaptureLayer).ToList() : null);

    private static void RestoreLayer(LayerState s)
    {
        var layer = s.Layer;
        layer.Name = s.Name;
        layer.Visible = s.Visible;
        layer.Locked = s.Locked;
        layer.Opacity = s.Opacity;
        layer.BlendMode = s.Blend;
        layer.Mask = s.Mask;
        layer.MaskEnabled = s.MaskEnabled;
        if (layer is PixelLayer p && s.Pixels != null)
        {
            p.Pixels = s.Pixels;
            p.PreviewPixels = null;
            p.Touch();
        }
        if (layer is TextLayer t && s.Text != null) t.Text = s.Text;
        if (layer is GroupLayer g && s.Children != null)
        {
            g.Expanded = s.Expanded;
            foreach (var child in g.Children) child.Parent = null;
            g.Children.Clear();
            foreach (var cs in s.Children)
            {
                RestoreLayer(cs);
                cs.Layer.Parent?.Children.Remove(cs.Layer);
                cs.Layer.Parent = g;
                g.Children.Add(cs.Layer);
            }
        }
    }
}
