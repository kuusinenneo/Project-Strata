using Avalonia;
using SkiaSharp;
using StrataPhoto.App.Canvas;
using StrataPhoto.Core.Model;
using DockDocument = Dock.Model.Mvvm.Controls.Document;

namespace StrataPhoto.App.ViewModels;

/// <summary>One open image: the Core document plus its view state (zoom, pan) and GPU tiles.</summary>
public sealed class DocumentViewModel : DockDocument
{
    public static readonly double[] ZoomSteps =
    {
        0.01, 0.015, 0.02, 0.03, 0.04, 0.05, 0.0625, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667,
        1, 1.5, 2, 3, 4, 5, 6, 8, 12, 16, 24, 32,
    };

    private double _zoom = 1;
    private Point _pan;
    private SKPoint? _cursor;
    private bool _closeConfirmed;

    public DocumentViewModel(MainViewModel main, Document document)
    {
        Main = main;
        Document = document;
        Tiles = new TileCache(document);
        Id = "doc-" + Guid.NewGuid().ToString("N");
        CanFloat = false;
        UpdateTitle();
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Document.Title) or nameof(Document.IsDirty) or nameof(Document.Name)) UpdateTitle();
        };
        document.SizeChanged += () =>
        {
            ClampPan();
            ViewChanged?.Invoke();
        };
    }

    public MainViewModel Main { get; }
    public Document Document { get; }
    public TileCache Tiles { get; }

    /// <summary>The canvas currently showing this document (set by the view).</summary>
    public CanvasView? Canvas { get; set; }

    /// <summary>True until the first fit-to-window after the canvas gets a size.</summary>
    public bool NeedsFit { get; set; } = true;

    /// <summary>Screen (device) pixels per image pixel: 1 = 100%.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            value = Math.Clamp(value, ZoomSteps[0], ZoomSteps[^1]);
            if (Math.Abs(value - _zoom) < 1e-9) return;
            _zoom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZoomText));
            ViewChanged?.Invoke();
            Main.OnViewChanged(this);
        }
    }

    public string ZoomText => $"{Zoom * 100:0.##}%";

    /// <summary>Position of the document's top-left corner in the canvas (view units).</summary>
    public Point Pan
    {
        get => _pan;
        set
        {
            _pan = value;
            ClampPan();
            ViewChanged?.Invoke();
        }
    }

    public SKPoint? CursorDoc => _cursor;

    public event Action? ViewChanged;
    public event Action? CursorChanged;

    public void SetCursor(SKPoint? docPos)
    {
        _cursor = docPos;
        CursorChanged?.Invoke();
        Main.OnCursorMoved(this);
    }

    private void UpdateTitle()
    {
        Title = Document.Name; // Dock adds its own "*" for IsModified
        IsModified = Document.IsDirty;
    }

    /// <summary>Keeps at least part of the image on screen.</summary>
    private void ClampPan()
    {
        if (Canvas == null) return;
        var size = Canvas.ViewSize;
        double s = _zoom / Canvas.CurrentRenderScale;
        double w = Document.Width * s, h = Document.Height * s;
        double margin = 40;
        _pan = new Point(Math.Clamp(_pan.X, margin - w, size.Width - margin), Math.Clamp(_pan.Y, margin - h, size.Height - margin));
    }

    public void ZoomAt(double newZoom, Point viewPoint, double renderScale)
    {
        newZoom = Math.Clamp(newZoom, ZoomSteps[0], ZoomSteps[^1]);
        double oldS = _zoom / renderScale, newS = newZoom / renderScale;
        var docX = (viewPoint.X - _pan.X) / oldS;
        var docY = (viewPoint.Y - _pan.Y) / oldS;
        _pan = new Point(viewPoint.X - docX * newS, viewPoint.Y - docY * newS);
        Zoom = newZoom;
        ClampPan();
        ViewChanged?.Invoke();
    }

    /// <summary>Next/previous preset zoom level, keeping <paramref name="viewPoint"/> fixed.</summary>
    public void ZoomStep(int direction, Point? viewPoint, double renderScale)
    {
        double target = direction > 0
            ? ZoomSteps.FirstOrDefault(z => z > _zoom * 1.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(z => z < _zoom * 0.999, ZoomSteps[0]);
        var center = viewPoint ?? (Canvas != null ? new Point(Canvas.ViewSize.Width / 2, Canvas.ViewSize.Height / 2) : default);
        ZoomAt(target, center, renderScale);
    }

    public void FitToView(Size viewSize, double renderScale)
    {
        NeedsFit = false;
        if (viewSize.Width < 10 || viewSize.Height < 10) return;
        double margin = 24;
        double s = Math.Min((viewSize.Width - margin * 2) / Document.Width, (viewSize.Height - margin * 2) / Document.Height);
        double zoom = Math.Clamp(Math.Min(s * renderScale, 1.0), ZoomSteps[0], ZoomSteps[^1]);
        _zoom = zoom;
        double ds = zoom / renderScale;
        _pan = new Point((viewSize.Width - Document.Width * ds) / 2, (viewSize.Height - Document.Height * ds) / 2);
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomText));
        ViewChanged?.Invoke();
        Main.OnViewChanged(this);
    }

    public void Fit()
    {
        if (Canvas != null) FitToView(Canvas.ViewSize, Canvas.CurrentRenderScale);
    }

    /// <summary>100% (one image pixel per screen pixel), centred.</summary>
    public void ActualPixels()
    {
        if (Canvas == null) return;
        var size = Canvas.ViewSize;
        ZoomAt(1, new Point(size.Width / 2, size.Height / 2), Canvas.CurrentRenderScale);
        double ds = 1 / Canvas.CurrentRenderScale;
        Pan = new Point((size.Width - Document.Width * ds) / 2, (size.Height - Document.Height * ds) / 2);
    }

    /// <summary>Dock asks before closing: unsaved documents get a prompt (asynchronously).</summary>
    public override bool OnClose()
    {
        if (_closeConfirmed || !Document.IsDirty)
        {
            Main.OnDocumentClosed(this);
            return true;
        }
        _ = ConfirmCloseAsync();
        return false;
    }

    private async Task ConfirmCloseAsync()
    {
        if (await Main.ConfirmDiscardAsync(this))
        {
            _closeConfirmed = true;
            Factory?.CloseDockable(this);
        }
    }

    /// <summary>Close without asking again (after the user already confirmed).</summary>
    public void ForceClose()
    {
        _closeConfirmed = true;
        Factory?.CloseDockable(this);
    }

    public void Release()
    {
        Tiles.Dispose();
        Document.Dispose();
    }
}
