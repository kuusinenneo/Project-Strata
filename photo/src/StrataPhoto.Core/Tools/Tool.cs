using SkiaSharp;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.Tools;

/// <summary>A pointer sample in document coordinates.</summary>
/// <param name="Pressure">0..1 (1 for mice).</param>
/// <param name="HandleRadius">Hit-test radius for handles, in document pixels (depends on zoom).</param>
public readonly record struct PointerInput(SKPoint Pos, float Pressure = 1f, bool Shift = false, bool Alt = false,
    bool Ctrl = false, float HandleRadius = 6f, bool DoubleClick = false);

public sealed class ToolContext
{
    public ToolContext(Document doc, ToolSettings settings)
    {
        Doc = doc;
        Settings = settings;
    }

    public Document Doc { get; }
    public ToolSettings Settings { get; }

    /// <summary>Message for the status bar (e.g. why a tool can't be used here).</summary>
    public string? Message { get; set; }
}

/// <summary>What the canvas should draw on top of the image while a tool is in use (document coordinates).</summary>
public abstract record ToolOverlay;
public sealed record RectOverlay(SKRect Rect, bool Ellipse = false) : ToolOverlay;
public sealed record PolylineOverlay(IReadOnlyList<SKPoint> Points, bool Closed) : ToolOverlay;
public sealed record LineOverlay(SKPoint From, SKPoint To) : ToolOverlay;
public sealed record CropOverlay(SKRect Rect) : ToolOverlay;
public sealed record TransformOverlay(SKPoint[] Corners) : ToolOverlay;
public sealed record ShapeOverlay(Editing.ShapeKind Kind, SKPoint From, SKPoint To, SKColor Color, bool Fill, float StrokeWidth) : ToolOverlay;
public sealed record MarkerOverlay(SKPoint Pos) : ToolOverlay;

public abstract class Tool
{
    public abstract ToolKind Kind { get; }

    /// <summary>True between pointer down and up (or while a crop/transform is pending).</summary>
    public virtual bool IsBusy => false;

    public virtual void Down(ToolContext ctx, PointerInput p) { }
    public virtual void Move(ToolContext ctx, PointerInput p) { }
    public virtual void Up(ToolContext ctx, PointerInput p) { }

    /// <summary>Enter: finish a pending operation (crop, transform).</summary>
    public virtual void Commit(ToolContext ctx) { }

    /// <summary>Esc or tool switch: abandon the current operation.</summary>
    public virtual void Cancel(ToolContext ctx) { }

    public virtual ToolOverlay? Overlay => null;

    protected static SKPoint Constrain45(SKPoint from, SKPoint to)
    {
        var d = to - from;
        double ang = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4);
        float len = d.Length;
        return new SKPoint(from.X + (float)Math.Cos(ang) * len, from.Y + (float)Math.Sin(ang) * len);
    }

    /// <summary>Drag rectangle with Shift = square and Alt = from centre.</summary>
    protected static SKRect DragRect(SKPoint start, SKPoint end, bool square, bool fromCenter)
    {
        float dx = end.X - start.X, dy = end.Y - start.Y;
        if (square)
        {
            float s = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = Math.Sign(dx == 0 ? 1 : dx) * s;
            dy = Math.Sign(dy == 0 ? 1 : dy) * s;
        }
        return fromCenter
            ? new SKRect(start.X - Math.Abs(dx), start.Y - Math.Abs(dy), start.X + Math.Abs(dx), start.Y + Math.Abs(dy))
            : new SKRect(Math.Min(start.X, start.X + dx), Math.Min(start.Y, start.Y + dy),
                Math.Max(start.X, start.X + dx), Math.Max(start.Y, start.Y + dy));
    }
}
