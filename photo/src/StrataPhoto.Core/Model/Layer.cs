using SkiaSharp;
using StrataPhoto.Core.Imaging;

namespace StrataPhoto.Core.Model;

/// <summary>
/// Base of the layer tree. Pixel data is always document-sized, so layer coordinates are
/// document coordinates. Bitmaps are shared with history snapshots: operations that change the
/// size or layout of the pixels assign a new bitmap instead of modifying the old one, and
/// in-place pixel edits record before/after patches.
/// </summary>
public abstract class Layer : Observable
{
    private static int _nextId;

    private string _name;
    private bool _visible = true;
    private bool _locked;
    private float _opacity = 1f;
    private BlendMode _blendMode = BlendMode.Normal;
    private SKBitmap? _mask;
    private bool _maskEnabled = true;

    protected Layer(string name)
    {
        _name = name;
        Id = Interlocked.Increment(ref _nextId);
    }

    public int Id { get; internal set; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Visible { get => _visible; set => Set(ref _visible, value); }
    public bool Locked { get => _locked; set => Set(ref _locked, value); }
    public float Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0f, 1f)); }
    public BlendMode BlendMode { get => _blendMode; set => Set(ref _blendMode, value); }

    /// <summary>Layer mask: document-sized opaque grey bitmap, white = visible, black = hidden.</summary>
    public SKBitmap? Mask
    {
        get => _mask;
        set { if (Set(ref _mask, value)) OnPropertyChanged(nameof(HasMask)); }
    }

    public bool MaskEnabled { get => _maskEnabled; set => Set(ref _maskEnabled, value); }
    public bool HasMask => _mask != null;

    public GroupLayer? Parent { get; internal set; }

    /// <summary>Nesting depth (0 = top level), for indentation in the Layers panel.</summary>
    public int Depth => Parent?.Parent == null ? 0 : Parent.Depth + 1;

    public abstract Layer CloneLayer(string? name = null);

    protected void CopyCommonTo(Layer other)
    {
        other._visible = _visible;
        other._locked = _locked;
        other._opacity = _opacity;
        other._blendMode = _blendMode;
        other._mask = _mask == null ? null : Bitmaps.Copy(_mask);
        other._maskEnabled = _maskEnabled;
    }

    public override string ToString() => $"{GetType().Name} '{Name}' #{Id}";
}

/// <summary>A raster layer.</summary>
public class PixelLayer : Layer
{
    private SKBitmap _pixels;

    public PixelLayer(string name, SKBitmap pixels) : base(name) => _pixels = pixels;

    public SKBitmap Pixels
    {
        get => _pixels;
        set { if (Set(ref _pixels, value)) Revision++; }
    }

    /// <summary>
    /// When set, the compositor shows this bitmap instead of <see cref="Pixels"/> (live previews of
    /// adjustments, filters and transforms). Not saved and not part of history.
    /// </summary>
    public SKBitmap? PreviewPixels { get; set; }

    /// <summary>Bumped whenever the pixels change (used for thumbnails).</summary>
    public int Revision { get; private set; }

    public void Touch()
    {
        Revision++;
        OnPropertyChanged(nameof(Revision));
    }

    public override Layer CloneLayer(string? name = null)
    {
        var copy = new PixelLayer(name ?? Name, Bitmaps.Copy(Pixels));
        CopyCommonTo(copy);
        return copy;
    }
}

public enum TextAlign { Left, Center, Right }

/// <summary>Everything needed to re-render a text layer. Immutable so history can share it.</summary>
public sealed record TextProps(
    string Text,
    string FontFamily,
    float FontSize,
    SKColor Color,
    TextAlign Align,
    bool Bold,
    bool Italic,
    float X,
    float Y,
    float LineSpacing = 1.2f)
{
    public static TextProps Default(float x, float y, SKColor color) =>
        new("Text", "Segoe UI", 48, color, TextAlign.Left, false, false, x, y);
}

/// <summary>An editable text layer; its pixels are re-rendered from <see cref="Text"/>.</summary>
public sealed class TextLayer : PixelLayer
{
    private TextProps _text;

    public TextLayer(string name, TextProps text, SKBitmap pixels) : base(name, pixels) => _text = text;

    public TextProps Text { get => _text; set => Set(ref _text, value); }

    public override Layer CloneLayer(string? name = null)
    {
        var copy = new TextLayer(name ?? Name, Text, Bitmaps.Copy(Pixels));
        CopyCommonTo(copy);
        return copy;
    }
}

/// <summary>A folder of layers. Children are ordered bottom (index 0) to top.</summary>
public sealed class GroupLayer : Layer
{
    private bool _expanded = true;

    public GroupLayer(string name) : base(name) { }

    public List<Layer> Children { get; } = new();

    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }

    public void Insert(int index, Layer layer)
    {
        layer.Parent?.Children.Remove(layer);
        layer.Parent = this;
        Children.Insert(Math.Clamp(index, 0, Children.Count), layer);
    }

    public void Add(Layer layer) => Insert(Children.Count, layer);

    public bool Remove(Layer layer)
    {
        if (!Children.Remove(layer)) return false;
        layer.Parent = null;
        return true;
    }

    /// <summary>All descendants, depth first, bottom to top.</summary>
    public IEnumerable<Layer> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            if (child is GroupLayer g)
                foreach (var d in g.Descendants())
                    yield return d;
        }
    }

    public bool Contains(Layer layer)
    {
        for (var p = layer.Parent; p != null; p = p.Parent)
            if (p == this) return true;
        return false;
    }

    public override Layer CloneLayer(string? name = null)
    {
        var copy = new GroupLayer(name ?? Name) { _expanded = _expanded };
        CopyCommonTo(copy);
        foreach (var child in Children)
            copy.Add(child.CloneLayer());
        return copy;
    }
}
