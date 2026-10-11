using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.App.ViewModels.Panels;

public sealed partial class LayersPanelViewModel : PanelBase
{
    private readonly Dictionary<int, LayerItemViewModel> _items = new();
    private readonly DispatcherTimer _thumbTimer;
    private DocumentViewModel? _doc;
    private bool _syncing;

    public LayersPanelViewModel(MainViewModel main) : base(main, "Layers", "Layers", "layers")
    {
        _thumbTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => RefreshThumbnails());
    }

    public ObservableCollection<LayerItemViewModel> Items { get; } = new();

    public IReadOnlyList<BlendMode> BlendModeList => BlendModes.All;

    [ObservableProperty] private LayerItemViewModel? _selectedItem;

    public bool HasDocument => _doc != null;
    public Layer? Active => _doc?.Document.ActiveLayer;

    public BlendMode ActiveBlendMode
    {
        get => Active?.BlendMode ?? BlendMode.Normal;
        set
        {
            if (_syncing || _doc == null || Active == null || Active.BlendMode == value) return;
            LayerOps.SetBlendMode(_doc.Document, Active, value);
        }
    }

    /// <summary>Opacity in percent for the panel's slider.</summary>
    public double ActiveOpacity
    {
        get => Math.Round((Active?.Opacity ?? 1) * 100);
        set
        {
            if (_syncing || _doc == null || Active == null) return;
            float v = (float)Math.Clamp(value / 100, 0, 1);
            if (Math.Abs(Active.Opacity - v) < 0.001f) return;
            LayerOps.SetOpacity(_doc.Document, Active, v);
        }
    }

    public bool ActiveLocked
    {
        get => Active?.Locked ?? false;
        set
        {
            if (_syncing || _doc == null || Active == null || Active.Locked == value) return;
            LayerOps.SetLocked(_doc.Document, Active, value);
        }
    }

    partial void OnSelectedItemChanged(LayerItemViewModel? value)
    {
        if (_syncing || _doc == null || value == null) return;
        if (_doc.Document.ActiveLayer != value.Layer)
        {
            Main.Tools.CancelActive();
            _doc.Document.ActiveLayer = value.Layer;
        }
    }

    public override void OnDocumentChanged(DocumentViewModel? doc)
    {
        if (_doc != doc)
        {
            if (_doc != null)
            {
                _doc.Document.LayersChanged -= Rebuild;
                _doc.Document.ActiveLayerChanged -= SyncActive;
                _doc.Document.Invalidated -= OnInvalidated;
            }
            _doc = doc;
            _items.Clear();
            if (_doc != null)
            {
                _doc.Document.LayersChanged += Rebuild;
                _doc.Document.ActiveLayerChanged += SyncActive;
                _doc.Document.Invalidated += OnInvalidated;
            }
        }
        OnPropertyChanged(nameof(HasDocument));
        Rebuild();
    }

    private void OnInvalidated(SkiaSharp.SKRectI r)
    {
        if (!_thumbTimer.IsEnabled) _thumbTimer.Start();
    }

    /// <summary>Rebuilds the flattened top-to-bottom list (children indented under their group).</summary>
    public void Rebuild()
    {
        _syncing = true;
        try
        {
            var list = new List<LayerItemViewModel>();
            if (_doc != null)
            {
                void Walk(GroupLayer g, int depth)
                {
                    for (int i = g.Children.Count - 1; i >= 0; i--)
                    {
                        var layer = g.Children[i];
                        if (!_items.TryGetValue(layer.Id, out var item))
                        {
                            item = new LayerItemViewModel(this, layer);
                            _items[layer.Id] = item;
                        }
                        item.Depth = depth;
                        item.Refresh();
                        list.Add(item);
                        if (layer is GroupLayer sub && sub.Expanded) Walk(sub, depth + 1);
                    }
                }
                Walk(_doc.Document.Root, 0);
            }
            // Update the collection in place to keep scroll position and selection stable.
            for (int i = Items.Count - 1; i >= 0; i--)
                if (!list.Contains(Items[i])) Items.RemoveAt(i);
            for (int i = 0; i < list.Count; i++)
            {
                int cur = Items.IndexOf(list[i]);
                if (cur == i) continue;
                if (cur >= 0) Items.Move(cur, i);
                else Items.Insert(i, list[i]);
            }
            var alive = new HashSet<int>(_doc?.Document.AllLayers.Select(l => l.Id) ?? Enumerable.Empty<int>());
            foreach (var dead in _items.Keys.Where(k => !alive.Contains(k)).ToList()) _items.Remove(dead);
        }
        finally
        {
            _syncing = false;
        }
        SyncActive();
        RefreshThumbnails(force: false);
    }

    private void SyncActive()
    {
        _syncing = true;
        try
        {
            var active = _doc?.Document.ActiveLayer;
            SelectedItem = active != null && _items.TryGetValue(active.Id, out var item) ? item : null;
            foreach (var i in Items) i.RefreshMaskTarget();
            OnPropertyChanged(nameof(Active));
            OnPropertyChanged(nameof(ActiveBlendMode));
            OnPropertyChanged(nameof(ActiveOpacity));
            OnPropertyChanged(nameof(ActiveLocked));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshThumbnails() => RefreshThumbnails(force: false);

    private void RefreshThumbnails(bool force)
    {
        _thumbTimer.Stop();
        foreach (var item in Items) item.UpdateThumbnail(force);
    }

    public Document? Doc => _doc?.Document;

    // ------------------------------------------------------------------ commands

    [RelayCommand] private void AddLayer() => Main.NewLayerCommand.Execute(null);
    [RelayCommand] private void AddGroup() => Main.NewGroupCommand.Execute(null);
    [RelayCommand] private void Duplicate() => Main.DuplicateLayerCommand.Execute(null);
    [RelayCommand] private void Delete() => Main.DeleteLayerCommand.Execute(null);
    [RelayCommand] private void AddMask() => Main.AddMaskRevealCommand.Execute(null);
    [RelayCommand] private void MoveUp() => Main.BringForwardCommand.Execute(null);
    [RelayCommand] private void MoveDown() => Main.SendBackwardCommand.Execute(null);

    public void ToggleVisible(LayerItemViewModel item)
    {
        if (Doc != null) LayerOps.SetVisible(Doc, item.Layer, !item.Layer.Visible);
    }

    public void ToggleExpanded(LayerItemViewModel item)
    {
        if (item.Layer is GroupLayer g)
        {
            g.Expanded = !g.Expanded;
            Rebuild();
        }
    }

    public void Rename(LayerItemViewModel item, string name)
    {
        if (Doc != null) LayerOps.Rename(Doc, item.Layer, name);
    }

    public void SelectMask(LayerItemViewModel item, bool mask)
    {
        if (Doc == null) return;
        Doc.ActiveLayer = item.Layer;
        Doc.EditingMask = mask && item.Layer.Mask != null;
        SyncActive();
        Main.StatusMessage = Doc.EditingMask ? "Painting on the layer mask (black hides, white reveals)." : "";
    }

    /// <summary>Ctrl+click a thumbnail: select the layer's opaque pixels.</summary>
    public void SelectPixels(LayerItemViewModel item)
    {
        if (Doc != null && item.Layer is PixelLayer p)
            Doc.SetSelection("Load Selection", SelectionOps.FromAlpha(p.Pixels));
    }

    /// <summary>Drag and drop: put <paramref name="dragged"/> above (or into) <paramref name="target"/>.</summary>
    public void Drop(LayerItemViewModel dragged, LayerItemViewModel target, bool below, bool into)
    {
        if (Doc == null || dragged == target) return;
        if (into && target.Layer is GroupLayer g)
        {
            LayerOps.MoveTo(Doc, dragged.Layer, g, g.Children.Count);
            return;
        }
        var parent = target.Layer.Parent;
        if (parent == null) return;
        int index = parent.Children.IndexOf(target.Layer);
        // The list is top-to-bottom; children are bottom-to-top.
        LayerOps.MoveTo(Doc, dragged.Layer, parent, below ? index : index + 1);
    }
}

public sealed partial class LayerItemViewModel : ObservableObject
{
    private readonly LayersPanelViewModel _panel;
    private int _thumbRevision = -1;
    private object? _maskRef;

    public LayerItemViewModel(LayersPanelViewModel panel, Layer layer)
    {
        _panel = panel;
        Layer = layer;
    }

    public Layer Layer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Indent))]
    private int _depth;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _visible;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private bool _hasMask;
    [ObservableProperty] private bool _maskEnabled;
    [ObservableProperty] private bool _isMaskTarget;
    [ObservableProperty] private bool _isEditingName;
    [ObservableProperty] private string _blendInfo = "";
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private Bitmap? _maskThumbnail;

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public bool IsGroup => Layer is GroupLayer;
    public bool IsText => Layer is TextLayer;
    public bool IsPixel => Layer is PixelLayer && Layer is not TextLayer;
    public bool Expanded => Layer is GroupLayer { Expanded: true };
    public string KindIcon => Layer switch { GroupLayer => "folder", TextLayer => "text", _ => "image" };

    public void Refresh()
    {
        Name = Layer.Name;
        Visible = Layer.Visible;
        Locked = Layer.Locked;
        HasMask = Layer.Mask != null;
        MaskEnabled = Layer.MaskEnabled;
        BlendInfo = Layer.BlendMode == BlendMode.Normal && Layer.Opacity >= 0.999f
            ? ""
            : $"{Layer.BlendMode.DisplayName()} {Layer.Opacity * 100:0}%";
        OnPropertyChanged(nameof(Expanded));
        RefreshMaskTarget();
    }

    public void RefreshMaskTarget() =>
        IsMaskTarget = _panel.Doc?.ActiveLayer == Layer && _panel.Doc.EditingMask;

    public void UpdateThumbnail(bool force)
    {
        if (Layer is PixelLayer p && (force || p.Revision != _thumbRevision || Thumbnail == null))
        {
            _thumbRevision = p.Revision;
            Thumbnail = Thumbnails.Render(p.PreviewPixels ?? p.Pixels, 36);
        }
        if (Layer.Mask != null && (force || !ReferenceEquals(_maskRef, Layer.Mask) || MaskThumbnail == null || IsMaskTarget))
        {
            _maskRef = Layer.Mask;
            MaskThumbnail = Thumbnails.Render(Layer.Mask, 36, checker: false);
        }
        else if (Layer.Mask == null)
        {
            MaskThumbnail = null;
            _maskRef = null;
        }
    }

    [RelayCommand] private void ToggleVisible() => _panel.ToggleVisible(this);
    [RelayCommand] private void ToggleExpanded() => _panel.ToggleExpanded(this);
    [RelayCommand] private void BeginRename() => IsEditingName = true;

    public void CommitRename(string text)
    {
        IsEditingName = false;
        _panel.Rename(this, text);
    }
}
