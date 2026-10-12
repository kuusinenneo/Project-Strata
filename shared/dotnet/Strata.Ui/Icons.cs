using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Strata.Ui;

/// <summary>One &lt;path&gt; of a shared icon.</summary>
public sealed record IconPath(Geometry Geometry, bool Filled, double StrokeWidth, double Opacity);

/// <summary>
/// Loads the shared SVG icons (24×24, path-only, drawn with currentColor) as Avalonia geometry.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, IReadOnlyList<IconPath>> Cache = new();

    /// <summary>Raised when an icon file can't be read (apps log it).</summary>
    public static event Action<Exception>? LoadFailed;

    public static IReadOnlyList<IconPath> Get(string name)
    {
        if (Cache.TryGetValue(name, out var icon)) return icon;
        icon = Load(name);
        Cache[name] = icon;
        return icon;
    }

    private static IReadOnlyList<IconPath> Load(string name)
    {
        try
        {
            var file = Path.Combine(SharedTheme.SharedFolder, "icons", name + ".svg");
            var svg = XDocument.Load(file).Root!;
            double defaultWidth = Num(svg.Attribute("stroke-width")?.Value, 1.6);
            var list = new List<IconPath>();
            foreach (var path in svg.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                var d = path.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(d)) continue;
                bool filled = path.Attribute("fill")?.Value == "currentColor";
                list.Add(new IconPath(StreamGeometry.Parse(d), filled,
                    Num(path.Attribute("stroke-width")?.Value, defaultWidth),
                    Num(path.Attribute("opacity")?.Value, 1)));
            }
            return list;
        }
        catch (Exception ex)
        {
            LoadFailed?.Invoke(new InvalidDataException($"Icon '{name}' could not be loaded", ex));
            return Array.Empty<IconPath>();
        }
    }

    private static double Num(string? s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>Draws an icon scaled into <paramref name="bounds"/>.</summary>
    public static void Draw(DrawingContext ctx, string name, Rect bounds, IBrush brush)
    {
        double scale = Math.Min(bounds.Width, bounds.Height) / 24.0;
        var offset = new Point(bounds.X + (bounds.Width - 24 * scale) / 2, bounds.Y + (bounds.Height - 24 * scale) / 2);
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            foreach (var p in Get(name))
            {
                using (ctx.PushOpacity(p.Opacity))
                {
                    if (p.Filled)
                        ctx.DrawGeometry(brush, null, p.Geometry);
                    else
                        ctx.DrawGeometry(null, new Pen(brush, p.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), p.Geometry);
                }
            }
        }
    }
}

/// <summary>Shows a shared icon: <c>&lt;theme:IconView Icon="brush"/&gt;</c>. Colour follows Foreground.</summary>
public sealed class IconView : Control
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<IconView, string?>(nameof(Icon));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<IconView>();

    static IconView()
    {
        AffectsRender<IconView>(IconProperty, ForegroundProperty);
    }

    public IconView()
    {
        Width = 18;
        Height = 18;
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Icon)) return;
        Icons.Draw(context, Icon, new Rect(Bounds.Size), Foreground ?? Brushes.White);
    }
}
