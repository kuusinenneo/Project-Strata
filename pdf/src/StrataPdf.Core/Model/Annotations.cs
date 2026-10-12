using System.Text.Json.Serialization;

namespace StrataPdf.Core.Model;

/// <summary>A point in page space: PDF points, origin top-left, y down, on the page as it is
/// displayed before any rotation added in Strata PDF.</summary>
public readonly record struct PagePoint(double X, double Y);

/// <summary>A rectangle in page space (see <see cref="PagePoint"/>).</summary>
public readonly record struct PageRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static PageRect FromPoints(PagePoint a, PagePoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool Contains(PagePoint p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public PageRect Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);

    public PageRect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);
}

/// <summary>Colour as #RRGGBB plus opacity 0..1.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, double Opacity = 1)
{
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba Red = new(220, 40, 40);
    public static readonly Rgba Yellow = new(255, 214, 0, 0.4);

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgba Parse(string hex, double opacity = 1)
    {
        hex = hex.TrimStart('#');
        return new Rgba(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16), opacity);
    }
}

public enum ShapeKind { Rectangle, Ellipse, Line, Arrow }

/// <summary>Something drawn on a page. Annotations stay editable in Strata PDF and are written
/// into the page content when the PDF is saved (sticky notes become real PDF notes).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HighlightAnnotation), "highlight")]
[JsonDerivedType(typeof(InkAnnotation), "ink")]
[JsonDerivedType(typeof(TextBoxAnnotation), "text")]
[JsonDerivedType(typeof(ShapeAnnotation), "shape")]
[JsonDerivedType(typeof(NoteAnnotation), "note")]
[JsonDerivedType(typeof(ImageAnnotation), "image")]
[JsonDerivedType(typeof(RedactionAnnotation), "redaction")]
public abstract record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Area used for selecting and moving the annotation.</summary>
    public abstract PageRect Bounds { get; }

    /// <summary>Copy moved by (dx, dy).</summary>
    public abstract Annotation Moved(double dx, double dy);

    public virtual string Describe() => GetType().Name.Replace("Annotation", "");
}

public sealed record HighlightAnnotation(PageRect Rect, Rgba Color) : Annotation
{
    public override PageRect Bounds => Rect;
    public override Annotation Moved(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
}

public sealed record InkAnnotation(IReadOnlyList<PagePoint> Points, Rgba Color, double Width) : Annotation
{
    public override PageRect Bounds
    {
        get
        {
            if (Points.Count == 0) return default;
            double x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
            return new PageRect(x0, y0, x1 - x0, y1 - y0).Inflate(Width / 2);
        }
    }

    public override Annotation Moved(double dx, double dy) =>
        this with { Points = Points.Select(p => new PagePoint(p.X + dx, p.Y + dy)).ToList() };

    public override string Describe() => "Pen";
}

public sealed record TextBoxAnnotation(PageRect Rect, string Text, double FontSize, Rgba Color,
    string FontFamily = "Arial", bool Border = false) : Annotation
{
    public override PageRect Bounds => Rect;
    public override Annotation Moved(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
    public override string Describe() => $"Text \"{(Text.Length > 20 ? Text[..20] + "…" : Text)}\"";
}

public sealed record ShapeAnnotation(ShapeKind Kind, PagePoint From, PagePoint To, Rgba Color, double Width,
    bool Filled = false) : Annotation
{
    public override PageRect Bounds => PageRect.FromPoints(From, To).Inflate(Width / 2 + 2);

    public override Annotation Moved(double dx, double dy) =>
        this with { From = new(From.X + dx, From.Y + dy), To = new(To.X + dx, To.Y + dy) };

    public override string Describe() => Kind.ToString();
}

/// <summary>A sticky note: an icon on the page whose text pops up in PDF viewers.</summary>
public sealed record NoteAnnotation(PagePoint At, string Text, string Author = "Strata PDF") : Annotation
{
    public const double IconSize = 22;
    public override PageRect Bounds => new(At.X, At.Y, IconSize, IconSize);
    public override Annotation Moved(double dx, double dy) => this with { At = new(At.X + dx, At.Y + dy) };
    public override string Describe() => "Note";
}

/// <summary>An image placed on the page (drawn or imported signatures, stamps). PNG bytes.</summary>
public sealed record ImageAnnotation(PageRect Rect, byte[] Png, string Label = "Signature") : Annotation
{
    public override PageRect Bounds => Rect;
    public override Annotation Moved(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
    public override string Describe() => Label;
}

/// <summary>
/// Area to redact. On save the page is replaced by an image with the area blacked out, so the
/// text, images and vector content underneath are really removed.
/// </summary>
public sealed record RedactionAnnotation(PageRect Rect) : Annotation
{
    public override PageRect Bounds => Rect;
    public override Annotation Moved(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
    public override string Describe() => "Redaction";
}
