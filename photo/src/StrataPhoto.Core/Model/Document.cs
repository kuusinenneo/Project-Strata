using SkiaSharp;
using StrataPhoto.Core.History;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Model;

/// <summary>An open image: layer tree, selection, history and the cached composite.</summary>
public sealed class Document : Observable, IDisposable
{
    private string _name;
    private string? _filePath;
    private Layer? _activeLayer;
    private bool _editingMask;
    private Selection _selection = Selection.None;
    private long _savedVersion;

    public Document(int width, int height, string name, string historyBaseName = "New Document")
    {
        Width = width;
        Height = height;
        _name = name;
        Root = new GroupLayer("Root");
        Compositor = new Compositor(width, height);
        History = new HistoryStack(this, historyBaseName);
        History.Changed += () =>
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
        };
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public SKRectI Bounds => new(0, 0, Width, Height);

    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) OnPropertyChanged(nameof(Title)); }
    }

    /// <summary>Path of the .sphoto project this document was opened from or saved to.</summary>
    public string? FilePath { get => _filePath; set => Set(ref _filePath, value); }

    public bool IsDirty => History.Version != _savedVersion;
    public string Title => IsDirty ? Name + " *" : Name;

    public GroupLayer Root { get; }
    public Compositor Compositor { get; }
    public HistoryStack History { get; }

    public IEnumerable<Layer> AllLayers => Root.Descendants();

    public Layer? ActiveLayer
    {
        get => _activeLayer;
        set
        {
            if (Set(ref _activeLayer, value))
            {
                if (_activeLayer?.Mask == null) EditingMask = false;
                ActiveLayerChanged?.Invoke();
            }
        }
    }

    /// <summary>True when painting should go into the active layer's mask instead of its pixels.</summary>
    public bool EditingMask
    {
        get => _editingMask;
        set => Set(ref _editingMask, value && _activeLayer?.Mask != null);
    }

    public PixelLayer? ActivePixelLayer => _activeLayer as PixelLayer;

    public Selection Selection
    {
        get => _selection;
        set
        {
            if (ReferenceEquals(_selection, value)) return;
            _selection = value ?? Selection.None;
            OnPropertyChanged();
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>The composite changed inside this rectangle.</summary>
    public event Action<SKRectI>? Invalidated;

    /// <summary>Layers were added, removed, reordered or had properties changed.</summary>
    public event Action? LayersChanged;

    public event Action? ActiveLayerChanged;
    public event Action? SelectionChanged;
    public event Action? SizeChanged;

    // ------------------------------------------------------------------ factories

    public static Document CreateBlank(int width, int height, string name, SKColor? background)
    {
        var doc = new Document(width, height, name);
        var layer = new PixelLayer(background.HasValue ? "Background" : "Layer 1", Bitmaps.Create(width, height, background));
        doc.Root.Add(layer);
        doc._activeLayer = layer;
        doc.InvalidateAll();
        return doc;
    }

    public static Document FromBitmap(SKBitmap bitmap, string name)
    {
        var doc = new Document(bitmap.Width, bitmap.Height, name, "Open");
        var layer = new PixelLayer("Background", bitmap);
        doc.Root.Add(layer);
        doc._activeLayer = layer;
        doc.InvalidateAll();
        return doc;
    }

    // ------------------------------------------------------------------ invalidation

    public void Invalidate(SKRectI rect)
    {
        rect = Bitmaps.Intersect(rect, Bounds);
        if (rect.IsEmpty) return;
        Compositor.Update(Root, rect);
        Invalidated?.Invoke(rect);
    }

    public void InvalidateAll() => Invalidate(Bounds);

    public void NotifyLayersChanged()
    {
        if (_activeLayer != null && !Root.Contains(_activeLayer)) _activeLayer = null;
        _activeLayer ??= Root.Descendants().LastOrDefault(l => l is not GroupLayer) ?? Root.Children.LastOrDefault();
        OnPropertyChanged(nameof(ActiveLayer));
        OnPropertyChanged(nameof(EditingMask));
        LayersChanged?.Invoke();
        ActiveLayerChanged?.Invoke();
        InvalidateAll();
    }

    internal void SetSize(int width, int height)
    {
        Width = width;
        Height = height;
        Compositor.Resize(width, height);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        SizeChanged?.Invoke();
    }

    internal void SetActiveLayerSilently(Layer? layer, bool editingMask)
    {
        _activeLayer = layer;
        _editingMask = editingMask && layer?.Mask != null;
    }

    // ------------------------------------------------------------------ history helpers

    /// <summary>
    /// Runs a structural change and records it as one undoable step. <paramref name="mergeKey"/>
    /// merges consecutive changes with the same key (slider drags) into one history entry.
    /// </summary>
    public void Commit(string name, Action change, string? mergeKey = null)
    {
        var before = DocState.Capture(this);
        change();
        var after = DocState.Capture(this);
        History.Push(new StateEntry(name, before, after, mergeKey));
        NotifyLayersChanged();
    }

    /// <summary>
    /// Records an in-place pixel edit of <paramref name="rect"/>. <paramref name="before"/> holds the
    /// old pixels of that rect; the new ones are read from the layer now.
    /// </summary>
    public void RecordPixels(string name, Layer layer, bool mask, SKRectI rect, SKBitmap before)
    {
        var target = mask ? layer.Mask : (layer as PixelLayer)?.Pixels;
        if (target == null || rect.IsEmpty) return;
        var after = Bitmaps.CopyRegion(target, rect);
        History.Push(new PixelPatchEntry(name, layer, mask, rect, before, after));
        (layer as PixelLayer)?.Touch();
    }

    /// <summary>Selection change as an undoable step (selection tools, Select All, Deselect...).</summary>
    public void SetSelection(string name, Selection selection)
    {
        if (ReferenceEquals(selection, _selection)) return;
        var before = DocState.Capture(this);
        Selection = selection;
        History.Push(new StateEntry(name, before, DocState.Capture(this)));
    }

    public void MarkSaved() => _savedVersion = History.Version;

    /// <summary>The bitmap edits should target: the active layer's mask or pixels (null if none / locked group).</summary>
    public SKBitmap? EditTarget(out Layer? layer, out bool mask)
    {
        layer = _activeLayer;
        mask = _editingMask && layer?.Mask != null;
        if (layer == null) return null;
        return mask ? layer.Mask : (layer as PixelLayer)?.Pixels;
    }

    /// <summary>Unique default name like "Layer 3".</summary>
    public string NextLayerName(string prefix = "Layer")
    {
        var names = new HashSet<string>(AllLayers.Select(l => l.Name));
        for (int i = 1; ; i++)
            if (!names.Contains($"{prefix} {i}")) return $"{prefix} {i}";
    }

    public void Dispose() => Compositor.Dispose();
}
