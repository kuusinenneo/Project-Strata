using StrataPhoto.Core.Model;

namespace StrataPhoto.Core.History;

public interface IHistoryEntry
{
    string Name { get; }
    long Bytes { get; }
    void Undo(Document doc);
    void Redo(Document doc);
}

/// <summary>Entries that can absorb a following entry (e.g. dragging an opacity slider).</summary>
public interface IMergeableEntry
{
    bool TryMerge(IHistoryEntry next);
}

/// <summary>Linear undo/redo stack. <see cref="Position"/> is the number of applied entries.</summary>
public sealed class HistoryStack : Observable
{
    private readonly List<IHistoryEntry> _entries = new();
    private readonly Document _doc;
    private int _position;
    private long _bytesSinceCollect;

    public HistoryStack(Document doc, string baseName)
    {
        _doc = doc;
        BaseName = baseName;
    }

    /// <summary>Name of the initial state, shown as the first History panel row.</summary>
    public string BaseName { get; private set; }

    public IReadOnlyList<IHistoryEntry> Entries => _entries;
    public int Position => _position;
    public int MaxEntries { get; set; } = 60;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _entries.Count;
    public bool IsApplying { get; private set; }

    /// <summary>Incremented on every change, so the document can tell whether it has unsaved edits.</summary>
    public long Version { get; private set; }

    public event Action? Changed;

    public void Push(IHistoryEntry entry)
    {
        if (IsApplying) return;
        if (_position < _entries.Count)
            _entries.RemoveRange(_position, _entries.Count - _position);
        if (_position > 0 && _entries[_position - 1] is IMergeableEntry m && m.TryMerge(entry))
        {
            Version++;
            Raise();
            return;
        }
        _entries.Add(entry);
        _position = _entries.Count;
        while (_entries.Count > MaxEntries)
        {
            BaseName = _entries[0].Name;
            _entries.RemoveAt(0);
            _position--;
        }
        Version++;
        CollectIfNeeded(entry.Bytes);
        Raise();
    }

    public void Undo() => GoTo(_position - 1);
    public void Redo() => GoTo(_position + 1);

    /// <summary>Undoes or redoes until <paramref name="position"/> entries are applied.</summary>
    public void GoTo(int position)
    {
        position = Math.Clamp(position, 0, _entries.Count);
        if (position == _position) return;
        IsApplying = true;
        try
        {
            while (_position > position)
                _entries[--_position].Undo(_doc);
            while (_position < position)
                _entries[_position++].Redo(_doc);
        }
        finally
        {
            IsApplying = false;
        }
        Version++;
        Raise();
    }

    public void Clear(string baseName)
    {
        _entries.Clear();
        _position = 0;
        BaseName = baseName;
        Version++;
        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        Changed?.Invoke();
    }

    /// <summary>
    /// Skia bitmaps live in native memory the GC cannot see, so dropped history snapshots would
    /// otherwise linger. Nudge a collection after a lot of pixel data has gone through history.
    /// </summary>
    private void CollectIfNeeded(long bytes)
    {
        _bytesSinceCollect += bytes;
        if (_bytesSinceCollect < 768L * 1024 * 1024) return;
        _bytesSinceCollect = 0;
        GC.Collect();
    }
}
