using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Painting;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.Core.Tools;

public enum ToolKind
{
    Move, RectMarquee, EllipseMarquee, Lasso, MagicWand, Crop, Eyedropper,
    Brush, Eraser, CloneStamp, Fill, Gradient, Text, Rectangle, Ellipse, Line, Hand, Zoom,
}

/// <summary>Colours and per-tool options, shared by all documents (shown in the options bar).</summary>
public sealed class ToolSettings : Observable
{
    private SKColor _foreground = SKColors.Black;
    private SKColor _background = SKColors.White;
    private SelectionMode _selectionMode = SelectionMode.Replace;
    private float _feather;
    private int _wandTolerance = 32;
    private bool _wandContiguous = true;
    private bool _wandAntiAlias = true;
    private bool _sampleAllLayers;
    private int _fillTolerance = 32;
    private bool _fillContiguous = true;
    private float _fillOpacity = 1;
    private GradientType _gradientType = GradientType.Linear;
    private float _gradientOpacity = 1;
    private bool _gradientReverse;
    private bool _gradientToTransparent;
    private bool _shapeFill = true;
    private float _shapeStrokeWidth = 4;
    private string _fontFamily = "Segoe UI";
    private float _fontSize = 48;
    private TextAlign _textAlign = TextAlign.Left;
    private bool _bold, _italic;
    private bool _cloneAligned = true;

    public SKColor Foreground { get => _foreground; set => Set(ref _foreground, value); }
    public SKColor Background { get => _background; set => Set(ref _background, value); }

    public BrushSettings Brush { get; } = new();
    public BrushSettings Eraser { get; } = new() { Hardness = 1f };
    public BrushSettings Clone { get; } = new() { Hardness = 0.5f };

    public SelectionMode SelectionMode { get => _selectionMode; set => Set(ref _selectionMode, value); }
    public float Feather { get => _feather; set => Set(ref _feather, Math.Clamp(value, 0, 250)); }
    public int WandTolerance { get => _wandTolerance; set => Set(ref _wandTolerance, Math.Clamp(value, 0, 255)); }
    public bool WandContiguous { get => _wandContiguous; set => Set(ref _wandContiguous, value); }
    public bool WandAntiAlias { get => _wandAntiAlias; set => Set(ref _wandAntiAlias, value); }
    public bool SampleAllLayers { get => _sampleAllLayers; set => Set(ref _sampleAllLayers, value); }

    public int FillTolerance { get => _fillTolerance; set => Set(ref _fillTolerance, Math.Clamp(value, 0, 255)); }
    public bool FillContiguous { get => _fillContiguous; set => Set(ref _fillContiguous, value); }
    public float FillOpacity { get => _fillOpacity; set => Set(ref _fillOpacity, Math.Clamp(value, 0.01f, 1f)); }

    public GradientType GradientType { get => _gradientType; set => Set(ref _gradientType, value); }
    public float GradientOpacity { get => _gradientOpacity; set => Set(ref _gradientOpacity, Math.Clamp(value, 0.01f, 1f)); }
    public bool GradientReverse { get => _gradientReverse; set => Set(ref _gradientReverse, value); }
    public bool GradientToTransparent { get => _gradientToTransparent; set => Set(ref _gradientToTransparent, value); }

    public bool ShapeFill { get => _shapeFill; set => Set(ref _shapeFill, value); }
    public float ShapeStrokeWidth { get => _shapeStrokeWidth; set => Set(ref _shapeStrokeWidth, Math.Clamp(value, 1, 500)); }

    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public float FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Clamp(value, 1, 2000)); }
    public TextAlign TextAlign { get => _textAlign; set => Set(ref _textAlign, value); }
    public bool Bold { get => _bold; set => Set(ref _bold, value); }
    public bool Italic { get => _italic; set => Set(ref _italic, value); }

    public bool CloneAligned { get => _cloneAligned; set => Set(ref _cloneAligned, value); }

    public void SwapColors() => (Foreground, Background) = (Background, Foreground);

    public void ResetColors()
    {
        Foreground = SKColors.Black;
        Background = SKColors.White;
    }

    public BrushSettings? BrushFor(ToolKind tool) => tool switch
    {
        ToolKind.Brush => Brush,
        ToolKind.Eraser => Eraser,
        ToolKind.CloneStamp => Clone,
        _ => null,
    };
}
