using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Model;

namespace StrataPhoto.App.ViewModels.Panels;

/// <summary>Properties of the active layer: name, opacity, blend mode, mask and (for text) the type settings.</summary>
public sealed partial class PropertiesPanelViewModel : PanelBase
{
    private DocumentViewModel? _doc;
    private Layer? _layer;
    private bool _syncing;

    public PropertiesPanelViewModel(MainViewModel main) : base(main, "Properties", "Properties", "properties") { }

    public IReadOnlyList<BlendMode> BlendModeList => BlendModes.All;
    public IReadOnlyList<string> FontFamilies => Main.FontFamilies;
    public IReadOnlyList<TextAlign> Alignments { get; } = Enum.GetValues<TextAlign>();

    public bool HasDocument => _doc != null;
    public bool HasLayer => _layer != null;
    public bool IsText => _layer is TextLayer;
    public bool HasMask => _layer?.Mask != null;
    public string DocumentInfo => _doc == null ? "" : $"{_doc.Document.Width} × {_doc.Document.Height} px";
    public string LayerKind => _layer switch
    {
        TextLayer => "Type layer",
        GroupLayer g => $"Group ({g.Children.Count} layers)",
        PixelLayer => "Pixel layer",
        _ => "",
    };

    public string LayerName
    {
        get => _layer?.Name ?? "";
        set
        {
            if (_syncing || _doc == null || _layer == null) return;
            LayerOps.Rename(_doc.Document, _layer, value);
        }
    }

    public double Opacity
    {
        get => Math.Round((_layer?.Opacity ?? 1) * 100);
        set
        {
            if (_syncing || _doc == null || _layer == null) return;
            float v = (float)Math.Clamp(value / 100, 0, 1);
            if (Math.Abs(v - _layer.Opacity) > 0.001f) LayerOps.SetOpacity(_doc.Document, _layer, v);
        }
    }

    public BlendMode BlendMode
    {
        get => _layer?.BlendMode ?? BlendMode.Normal;
        set
        {
            if (_syncing || _doc == null || _layer == null || value == _layer.BlendMode) return;
            LayerOps.SetBlendMode(_doc.Document, _layer, value);
        }
    }

    public bool LayerVisible
    {
        get => _layer?.Visible ?? false;
        set
        {
            if (_syncing || _doc == null || _layer == null || value == _layer.Visible) return;
            LayerOps.SetVisible(_doc.Document, _layer, value);
        }
    }

    public bool LayerLocked
    {
        get => _layer?.Locked ?? false;
        set
        {
            if (_syncing || _doc == null || _layer == null || value == _layer.Locked) return;
            LayerOps.SetLocked(_doc.Document, _layer, value);
        }
    }

    public bool MaskEnabled
    {
        get => _layer?.MaskEnabled ?? false;
        set
        {
            if (_syncing || _doc == null || _layer == null || value == _layer.MaskEnabled) return;
            LayerOps.SetMaskEnabled(_doc.Document, _layer, value);
        }
    }

    public bool EditingMask
    {
        get => _doc?.Document.EditingMask ?? false;
        set
        {
            if (_syncing || _doc == null) return;
            _doc.Document.EditingMask = value;
            Main.Panels.Layers.Rebuild();
        }
    }

    // ------------------------------------------------------------------ text

    private TextProps? Text => (_layer as TextLayer)?.Text;

    private void EditText(Func<TextProps, TextProps> change)
    {
        if (_syncing || _doc == null || _layer is not TextLayer t) return;
        LayerOps.EditText(_doc.Document, t, change(t.Text));
    }

    public string TextContent
    {
        get => Text?.Text ?? "";
        set => EditText(p => p with { Text = value });
    }

    public string? FontFamily
    {
        get => Text?.FontFamily;
        set { if (!string.IsNullOrEmpty(value)) EditText(p => p with { FontFamily = value }); }
    }

    public double FontSize
    {
        get => Text?.FontSize ?? 12;
        set => EditText(p => p with { FontSize = (float)Math.Clamp(value, 1, 2000) });
    }

    public TextAlign Align
    {
        get => Text?.Align ?? TextAlign.Left;
        set => EditText(p => p with { Align = value });
    }

    public bool Bold
    {
        get => Text?.Bold ?? false;
        set => EditText(p => p with { Bold = value });
    }

    public bool Italic
    {
        get => Text?.Italic ?? false;
        set => EditText(p => p with { Italic = value });
    }

    public Color TextColor
    {
        get
        {
            var c = Text?.Color ?? SKColors.Black;
            return Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
        }
    }

    [RelayCommand]
    private void UseForegroundForText()
    {
        var fg = Main.Tools.Settings.Foreground;
        EditText(p => p with { Color = fg });
    }

    [RelayCommand] private void Rasterize() => Main.RasterizeCommand.Execute(null);
    [RelayCommand] private void ApplyMask() => Main.ApplyMaskCommand.Execute(null);
    [RelayCommand] private void DeleteMask() => Main.DeleteMaskCommand.Execute(null);
    [RelayCommand] private void AddMask() => Main.AddMaskRevealCommand.Execute(null);
    [RelayCommand] private void ImageSize() => Main.ImageSizeCommand.Execute(null);
    [RelayCommand] private void CanvasSize() => Main.CanvasSizeCommand.Execute(null);

    public override void OnDocumentChanged(DocumentViewModel? doc)
    {
        if (_doc != doc)
        {
            if (_doc != null)
            {
                _doc.Document.LayersChanged -= Sync;
                _doc.Document.ActiveLayerChanged -= Sync;
                _doc.Document.SizeChanged -= Sync;
            }
            _doc = doc;
            if (_doc != null)
            {
                _doc.Document.LayersChanged += Sync;
                _doc.Document.ActiveLayerChanged += Sync;
                _doc.Document.SizeChanged += Sync;
            }
        }
        Sync();
    }

    private void Sync()
    {
        _syncing = true;
        try
        {
            _layer = _doc?.Document.ActiveLayer;
            foreach (var name in new[]
                     {
                         nameof(HasDocument), nameof(HasLayer), nameof(IsText), nameof(HasMask), nameof(DocumentInfo),
                         nameof(LayerKind), nameof(LayerName), nameof(Opacity), nameof(BlendMode), nameof(LayerVisible),
                         nameof(LayerLocked), nameof(MaskEnabled), nameof(EditingMask), nameof(TextContent),
                         nameof(FontFamily), nameof(FontSize), nameof(Align), nameof(Bold), nameof(Italic), nameof(TextColor),
                     })
                OnPropertyChanged(name);
        }
        finally
        {
            _syncing = false;
        }
    }
}
