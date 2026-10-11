using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using StrataPhoto.Core.Imaging;
using DockTool = Dock.Model.Mvvm.Controls.Tool;

namespace StrataPhoto.App.ViewModels.Panels;

/// <summary>Base for the dockable panels on the right.</summary>
public abstract class PanelBase : DockTool
{
    protected PanelBase(MainViewModel main, string id, string title, string icon)
    {
        Main = main;
        Id = id;
        Title = title;
        Icon = icon;
        CanClose = true;
        CanFloat = true;
        CanPin = false;
    }

    public MainViewModel Main { get; }
    public string Icon { get; }

    /// <summary>Called when the active document (or its contents) changes.</summary>
    public virtual void OnDocumentChanged(DocumentViewModel? doc) { }
}

/// <summary>All panels, created once and reused across layout resets.</summary>
public sealed class PanelSet
{
    public PanelSet(MainViewModel main)
    {
        Layers = new LayersPanelViewModel(main);
        Adjustments = new AdjustmentsPanelViewModel(main);
        Properties = new PropertiesPanelViewModel(main);
        History = new HistoryPanelViewModel(main);
        Color = new ColorPanelViewModel(main);
        Swatches = new SwatchesPanelViewModel(main);
        All = new PanelBase[] { Layers, Adjustments, Properties, History, Color, Swatches };
    }

    public LayersPanelViewModel Layers { get; }
    public AdjustmentsPanelViewModel Adjustments { get; }
    public PropertiesPanelViewModel Properties { get; }
    public HistoryPanelViewModel History { get; }
    public ColorPanelViewModel Color { get; }
    public SwatchesPanelViewModel Swatches { get; }
    public IReadOnlyList<PanelBase> All { get; }
}

/// <summary>Small previews of layer pixels and masks.</summary>
public static class Thumbnails
{
    public static WriteableBitmap Render(SKBitmap src, int max, bool checker = true)
    {
        double s = Math.Min((double)max / src.Width, (double)max / src.Height);
        int w = Math.Max(1, (int)Math.Round(src.Width * s)), h = Math.Max(1, (int)Math.Round(src.Height * s));
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
        var c = surface.Canvas;
        if (checker)
        {
            c.Clear(new SKColor(0xCC, 0xCC, 0xCC));
            using var p = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF) };
            for (int y = 0; y < h; y += 4)
            for (int x = (y / 4 % 2) * 4; x < w; x += 8)
                c.DrawRect(x, y, 4, 4, p);
        }
        else c.Clear(SKColors.Transparent);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        c.DrawPixels(src, new SKRect(0, 0, w, h), paint);
        c.Flush();
        return wb;
    }
}
