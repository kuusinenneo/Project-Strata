using SkiaSharp;

namespace StrataPhoto.Core.Model;

public enum BlendMode
{
    Normal,
    Darken,
    Multiply,
    ColorBurn,
    Lighten,
    Screen,
    ColorDodge,
    LinearDodge,
    Overlay,
    SoftLight,
    HardLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}

public static class BlendModes
{
    public static IReadOnlyList<BlendMode> All { get; } = Enum.GetValues<BlendMode>();

    public static SKBlendMode ToSkia(this BlendMode mode) => mode switch
    {
        BlendMode.Normal => SKBlendMode.SrcOver,
        BlendMode.Darken => SKBlendMode.Darken,
        BlendMode.Multiply => SKBlendMode.Multiply,
        BlendMode.ColorBurn => SKBlendMode.ColorBurn,
        BlendMode.Lighten => SKBlendMode.Lighten,
        BlendMode.Screen => SKBlendMode.Screen,
        BlendMode.ColorDodge => SKBlendMode.ColorDodge,
        BlendMode.LinearDodge => SKBlendMode.Plus,
        BlendMode.Overlay => SKBlendMode.Overlay,
        BlendMode.SoftLight => SKBlendMode.SoftLight,
        BlendMode.HardLight => SKBlendMode.HardLight,
        BlendMode.Difference => SKBlendMode.Difference,
        BlendMode.Exclusion => SKBlendMode.Exclusion,
        BlendMode.Hue => SKBlendMode.Hue,
        BlendMode.Saturation => SKBlendMode.Saturation,
        BlendMode.Color => SKBlendMode.Color,
        BlendMode.Luminosity => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };

    public static string DisplayName(this BlendMode mode) => mode switch
    {
        BlendMode.ColorBurn => "Color Burn",
        BlendMode.ColorDodge => "Color Dodge",
        BlendMode.LinearDodge => "Linear Dodge (Add)",
        BlendMode.SoftLight => "Soft Light",
        BlendMode.HardLight => "Hard Light",
        _ => mode.ToString(),
    };
}
