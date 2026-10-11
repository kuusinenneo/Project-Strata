using SkiaSharp;
using StrataPhoto.Core.Imaging;
using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.History;

/// <summary>An in-place pixel edit: the before and after pixels of one rectangle.</summary>
public sealed class PixelPatchEntry : IHistoryEntry
{
    private readonly Layer _layer;
    private readonly bool _mask;
    private readonly SKRectI _rect;
    private readonly SKBitmap _before;
    private readonly SKBitmap _after;

    public PixelPatchEntry(string name, Layer layer, bool mask, SKRectI rect, SKBitmap before, SKBitmap after)
    {
        Name = name;
        _layer = layer;
        _mask = mask;
        _rect = rect;
        _before = before;
        _after = after;
    }

    public string Name { get; }
    public long Bytes => Bitmaps.ByteSize(_before) + Bitmaps.ByteSize(_after);

    public void Undo(Document doc) => Apply(doc, _before);
    public void Redo(Document doc) => Apply(doc, _after);

    private void Apply(Document doc, SKBitmap patch)
    {
        var target = _mask ? _layer.Mask : (_layer as PixelLayer)?.Pixels;
        if (target == null) return;
        Bitmaps.WriteRegion(target, _rect, patch);
        (_layer as PixelLayer)?.Touch();
        doc.Invalidate(_rect);
    }
}

/// <summary>A structural change: the whole layer tree (sharing bitmaps) before and after.</summary>
public sealed class StateEntry : IHistoryEntry, IMergeableEntry
{
    private readonly DocState _before;
    private DocState _after;

    public StateEntry(string name, DocState before, DocState after, string? mergeKey = null)
    {
        Name = name;
        _before = before;
        _after = after;
        MergeKey = mergeKey;
    }

    public string Name { get; }
    public string? MergeKey { get; }
    public long Bytes => _after.NewPixelBytes(_before);

    public void Undo(Document doc) => _before.Restore(doc);
    public void Redo(Document doc) => _after.Restore(doc);

    public bool TryMerge(IHistoryEntry next)
    {
        if (MergeKey == null || next is not StateEntry s || s.MergeKey != MergeKey) return false;
        _after = s._after;
        return true;
    }
}

/// <summary>Several entries recorded as one step.</summary>
public sealed class CompoundEntry : IHistoryEntry
{
    private readonly IReadOnlyList<IHistoryEntry> _parts;

    public CompoundEntry(string name, IReadOnlyList<IHistoryEntry> parts)
    {
        Name = name;
        _parts = parts;
    }

    public string Name { get; }
    public long Bytes => _parts.Sum(p => p.Bytes);

    public void Undo(Document doc)
    {
        for (int i = _parts.Count - 1; i >= 0; i--) _parts[i].Undo(doc);
    }

    public void Redo(Document doc)
    {
        foreach (var p in _parts) p.Redo(doc);
    }
}
