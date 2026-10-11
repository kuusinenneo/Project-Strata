using SkiaSharp;
using StrataPhoto.Core.Model;

namespace StrataPhoto.App.Canvas;

/// <summary>
/// GPU copy of a document's composite, split into 256×256 tiles. The UI thread marks tiles dirty
/// when the composite changes; the render thread re-uploads only those tiles (and only when they
/// are visible) before drawing. A brush dab therefore costs a few small texture uploads instead of
/// a full-image upload.
/// </summary>
public sealed class TileCache : IDisposable
{
    public const int TileSize = 256;

    // GPU images must be released on the render thread; closed documents park them here.
    private static readonly List<SKImage> Trash = new();

    private readonly Document _doc;
    private readonly object _lock = new();
    private int _w, _h, _cols, _rows;
    private bool[] _dirty = Array.Empty<bool>();
    private SKImage?[] _images = Array.Empty<SKImage?>();
    private GRContext? _context;
    private bool _contextKnown;
    private bool _disposed;

    public TileCache(Document doc) => _doc = doc;

    /// <summary>Whether the last draw had a GPU context (diagnostics / self-test).</summary>
    public static bool? LastDrawUsedGpu { get; private set; }

    /// <summary>Number of tile uploads so far (diagnostics / tests).</summary>
    public long Uploads { get; private set; }

    public void MarkDirty(SKRectI r)
    {
        lock (_lock)
        {
            if (_cols == 0) return;
            int x0 = Math.Max(0, r.Left / TileSize), y0 = Math.Max(0, r.Top / TileSize);
            int x1 = Math.Min(_cols - 1, (r.Right - 1) / TileSize), y1 = Math.Min(_rows - 1, (r.Bottom - 1) / TileSize);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _dirty[y * _cols + x] = true;
        }
    }

    /// <summary>Render thread: refresh visible dirty tiles and draw them (canvas in document coordinates).</summary>
    public void Draw(SKCanvas canvas, GRContext? gr, SKRect visibleDoc, bool smooth)
    {
        EmptyTrash();
        if (_disposed) return;
        LastDrawUsedGpu = gr != null;
        int c0, c1, r0, r1, cols;
        SKImage?[] images;
        lock (_doc.Compositor.Sync)
        lock (_lock)
        {
            var comp = _doc.Compositor.Composite;
            if (comp.Width != _w || comp.Height != _h || !_contextKnown || !ReferenceEquals(gr, _context))
                Reset(comp.Width, comp.Height, gr);
            c0 = Math.Max(0, (int)Math.Floor(visibleDoc.Left / TileSize));
            r0 = Math.Max(0, (int)Math.Floor(visibleDoc.Top / TileSize));
            c1 = Math.Min(_cols - 1, (int)Math.Floor(visibleDoc.Right / TileSize));
            r1 = Math.Min(_rows - 1, (int)Math.Floor(visibleDoc.Bottom / TileSize));
            for (int ty = r0; ty <= r1; ty++)
            for (int tx = c0; tx <= c1; tx++)
            {
                int i = ty * _cols + tx;
                if (_images[i] != null && !_dirty[i]) continue;
                _images[i]?.Dispose();
                _images[i] = Upload(comp, tx, ty, gr);
                _dirty[i] = false;
                Uploads++;
            }
            images = _images;
            cols = _cols;
        }
        using var paint = new SKPaint
        {
            FilterQuality = smooth ? SKFilterQuality.Medium : SKFilterQuality.None,
            IsAntialias = false,
        };
        for (int ty = r0; ty <= r1; ty++)
        for (int tx = c0; tx <= c1; tx++)
        {
            var img = images[ty * cols + tx];
            if (img == null) continue;
            var dst = SKRect.Create(tx * TileSize, ty * TileSize, img.Width, img.Height);
            canvas.DrawImage(img, dst, paint);
        }
    }

    private SKImage? Upload(SKBitmap comp, int tx, int ty, GRContext? gr)
    {
        int x = tx * TileSize, y = ty * TileSize;
        int w = Math.Min(TileSize, comp.Width - x), h = Math.Min(TileSize, comp.Height - y);
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var ptr = comp.GetPixels() + y * comp.RowBytes + x * 4;
        // Copy first: the composite keeps changing on the UI thread and GPU uploads may be deferred.
        var raster = SKImage.FromPixelCopy(info, ptr, comp.RowBytes);
        if (gr == null || raster == null) return raster;
        var texture = raster.ToTextureImage(gr, true);
        if (texture == null) return raster;
        raster.Dispose();
        return texture;
    }

    private void Reset(int w, int h, GRContext? gr)
    {
        foreach (var img in _images) img?.Dispose();
        _w = w;
        _h = h;
        _cols = (w + TileSize - 1) / TileSize;
        _rows = (h + TileSize - 1) / TileSize;
        _images = new SKImage?[_cols * _rows];
        _dirty = new bool[_cols * _rows];
        _context = gr;
        _contextKnown = true;
    }

    private static void EmptyTrash()
    {
        lock (Trash)
        {
            foreach (var img in Trash) img.Dispose();
            Trash.Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            lock (Trash)
                Trash.AddRange(_images.Where(i => i != null)!);
            _images = Array.Empty<SKImage?>();
            _cols = _rows = 0;
        }
    }
}
