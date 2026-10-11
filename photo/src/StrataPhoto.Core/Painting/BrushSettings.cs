namespace StrataPhoto.Core.Painting;

/// <summary>Brush tip and dynamics. Size is the diameter in pixels; the others are 0..1.</summary>
public sealed class BrushSettings : Observable
{
    public const float MinSize = 1, MaxSize = 2500;

    private float _size = 30;
    private float _hardness = 0.8f;
    private float _opacity = 1f;
    private float _flow = 1f;
    private float _spacing = 0.12f;
    private bool _pressureSize = true;
    private bool _pressureOpacity;

    public float Size { get => _size; set => Set(ref _size, Math.Clamp(value, MinSize, MaxSize)); }
    public float Hardness { get => _hardness; set => Set(ref _hardness, Math.Clamp(value, 0f, 1f)); }
    public float Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0.01f, 1f)); }
    public float Flow { get => _flow; set => Set(ref _flow, Math.Clamp(value, 0.01f, 1f)); }

    /// <summary>Distance between dabs as a fraction of the diameter.</summary>
    public float Spacing { get => _spacing; set => Set(ref _spacing, Math.Clamp(value, 0.01f, 2f)); }

    /// <summary>Tablet pressure scales the brush size.</summary>
    public bool PressureSize { get => _pressureSize; set => Set(ref _pressureSize, value); }

    /// <summary>Tablet pressure scales the flow.</summary>
    public bool PressureOpacity { get => _pressureOpacity; set => Set(ref _pressureOpacity, value); }

    /// <summary>Photoshop-style [ and ] steps.</summary>
    public void StepSize(int direction)
    {
        float step = Size < 10 ? 1 : Size < 50 ? 5 : Size < 100 ? 10 : Size < 300 ? 25 : 50;
        Size = MathF.Round(Size + direction * step);
    }
}
