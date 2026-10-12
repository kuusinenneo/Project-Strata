using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrataPdf.Core.IO;
using StrataPdf.Core.Model;
using StrataPdf.Core.Rendering;

namespace StrataPdf.App.ViewModels;

public enum PdfTool { Select, Hand, Highlight, Pen, Text, Rectangle, Ellipse, Arrow, Line, Note, Signature, Redact, Zoom }

public sealed record ToolInfo(PdfTool Tool, string Name, string Icon, string Key, string Hint)
{
    public static readonly IReadOnlyList<ToolInfo> All = new[]
    {
        new ToolInfo(PdfTool.Select, "Select", "move", "V", "Click an annotation to select it, drag to move it, double-click text to edit. Delete removes it."),
        new ToolInfo(PdfTool.Hand, "Hand", "hand", "H", "Drag to scroll. Hold Space with any tool."),
        new ToolInfo(PdfTool.Highlight, "Highlight", "highlighter", "M", "Drag over text to highlight it."),
        new ToolInfo(PdfTool.Pen, "Pen", "brush", "P", "Draw freehand."),
        new ToolInfo(PdfTool.Text, "Text Box", "text", "T", "Drag a box (or click) and type your text."),
        new ToolInfo(PdfTool.Rectangle, "Rectangle", "rectangle", "R", "Drag to draw a rectangle."),
        new ToolInfo(PdfTool.Ellipse, "Ellipse", "ellipse", "E", "Drag to draw an ellipse."),
        new ToolInfo(PdfTool.Arrow, "Arrow", "arrow", "A", "Drag to draw an arrow."),
        new ToolInfo(PdfTool.Line, "Line", "line", "L", "Drag to draw a line."),
        new ToolInfo(PdfTool.Note, "Sticky Note", "sticky-note", "N", "Click to add a note (shown as a pop-up note in PDF readers)."),
        new ToolInfo(PdfTool.Signature, "Signature", "signature", "S", "Click or drag to place your signature. Draw or load it first under Document → Signature."),
        new ToolInfo(PdfTool.Redact, "Redact", "redact", "X", "Drag over content to remove it. Redacted areas are permanently erased when you save."),
        new ToolInfo(PdfTool.Zoom, "Zoom", "zoom", "Z", "Click to zoom in, Alt+click to zoom out. Ctrl+wheel zooms anywhere."),
    };

    public static ToolInfo Get(PdfTool t) => All.First(i => i.Tool == t);
}

public sealed partial class ThumbItem : ObservableObject
{
    public ThumbItem(PageItem page, int number)
    {
        Page = page;
        _number = number;
    }

    public PageItem Page { get; set; }
    [ObservableProperty] private int _number;
    [ObservableProperty] private Bitmap? _image;
    public string Key => $"{Page.SourceId}|{Page.SourceIndex}|{Page.Rotation}";
    public string? RenderedKey { get; set; }
}

public sealed partial class FormFieldItem : ObservableObject
{
    public FormFieldItem(string sourceId, string sourceName, FormField field)
    {
        SourceId = sourceId;
        SourceName = sourceName;
        Field = field;
        _value = field.Value;
    }

    public string SourceId { get; }
    public string SourceName { get; }
    public FormField Field { get; }
    public string Label => Field.Name;
    public bool IsText => Field.Kind == FieldKind.Text;
    public bool IsCheck => Field.Kind == FieldKind.CheckBox;
    public bool IsChoice => Field.Kind == FieldKind.Choice;
    public bool IsOther => Field.Kind is FieldKind.Radio or FieldKind.Other;
    public bool Editable => !Field.ReadOnly && !IsOther;
    public bool Changed => Value != Field.Value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Checked))]
    private string _value;

    public bool Checked
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }
}

public interface IPdfDialogs
{
    Task<IReadOnlyList<string>> PickPdfsAsync(string title, bool multiple);
    Task<string?> PickSavePathAsync(string title, string suggestedName);
    Task<string?> PickFolderAsync(string title);
    Task<string?> AskTextAsync(string title, string label, string initial, bool multiline);
    Task<byte[]?> SignatureAsync();
    Task<SaveOptions?> CompressOptionsAsync(long currentSize);
    Task<int?> AskNumberAsync(string title, string label, int value, int min, int max);
    Task<bool?> AskSaveChangesAsync(string name);
    Task MessageAsync(string title, string text);
    Task ShowShortcutsAsync();
    Task ShowAboutAsync();
    void Exit();
}

/// <summary>State and commands of the Strata PDF window.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Fonts = new[] { "Arial", "Times New Roman", "Courier New", "Segoe UI", "Georgia", "Verdana" };
    }

    public IPdfDialogs Dialogs { get; set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(WindowTitle), nameof(PageCountText))]
    private PdfWorkspace? _workspace;

    public bool HasDocument => Workspace != null;
    public string WindowTitle => Workspace == null ? "Strata PDF" : $"{Workspace.Name}{(Workspace.IsDirty ? " *" : "")} — Strata PDF";
    public string PageCountText => Workspace == null ? "" : $"Page {CurrentPage + 1} of {Workspace.Pages.Count}";

    public ObservableCollection<ThumbItem> Thumbs { get; } = new();
    public ObservableCollection<FormFieldItem> FormFields { get; } = new();
    public IReadOnlyList<string> Fonts { get; }
    public IReadOnlyList<StampPosition> StampPositions { get; } = Enum.GetValues<StampPosition>();

    /// <summary>Raised whenever pages/annotations change (the canvas repaints, thumbnails refresh).</summary>
    public event Action? DocumentChanged;
    /// <summary>Raised to scroll the canvas to a page.</summary>
    public event Action<int>? ScrollToPageRequested;

    // ------------------------------------------------------------------ tool state

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolName), nameof(ToolHint), nameof(ShowsColor), nameof(ShowsWidth), nameof(ShowsFont))]
    private PdfTool _tool = PdfTool.Select;

    public string ToolName => ToolInfo.Get(Tool).Name;
    public string ToolHint => ToolInfo.Get(Tool).Hint;
    public bool ShowsColor => Tool is PdfTool.Highlight or PdfTool.Pen or PdfTool.Text or PdfTool.Rectangle or PdfTool.Ellipse or PdfTool.Arrow or PdfTool.Line;
    public bool ShowsWidth => Tool is PdfTool.Pen or PdfTool.Rectangle or PdfTool.Ellipse or PdfTool.Arrow or PdfTool.Line;
    public bool ShowsFont => Tool is PdfTool.Text;

    public IReadOnlyList<string> Palette { get; } = new[]
    {
        "#000000", "#e5484d", "#ff8a3d", "#ffd600", "#3cb371", "#409cff", "#8b5cf6", "#ffffff",
    };

    [ObservableProperty] private string _colorHex = "#e5484d";
    [ObservableProperty] private string _highlightHex = "#ffd600";
    [ObservableProperty] private double _strokeWidth = 2;
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private string _fontFamily = "Arial";
    [ObservableProperty] private bool _fillShapes;

    public Rgba StrokeColor => Rgba.Parse(ColorHex);
    public Rgba HighlightColor => Rgba.Parse(HighlightHex, 0.4);

    [ObservableProperty] private byte[]? _signaturePng;

    // ------------------------------------------------------------------ view state

    /// <summary>1 = 100% (a point is 1/72 inch on screen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private double _zoom = 1.0;

    public string ZoomText => $"{Zoom * 100:0}%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageCountText))]
    private int _currentPage;

    [ObservableProperty] private string _statusMessage = "Open a PDF to start (Ctrl+O), or drop one on the window.";

    /// <summary>Selected annotation (and the page it is on).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionText))]
    private (Guid PageId, Guid AnnotationId)? _selection;

    public bool HasSelection => Selection != null;
    public string SelectionText => SelectedAnnotation?.Describe() ?? "";

    public Annotation? SelectedAnnotation =>
        Selection is { } s ? Workspace?.Page(s.PageId)?.Annotations.FirstOrDefault(a => a.Id == s.AnnotationId) : null;

    public List<ThumbItem> SelectedThumbs { get; } = new();

    // ------------------------------------------------------------------ stamps (Document panel)

    [ObservableProperty] private bool _pnEnabled;
    [ObservableProperty] private string _pnFormat = "Page {n} of {total}";
    [ObservableProperty] private StampPosition _pnPosition = StampPosition.BottomCenter;
    [ObservableProperty] private double _pnFontSize = 10;
    [ObservableProperty] private int _pnStart = 1;
    [ObservableProperty] private bool _pnSkipFirst;
    [ObservableProperty] private bool _wmEnabled;
    [ObservableProperty] private string _wmText = "CONFIDENTIAL";
    [ObservableProperty] private double _wmFontSize = 64;
    [ObservableProperty] private double _wmOpacity = 18;
    [ObservableProperty] private double _wmAngle = -45;
    [ObservableProperty] private string _wmColorHex = "#c82828";

    // ------------------------------------------------------------------ documents

    public void SetWorkspace(PdfWorkspace? ws)
    {
        if (Workspace != null) Workspace.Changed -= OnWorkspaceChanged;
        Workspace?.Dispose();
        Workspace = ws;
        Selection = null;
        CurrentPage = 0;
        if (ws != null)
        {
            ws.Changed += OnWorkspaceChanged;
            var pn = ws.PageNumbers;
            (PnEnabled, PnFormat, PnPosition, PnFontSize, PnStart, PnSkipFirst) = (pn.Enabled, pn.Format, pn.Position, pn.FontSize, pn.StartAt, pn.SkipFirstPage);
            var wm = ws.Watermark;
            (WmEnabled, WmText, WmFontSize, WmAngle) = (wm.Enabled, wm.Text, wm.FontSize, wm.Angle);
        }
        RebuildThumbs();
        LoadFormFields();
        RefreshCommands();
        DocumentChanged?.Invoke();
    }

    private void OnWorkspaceChanged()
    {
        if (Selection != null && SelectedAnnotation == null) Selection = null;
        CurrentPage = Math.Clamp(CurrentPage, 0, Math.Max(0, (Workspace?.Pages.Count ?? 1) - 1));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(PageCountText));
        OnPropertyChanged(nameof(SelectionText));
        RebuildThumbs();
        RefreshCommands();
        DocumentChanged?.Invoke();
    }

    private void RefreshCommands()
    {
        foreach (var prop in GetType().GetProperties())
            if (typeof(IRelayCommand).IsAssignableFrom(prop.PropertyType))
                (prop.GetValue(this) as IRelayCommand)?.NotifyCanExecuteChanged();
    }

    private void RebuildThumbs()
    {
        var pages = Workspace?.Pages ?? Array.Empty<PageItem>();
        var byId = Thumbs.ToDictionary(t => t.Page.Id);
        var list = new List<ThumbItem>();
        for (int i = 0; i < pages.Count; i++)
        {
            if (!byId.TryGetValue(pages[i].Id, out var t)) t = new ThumbItem(pages[i], i + 1);
            t.Page = pages[i];
            t.Number = i + 1;
            list.Add(t);
        }
        for (int i = Thumbs.Count - 1; i >= 0; i--)
            if (!list.Contains(Thumbs[i])) Thumbs.RemoveAt(i);
        for (int i = 0; i < list.Count; i++)
        {
            int cur = Thumbs.IndexOf(list[i]);
            if (cur == i) continue;
            if (cur >= 0) Thumbs.Move(cur, i);
            else Thumbs.Insert(i, list[i]);
        }
        _ = RenderThumbsAsync();
    }

    private async Task RenderThumbsAsync()
    {
        var ws = Workspace;
        if (ws == null) return;
        foreach (var t in Thumbs.ToList())
        {
            if (t.RenderedKey == t.Key) continue;
            var page = t.Page;
            string key = t.Key;
            string path;
            try { path = ws.SourceOf(page).Path; }
            catch (KeyNotFoundException) { continue; }
            var bmp = await Task.Run(() =>
            {
                double scale = 150.0 / Math.Max(page.Width, page.Height);
                using var sk = PdfRenderer.RenderPage(path, page.SourceIndex, scale, page.Rotation);
                return BitmapConvert.ToAvalonia(sk);
            });
            if (ws != Workspace) return;
            t.Image = bmp;
            t.RenderedKey = key;
        }
    }

    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (Workspace != null && Workspace.IsDirty && !await ConfirmDiscardAsync()) return;
        try
        {
            var ws = await Task.Run(() => PdfWorkspace.Open(paths[0]));
            SetWorkspace(ws);
            foreach (var more in paths.Skip(1)) ws.InsertPdf(more);
            if (paths.Count > 1) ws.MarkSaved();
            StatusMessage = $"Opened {Path.GetFileName(paths[0])}" + (paths.Count > 1 ? $" and merged {paths.Count - 1} more" : "");
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync("Could not open PDF", $"{Path.GetFileName(paths[0])}\n\n{ex.Message}");
        }
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (Workspace == null || !Workspace.IsDirty) return true;
        var r = await Dialogs.AskSaveChangesAsync(Workspace.Name);
        if (r == null) return false;
        return r == false || await SaveToAsync(Workspace.FilePath);
    }

    [RelayCommand]
    private async Task Open() => await OpenPathsAsync(await Dialogs.PickPdfsAsync("Open PDF", true));

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task InsertPdf()
    {
        var files = await Dialogs.PickPdfsAsync("Insert PDF after the current page", true);
        int at = CurrentPage + 1;
        foreach (var f in files)
        {
            try
            {
                at += Workspace!.InsertPdf(f, at).Count;
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync("Could not insert PDF", ex.Message);
            }
        }
        if (files.Count > 0) StatusMessage = $"Inserted {files.Count} file(s)";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Save() => SaveToAsync(Workspace?.FilePath);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task SaveAs() => SaveToAsync(null);

    private async Task<bool> SaveToAsync(string? path, SaveOptions? options = null, bool isCopy = false)
    {
        var ws = Workspace;
        if (ws == null) return false;
        path ??= await Dialogs.PickSavePathAsync(isCopy ? "Save a Copy" : "Save As", ws.Name);
        if (path == null) return false;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";
        bool redacts = ws.Pages.Any(p => p.Annotations.OfType<RedactionAnnotation>().Any());
        try
        {
            StatusMessage = "Saving…";
            await Task.Run(() => PdfSaver.Save(ws, path, options));
            if (!isCopy)
            {
                ws.FilePath = path;
                ws.MarkSaved();
                OnPropertyChanged(nameof(WindowTitle));
            }
            StatusMessage = $"Saved {Path.GetFileName(path)} ({FormatSize(new FileInfo(path).Length)})"
                            + (redacts ? " — redacted areas were permanently removed" : "");
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync("Could not save", ex.Message);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveCompressed()
    {
        var ws = Workspace!;
        long before = ws.FilePath != null && File.Exists(ws.FilePath) ? new FileInfo(ws.FilePath).Length : 0;
        var opts = await Dialogs.CompressOptionsAsync(before);
        if (opts == null) return;
        var suggested = Path.GetFileNameWithoutExtension(ws.Name) + "-compressed.pdf";
        var path = await Dialogs.PickSavePathAsync("Save Compressed Copy", suggested);
        if (path == null) return;
        if (await SaveToAsync(path, opts, isCopy: true))
        {
            long after = new FileInfo(path).Length;
            StatusMessage = before > 0
                ? $"Compressed copy saved: {FormatSize(before)} → {FormatSize(after)} ({100.0 * after / before:0}%)"
                : $"Compressed copy saved ({FormatSize(after)})";
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ExtractPages()
    {
        var ws = Workspace!;
        var ids = SelectedThumbs.Count > 0 ? SelectedThumbs.Select(t => t.Page.Id).ToList() : new List<Guid> { ws.Pages[CurrentPage].Id };
        var path = await Dialogs.PickSavePathAsync($"Extract {ids.Count} page(s)", Path.GetFileNameWithoutExtension(ws.Name) + "-pages.pdf");
        if (path == null) return;
        await SaveToAsync(path, new SaveOptions(OnlyPages: ids), isCopy: true);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Split()
    {
        var ws = Workspace!;
        var n = await Dialogs.AskNumberAsync("Split Document", "Pages per file", 1, 1, Math.Max(1, ws.Pages.Count));
        if (n == null) return;
        var folder = await Dialogs.PickFolderAsync("Folder for the split files");
        if (folder == null) return;
        string stem = Path.GetFileNameWithoutExtension(ws.Name);
        int files = 0;
        try
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < ws.Pages.Count; i += n.Value)
                {
                    var ids = ws.Pages.Skip(i).Take(n.Value).Select(p => p.Id).ToList();
                    string name = n.Value == 1 ? $"{stem}-p{i + 1}.pdf" : $"{stem}-p{i + 1}-{i + ids.Count}.pdf";
                    PdfSaver.Save(ws, Path.Combine(folder, name), new SaveOptions(OnlyPages: ids));
                    files++;
                }
            });
            StatusMessage = $"Split into {files} file(s) in {folder}";
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync("Could not split", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task CloseDocument()
    {
        if (await ConfirmDiscardAsync()) SetWorkspace(null);
    }

    [RelayCommand] private void Exit() => Dialogs.Exit();

    // ------------------------------------------------------------------ edit / pages

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Undo()
    {
        Workspace?.Undo();
        StatusMessage = "Undo";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Redo()
    {
        Workspace?.Redo();
        StatusMessage = "Redo";
    }

    private List<Guid> TargetPages() => SelectedThumbs.Count > 0
        ? SelectedThumbs.Select(t => t.Page.Id).ToList()
        : Workspace?.Pages.Count > 0 ? new List<Guid> { Workspace.Pages[Math.Min(CurrentPage, Workspace.Pages.Count - 1)].Id } : new();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void RotateRight() => Workspace?.RotatePages(TargetPages(), 90);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void RotateLeft() => Workspace?.RotatePages(TargetPages(), -90);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void DeletePages()
    {
        var ids = TargetPages();
        if (Workspace != null && !Workspace.DeletePages(ids)) StatusMessage = "A PDF needs at least one page.";
        else StatusMessage = $"Deleted {ids.Count} page(s)";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void MovePagesUp() => MovePages(-1);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void MovePagesDown() => MovePages(+1);

    private void MovePages(int dir)
    {
        var ws = Workspace;
        var ids = TargetPages();
        if (ws == null || ids.Count == 0) return;
        int first = ids.Select(ws.IndexOf).Min(), last = ids.Select(ws.IndexOf).Max();
        int target = dir < 0 ? first - 1 : last + 2;
        if (target < 0 || target > ws.Pages.Count) return;
        ws.MovePages(ids, target);
        CurrentPage = ws.IndexOf(ids[0]);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteAnnotation()
    {
        if (Selection is { } s) Workspace?.RemoveAnnotation(s.PageId, s.AnnotationId);
        Selection = null;
    }

    public async Task EditAnnotationTextAsync(Guid pageId, Annotation a)
    {
        switch (a)
        {
            case TextBoxAnnotation t:
            {
                var text = await Dialogs.AskTextAsync("Edit Text", "Text", t.Text, true);
                if (text != null) Workspace?.ReplaceAnnotation(pageId, t with { Text = text });
                break;
            }
            case NoteAnnotation n:
            {
                var text = await Dialogs.AskTextAsync("Edit Note", "Note", n.Text, true);
                if (text != null) Workspace?.ReplaceAnnotation(pageId, n with { Text = text });
                break;
            }
        }
    }

    // ------------------------------------------------------------------ view

    public void GoToPage(int index)
    {
        if (Workspace == null) return;
        CurrentPage = Math.Clamp(index, 0, Workspace.Pages.Count - 1);
        ScrollToPageRequested?.Invoke(CurrentPage);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))] private void NextPage() => GoToPage(CurrentPage + 1);
    [RelayCommand(CanExecute = nameof(HasDocument))] private void PreviousPage() => GoToPage(CurrentPage - 1);
    [RelayCommand(CanExecute = nameof(HasDocument))] private void FirstPage() => GoToPage(0);
    [RelayCommand(CanExecute = nameof(HasDocument))] private void LastPage() => GoToPage(int.MaxValue);

    public static readonly double[] ZoomSteps = { 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8 };

    [RelayCommand] private void ZoomIn() => Zoom = ZoomSteps.FirstOrDefault(z => z > Zoom * 1.01, ZoomSteps[^1]);
    [RelayCommand] private void ZoomOut() => Zoom = ZoomSteps.LastOrDefault(z => z < Zoom * 0.99, ZoomSteps[0]);
    [RelayCommand] private void ActualSize() => Zoom = 1;

    /// <summary>Set by the canvas: fits the page width / whole page into the view.</summary>
    public Action? FitWidthAction { get; set; }
    public Action? FitPageAction { get; set; }
    [RelayCommand] private void FitWidth() => FitWidthAction?.Invoke();
    [RelayCommand] private void FitPage() => FitPageAction?.Invoke();

    [RelayCommand]
    private void SelectTool(PdfTool tool)
    {
        Tool = tool;
        StatusMessage = ToolHint;
        if (tool != PdfTool.Select) Selection = null;
    }

    [RelayCommand] private void PickColor(string hex)
    {
        if (Tool == PdfTool.Highlight) HighlightHex = hex;
        else ColorHex = hex;
    }

    // ------------------------------------------------------------------ signature, stamps, forms

    [RelayCommand]
    private async Task EditSignature()
    {
        var png = await Dialogs.SignatureAsync();
        if (png == null) return;
        SignaturePng = png;
        Tool = PdfTool.Signature;
        StatusMessage = "Signature ready: click on a page to place it.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ApplyStamps()
    {
        var ws = Workspace!;
        var pn = new PageNumberSettings(PnEnabled, PnFormat, PnPosition, Math.Clamp(PnFontSize, 4, 72), PnStart, PnSkipFirst);
        var wm = new WatermarkSettings(WmEnabled, WmText, Math.Clamp(WmFontSize, 6, 300), WmAngle)
        {
            Color = Rgba.Parse(WmColorHex, Math.Clamp(WmOpacity, 1, 100) / 100),
        };
        if (pn != ws.PageNumbers) ws.SetPageNumbers(pn);
        if (wm != ws.Watermark) ws.SetWatermark(wm);
        StatusMessage = "Page numbers and watermark are shown on the pages and added when you save.";
    }

    public void LoadFormFields()
    {
        FormFields.Clear();
        var ws = Workspace;
        if (ws == null) return;
        foreach (var sourceId in ws.Pages.Select(p => p.SourceId).Distinct())
        {
            var src = ws.Sources[sourceId];
            IReadOnlyList<FormField> fields;
            try { fields = PdfForms.Read(src.Path); }
            catch { continue; }
            foreach (var f in fields) FormFields.Add(new FormFieldItem(sourceId, src.DisplayName, f));
        }
        OnPropertyChanged(nameof(HasFormFields));
    }

    public bool HasFormFields => FormFields.Count > 0;

    private string _formSources = "";

    /// <summary>Reloads the form panel when pages from different files are now in the document.</summary>
    public void LoadFormFieldsIfSourcesChanged()
    {
        string key = Workspace == null ? "" : string.Join(",", Workspace.Pages.Select(x => x.SourceId).Distinct());
        if (key == _formSources) return;
        _formSources = key;
        LoadFormFields();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ApplyForm()
    {
        var ws = Workspace!;
        var changed = FormFields.Where(f => f.Changed && f.Editable).GroupBy(f => f.SourceId).ToList();
        if (changed.Count == 0)
        {
            StatusMessage = "No form fields were changed.";
            return;
        }
        try
        {
            foreach (var group in changed)
            {
                string tmp = Path.Combine(ws.WorkDir, $"form-{Guid.NewGuid():N}.pdf");
                var values = group.ToDictionary(f => f.Field.Name, f => f.Value);
                await Task.Run(() => PdfForms.Fill(ws.Sources[group.Key].Path, tmp, values));
                ws.ReplaceSourceFile(group.Key, tmp, "Fill Form");
            }
            LoadFormFields();
            StatusMessage = "Form updated.";
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync("Could not fill the form", ex.Message);
        }
    }

    [RelayCommand] private Task Shortcuts() => Dialogs.ShowShortcutsAsync();
    [RelayCommand] private Task About() => Dialogs.ShowAboutAsync();

    public static string FormatSize(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        _ => $"{b / 1024.0 / 1024.0:0.##} MB",
    };

    public void Post(Action a) => Dispatcher.UIThread.Post(a);
}
