using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SkiaSharp;
using StrataPhoto.Core.Model;

namespace StrataPhoto.App.Views;

/// <summary>0..1 (Core) ↔ 0..100 (UI).</summary>
public sealed class PercentConverter : IValueConverter
{
    public static readonly PercentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is float f ? Math.Round(f * 100.0, 1) : value is double d ? Math.Round(d * 100, 1) : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value switch { double d => d, decimal m => (double)m, float f => f, _ => 0 };
        return targetType == typeof(float) ? (object)(float)(v / 100) : v / 100;
    }
}

/// <summary>float ↔ double for sliders bound to Core settings.</summary>
public sealed class FloatConverter : IValueConverter
{
    public static readonly FloatConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is float f ? (double)f : value is int i ? (double)i : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value switch { double d => d, decimal m => (double)m, _ => 0 };
        if (targetType == typeof(int)) return (int)Math.Round(v);
        return (float)v;
    }
}

/// <summary>Checks an enum value against the converter parameter (for toggle-button groups).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter != null ? Enum.Parse(targetType, parameter.ToString()!, true) : BindingOperations.DoNothing;
}

public sealed class BlendModeNameConverter : IValueConverter
{
    public static readonly BlendModeNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BlendMode m ? m.DisplayName() : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

public sealed class SkColorBrushConverter : IValueConverter
{
    public static readonly SkColorBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SKColor c ? new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

public sealed class ColorBrushConverter : IValueConverter
{
    public static readonly ColorBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color c ? new SolidColorBrush(c) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>true → 0.45 opacity (dimmed undone history steps, hidden layers).</summary>
public sealed class DimConverter : IValueConverter
{
    public static readonly DimConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 0.45 : 1.0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
