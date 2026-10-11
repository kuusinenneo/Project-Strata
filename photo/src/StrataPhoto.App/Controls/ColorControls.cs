using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace StrataPhoto.App.Controls;

/// <summary>Saturation (x) / brightness (y) square for a given hue.</summary>
public sealed class SatValPicker : Control
{
    public static readonly StyledProperty<Color> HueColorProperty =
        AvaloniaProperty.Register<SatValPicker, Color>(nameof(HueColor), Colors.Red);

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<SatValPicker, double>(nameof(Saturation), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<SatValPicker, double>(nameof(Brightness), defaultBindingMode: BindingMode.TwoWay);

    static SatValPicker() => AffectsRender<SatValPicker>(HueColorProperty, SaturationProperty, BrightnessProperty);

    public Color HueColor { get => GetValue(HueColorProperty); set => SetValue(HueColorProperty, value); }
    public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Brightness { get => GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(HueColor, 1) },
        }, r);
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) },
        }, r);
        var p = new Point(Saturation * r.Width, (1 - Brightness) * r.Height);
        ctx.DrawEllipse(null, new Pen(Brushes.Black, 2.5), p, 5, 5);
        ctx.DrawEllipse(null, new Pen(Brushes.White, 1.5), p, 5, 5);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Pick(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this) Pick(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Pick(Point p)
    {
        Saturation = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1);
        Brightness = Math.Clamp(1 - p.Y / Math.Max(1, Bounds.Height), 0, 1);
    }
}

/// <summary>Horizontal rainbow bar for picking the hue (0..360).</summary>
public sealed class HueSlider : Control
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<HueSlider, double>(nameof(Hue), defaultBindingMode: BindingMode.TwoWay);

    static HueSlider() => AffectsRender<HueSlider>(HueProperty);

    public double Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
        for (int i = 0; i <= 6; i++)
            brush.GradientStops.Add(new GradientStop(HsvColor.ToRgb(i * 60 % 360, 1, 1), i / 6.0));
        ctx.FillRectangle(brush, r.Deflate(new Thickness(0, 3)));
        double x = Hue / 360 * r.Width;
        ctx.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), new Rect(x - 3, 0, 6, r.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Pick(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this) Pick(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Pick(Point p) => Hue = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 0.9999) * 360;
}

/// <summary>Foreground/background colour squares (Photoshop-style, overlapping).</summary>
public sealed class ColorPairView : Control
{
    public static readonly StyledProperty<Color> ForegroundColorProperty =
        AvaloniaProperty.Register<ColorPairView, Color>(nameof(ForegroundColor), Colors.Black);

    public static readonly StyledProperty<Color> BackgroundColorProperty =
        AvaloniaProperty.Register<ColorPairView, Color>(nameof(BackgroundColor), Colors.White);

    public static readonly StyledProperty<bool> BackgroundActiveProperty =
        AvaloniaProperty.Register<ColorPairView, bool>(nameof(BackgroundActive));

    static ColorPairView() => AffectsRender<ColorPairView>(ForegroundColorProperty, BackgroundColorProperty, BackgroundActiveProperty);

    public Color ForegroundColor { get => GetValue(ForegroundColorProperty); set => SetValue(ForegroundColorProperty, value); }
    public Color BackgroundColor { get => GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }
    public bool BackgroundActive { get => GetValue(BackgroundActiveProperty); set => SetValue(BackgroundActiveProperty, value); }

    /// <summary>Raised with true when the background square was clicked, false for the foreground.</summary>
    public event Action<bool>? SquareClicked;

    private Rect FgRect => new(0, 0, Bounds.Width * 0.62, Bounds.Height * 0.62);
    private Rect BgRect => new(Bounds.Width * 0.38, Bounds.Height * 0.38, Bounds.Width * 0.62, Bounds.Height * 0.62);

    public override void Render(DrawingContext ctx)
    {
        var outline = new Pen(Brushes.Black, 1);
        var hi = new Pen(new SolidColorBrush(Color.Parse("#409cff")), 2);
        ctx.DrawRectangle(new SolidColorBrush(BackgroundColor), BackgroundActive ? hi : outline, BgRect);
        ctx.DrawRectangle(Brushes.White, null, FgRect.Inflate(1));
        ctx.DrawRectangle(new SolidColorBrush(ForegroundColor), BackgroundActive ? outline : hi, FgRect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        SquareClicked?.Invoke(!FgRect.Contains(p) && BgRect.Contains(p));
    }
}
