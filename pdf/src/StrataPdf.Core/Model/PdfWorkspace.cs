using StrataPdf.Core.Rendering;

namespace StrataPdf.Core.Model;

/// <summary>A PDF file the workspace takes pages from. Always a private copy in the work folder.</summary>
public sealed record PdfSource(string Id, string Path, string DisplayName);

/// <summary>One page of the document being edited.</summary>
public sealed record PageItem(Guid Id, string SourceId, int SourceIndex, double BaseWidth, double BaseHeight,
    int Rotation = 0, IReadOnlyList<Annotation>? Annotations = null)
{
    public IReadOnlyList<Annotation> Annotations { get; init; } = Annotations ?? Array.Empty<Annotation>();

    /// <summary>Size in points as displayed, after the rotation added in Strata PDF.</summary>
    public double Width => Rotation % 180 == 0 ? BaseWidth : BaseHeight;
    public double Height => Rotation % 180 == 0 ? BaseHeight : BaseWidth;
}

public enum StampPosition { BottomCenter, BottomRight, BottomLeft, TopCenter, TopRight, TopLeft }

public sealed record PageNumberSettings(bool Enabled = false, string Format = "Page {n} of {total}",
    StampPosition Position = StampPosition.BottomCenter, double FontSize = 10, int StartAt = 1,
    bool SkipFirstPage = false, double Margin = 24)
{
    public Rgba Color { get; init; } = new(60, 60, 60);

    public string TextFor(int pageIndex, int pageCount)
    {
        int first = SkipFirstPage ? 1 : 0;
        int n = pageIndex - first + StartAt;
        int total = pageCount - first + StartAt - 1;
        return Format.Replace("{n}", n.ToString()).Replace("{total}", total.ToString());
    }

    public bool AppliesTo(int pageIndex) => Enabled && !(SkipFirstPage && pageIndex == 0);
}

public sealed record WatermarkSettings(bool Enabled = false, string Text = "CONFIDENTIAL", double FontSize = 64,
    double Angle = -45)
{
    public Rgba Color { get; init; } = new(200, 40, 40, 0.18);
}

/// <summary>
/// The document being edited: an ordered list of pages taken from one or more source PDFs, with
/// per-page rotation and annotations, plus document stamps (page numbers, watermark). Nothing is
/// written to a PDF until <see cref="IO.PdfSaver"/> saves it, so every edit is cheap and undoable.
/// </summary>
public sealed class PdfWorkspace : IDisposable
{
    private readonly Dictionary<string, PdfSource> _sources = new();
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private int _version, _savedVersion;
    private int _sourceCounter;

    private sealed record Snapshot(string Name, IReadOnlyList<PageItem> Pages, PageNumberSettings PageNumbers,
        WatermarkSettings Watermark, int Version);

    public PdfWorkspace()
    {
        WorkDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "strata-pdf", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(WorkDir);
    }

    public string WorkDir { get; }
    public string? FilePath { get; set; }
    public string Name => FilePath != null ? System.IO.Path.GetFileName(FilePath) : "Untitled.pdf";
    public IReadOnlyList<PageItem> Pages { get; private set; } = Array.Empty<PageItem>();
    public PageNumberSettings PageNumbers { get; private set; } = new();
    public WatermarkSettings Watermark { get; private set; } = new();
    public IReadOnlyDictionary<string, PdfSource> Sources => _sources;
    public bool IsDirty => _version != _savedVersion;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>Raised after any change (edits, undo/redo).</summary>
    public event Action? Changed;

    // ------------------------------------------------------------------ files

    public static PdfWorkspace Open(string path)
    {
        var ws = new PdfWorkspace { FilePath = System.IO.Path.GetFullPath(path) };
        ws.Pages = ws.LoadPages(path).ToList();
        ws.MarkSaved();
        return ws;
    }

    /// <summary>Copies a PDF into the work folder and returns its pages (not yet in the document).</summary>
    private IEnumerable<PageItem> LoadPages(string path)
    {
        var src = AddSource(path, System.IO.Path.GetFileName(path));
        var sizes = PdfRenderer.PageSizes(src.Path);
        for (int i = 0; i < sizes.Count; i++)
            yield return new PageItem(Guid.NewGuid(), src.Id, i, sizes[i].Width, sizes[i].Height);
    }

    public PdfSource AddSource(string path, string displayName)
    {
        string id = $"s{++_sourceCounter}";
        string copy = System.IO.Path.Combine(WorkDir, $"{id}.pdf");
        File.Copy(path, copy, overwrite: true);
        var src = new PdfSource(id, copy, displayName);
        _sources[id] = src;
        return src;
    }

    public PdfSource SourceOf(PageItem page) => _sources[page.SourceId];

    /// <summary>Inserts all pages of another PDF (merge). Index -1 appends.</summary>
    public IReadOnlyList<PageItem> InsertPdf(string path, int index = -1)
    {
        var added = LoadPages(path).ToList();
        Commit("Insert PDF", pages =>
        {
            int at = index < 0 || index > pages.Count ? pages.Count : index;
            pages.InsertRange(at, added);
        });
        return added;
    }

    /// <summary>Points every page of one source at a new version of that file (after filling forms).</summary>
    public void ReplaceSourceFile(string sourceId, string newPath, string actionName)
    {
        var old = _sources[sourceId];
        var src = AddSource(newPath, old.DisplayName);
        Commit(actionName, pages =>
        {
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].SourceId == sourceId) pages[i] = pages[i] with { SourceId = src.Id };
        });
    }

    // ------------------------------------------------------------------ page operations

    public int IndexOf(Guid pageId) => Pages.ToList().FindIndex(p => p.Id == pageId);
    public PageItem? Page(Guid id) => Pages.FirstOrDefault(p => p.Id == id);

    public void RotatePages(IEnumerable<Guid> ids, int degrees)
    {
        var set = ids.ToHashSet();
        if (set.Count == 0) return;
        Commit(degrees > 0 ? "Rotate Right" : "Rotate Left", pages =>
        {
            for (int i = 0; i < pages.Count; i++)
                if (set.Contains(pages[i].Id))
                    pages[i] = pages[i] with { Rotation = ((pages[i].Rotation + degrees) % 360 + 360) % 360 };
        });
    }

    public bool DeletePages(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        if (set.Count == 0 || set.Count >= Pages.Count) return false; // keep at least one page
        Commit(set.Count == 1 ? "Delete Page" : "Delete Pages", pages => pages.RemoveAll(p => set.Contains(p.Id)));
        return true;
    }

    /// <summary>Moves pages (keeping their order) so the first lands at <paramref name="newIndex"/>.</summary>
    public void MovePages(IEnumerable<Guid> ids, int newIndex)
    {
        var set = ids.ToHashSet();
        var moving = Pages.Where(p => set.Contains(p.Id)).ToList();
        if (moving.Count == 0) return;
        Commit("Move Pages", pages =>
        {
            int before = pages.Take(Math.Clamp(newIndex, 0, pages.Count)).Count(p => set.Contains(p.Id));
            pages.RemoveAll(p => set.Contains(p.Id));
            pages.InsertRange(Math.Clamp(newIndex - before, 0, pages.Count), moving);
        });
    }

    // ------------------------------------------------------------------ annotations

    public void AddAnnotation(Guid pageId, Annotation a) =>
        UpdatePage(pageId, "Add " + a.Describe(), p => p with { Annotations = p.Annotations.Append(a).ToList() });

    public void ReplaceAnnotation(Guid pageId, Annotation updated, string? name = null) =>
        UpdatePage(pageId, name ?? "Edit " + updated.Describe(), p => p with
        {
            Annotations = p.Annotations.Select(x => x.Id == updated.Id ? updated : x).ToList(),
        });

    public void RemoveAnnotation(Guid pageId, Guid annotationId) =>
        UpdatePage(pageId, "Delete Annotation", p => p with
        {
            Annotations = p.Annotations.Where(x => x.Id != annotationId).ToList(),
        });

    private void UpdatePage(Guid pageId, string name, Func<PageItem, PageItem> change) =>
        Commit(name, pages =>
        {
            int i = pages.FindIndex(p => p.Id == pageId);
            if (i >= 0) pages[i] = change(pages[i]);
        });

    // ------------------------------------------------------------------ stamps

    public void SetPageNumbers(PageNumberSettings s) => Commit("Page Numbers", _ => PageNumbers = s);
    public void SetWatermark(WatermarkSettings s) => Commit("Watermark", _ => Watermark = s);

    // ------------------------------------------------------------------ undo

    private Snapshot Capture(string name) => new(name, Pages, PageNumbers, Watermark, _version);

    private void Commit(string name, Action<List<PageItem>> change)
    {
        _undo.Add(Capture(name));
        if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
        var pages = Pages.ToList();
        change(pages);
        Pages = pages;
        _version = NextVersion();
        Changed?.Invoke();
    }

    private int _versionCounter;
    private int NextVersion() => ++_versionCounter;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Capture(s.Name));
        Restore(s);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Capture(s.Name));
        Restore(s);
    }

    private void Restore(Snapshot s)
    {
        Pages = s.Pages;
        PageNumbers = s.PageNumbers;
        Watermark = s.Watermark;
        _version = s.Version;
        Changed?.Invoke();
    }

    public void MarkSaved() => _savedVersion = _version;

    public void Dispose()
    {
        PdfRenderer.Forget(_sources.Values.Select(s => s.Path));
        try
        {
            Directory.Delete(WorkDir, recursive: true);
        }
        catch (IOException)
        {
            // a renderer may still hold a file; the temp folder is cleaned up later by the OS
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
