using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StrataPhoto.App.Theme;
using StrataPhoto.App.ViewModels;

namespace StrataPhoto.App.Canvas;

/// <summary>Pixel ruler along the top or left edge of the canvas, with a cursor marker.</summary>
public sealed class RulerView : Control
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<RulerView, Orientation>(nameof(Orientation));

    private DocumentViewModel? _vm;
    private static readonly double[] Steps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000, 10000 };

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.ViewChanged -= InvalidateVisual;
            _vm.CursorChanged -= InvalidateVisual;
        }
        _vm = DataContext as DocumentViewModel;
        if (_vm != null)
        {
            _vm.ViewChanged += InvalidateVisual;
            _vm.CursorChanged += InvalidateVisual;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(new SolidColorBrush(SharedTheme.Get("bgPanel")), new Rect(size));
        bool horizontal = Orientation == Orientation.Horizontal;
        var border = new Pen(new SolidColorBrush(SharedTheme.Get("border")), 1);
        if (horizontal) ctx.DrawLine(border, new Point(0, size.Height - 0.5), new Point(size.Width, size.Height - 0.5));
        else ctx.DrawLine(border, new Point(size.Width - 0.5, 0), new Point(size.Width - 0.5, size.Height));
        if (_vm?.Canvas is not { } canvas) return;

        double scale = _vm.Zoom / canvas.CurrentRenderScale; // DIPs per document pixel
        double offset = horizontal ? _vm.Pan.X : _vm.Pan.Y;
        double length = horizontal ? size.Width : size.Height;
        double thickness = horizontal ? size.Height : size.Width;

        // Major tick spacing: at least ~60 DIPs apart.
        double major = Steps.FirstOrDefault(st => st * scale >= 60, Steps[^1]);
        double minor = major / (major >= 10 && (major / 10) * scale >= 6 ? 10 : major >= 5 && (major / 5) * scale >= 6 ? 5 : 2);
        var tickPen = new Pen(new SolidColorBrush(SharedTheme.Get("textDim")), 1);
        var textBrush = new SolidColorBrush(SharedTheme.Get("textDim"));
        var typeface = new Typeface(FontFamily.Default);

        double startDoc = Math.Floor(-offset / scale / minor) * minor;
        double endDoc = (length - offset) / scale;
        for (double v = startDoc; v <= endDoc; v += minor)
        {
            double pos = Math.Round(v * scale + offset) + 0.5;
            bool isMajor = Math.Abs(v / major - Math.Round(v / major)) < 1e-6;
            double tick = isMajor ? thickness : thickness * 0.3;
            if (horizontal) ctx.DrawLine(tickPen, new Point(pos, thickness - tick), new Point(pos, thickness));
            else ctx.DrawLine(tickPen, new Point(thickness - tick, pos), new Point(thickness, pos));
            if (!isMajor) continue;
            var text = new FormattedText(((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 9, textBrush);
            if (horizontal)
            {
                ctx.DrawText(text, new Point(pos + 3, 1));
            }
            else
            {
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(1, pos - 3)))
                    ctx.DrawText(text, new Point(0, 0));
            }
        }

        if (_vm.CursorDoc is { } c)
        {
            double cp = (horizontal ? c.X : c.Y) * scale + offset;
            var marker = new Pen(new SolidColorBrush(SharedTheme.Get("accent")), 1);
            if (horizontal) ctx.DrawLine(marker, new Point(cp, 0), new Point(cp, thickness));
            else ctx.DrawLine(marker, new Point(0, cp), new Point(thickness, cp));
        }
    }
}
