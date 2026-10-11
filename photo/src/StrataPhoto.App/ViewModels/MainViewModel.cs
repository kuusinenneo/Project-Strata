using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using SkiaSharp;
using StrataPhoto.App.ViewModels.Panels;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.IO;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Operations;
using StrataPhoto.Core.Selections;
using StrataPhoto.Core.Tools;

namespace StrataPhoto.App.ViewModels;

/// <summary>Application state: open documents, tools, panels, dock layout and all menu commands.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private ClipboardImage? _clipboard;
    private int _untitled;

    public MainViewModel()
    {
        Tools = new ToolController(this);
        Panels = new PanelSet(this);
        Factory = new DockFactory(this);
        Layout = Factory.CreateLayout();
        Factory.InitLayout(Layout);
        Factory.ActiveDockableChanged += (_, e) =>
        {
            if (e.Dockable is DocumentViewModel d && d != ActiveDocument) ActiveDocument = d;
        };
        Factory.FocusedDockableChanged += (_, e) =>
        {
            if (e.Dockable is DocumentViewModel d && d != ActiveDocument) ActiveDocument = d;
        };
        Tools.Settings.PropertyChanged += (_, e) => OnToolSettingChanged(e.PropertyName);
        FontFamilies = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n).Distinct().ToList();
    }

    public IDialogService Dialogs { get; set; } = null!;
    public ToolController Tools { get; }
    public PanelSet Panels { get; }
    public DockFactory Factory { get; }

    [ObservableProperty] private IRootDock _layout;

    public ObservableCollection<DocumentViewModel> Documents { get; } = new();
    public IReadOnlyList<string> FontFamilies { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(WindowTitle), nameof(DocSizeText), nameof(ZoomText))]
    private DocumentViewModel? _activeDocument;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private bool _showRulers = true;
    [ObservableProperty] private TextEditSession? _textEdit;

    public bool HasDocument => ActiveDocument != null;
    public string WindowTitle => ActiveDocument == null ? "Strata Photo" : $"{ActiveDocument.Document.Title} — Strata Photo";
    public string DocSizeText => ActiveDocument == null ? "" : $"{ActiveDocument.Document.Width} × {ActiveDocument.Document.Height} px";
    public string ZoomText => ActiveDocument?.ZoomText ?? "";

    private Document? Doc => ActiveDocument?.Document;

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        Tools.CancelActive();
        EndTextEdit();
        if (oldValue != null) oldValue.Document.PropertyChanged -= OnDocPropertyChanged;
        if (newValue != null) newValue.Document.PropertyChanged += OnDocPropertyChanged;
        foreach (var p in Panels.All) p.OnDocumentChanged(newValue);
        if (newValue != null) Factory.Activate(newValue);
        CursorText = "";
        foreach (var prop in GetType().GetProperties())
            if (typeof(IRelayCommand).IsAssignableFrom(prop.PropertyType))
                (prop.GetValue(this) as IRelayCommand)?.NotifyCanExecuteChanged();
    }

    private void OnDocPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Document.Title) or nameof(Document.Width) or nameof(Document.Height))
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(DocSizeText));
        }
    }

    public void OnViewChanged(DocumentViewModel d)
    {
        if (d == ActiveDocument) OnPropertyChanged(nameof(ZoomText));
    }

    public void OnCursorMoved(DocumentViewModel d)
    {
        if (d != ActiveDocument) return;
        if (d.CursorDoc is { } p && p.X >= 0 && p.Y >= 0 && p.X < d.Document.Width && p.Y < d.Document.Height)
        {
            int x = (int)p.X, y = (int)p.Y;
            var c = PaintOps.SampleColor(d.Document, x, y, true);
            CursorText = $"X {x}  Y {y}   #{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
        }
        else CursorText = "";
    }

    // ------------------------------------------------------------------ documents

    public DocumentViewModel AddDocument(Document doc)
    {
        var vm = new DocumentViewModel(this, doc);
        Documents.Add(vm);
        Factory.AddDocument(vm);
        ActiveDocument = vm;
        return vm;
    }

    public void OnDocumentClosed(DocumentViewModel d)
    {
        if (TextEdit?.Doc == d) EndTextEdit();
        if (ActiveDocument == d) Tools.CancelActive();
        Documents.Remove(d);
        if (ActiveDocument == d) ActiveDocument = Documents.LastOrDefault();
        d.Release();
    }

    public async Task<bool> ConfirmDiscardAsync(DocumentViewModel d)
    {
        Factory.Activate(d);
        var choice = await Dialogs.AskSaveChangesAsync(d.Document.Name);
        if (choice == SaveChoice.Cancel) return false;
        if (choice == SaveChoice.Save) return await SaveDocumentAsync(d, false);
        return true;
    }

    /// <summary>Asks about every unsaved document before the app exits.</summary>
    public async Task<bool> ConfirmExitAsync()
    {
        foreach (var d in Documents.Where(d => d.Document.IsDirty).ToList())
            if (!await ConfirmDiscardAsync(d)) return false;
        return true;
    }

    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var existing = Documents.FirstOrDefault(d => string.Equals(d.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveDocument = existing;
                continue;
            }
            try
            {
                var doc = await Task.Run(() => path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)
                    ? ProjectFile.Load(path)
                    : ImageIO.Open(path));
                AddDocument(doc);
                StatusMessage = $"Opened {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                await Dialogs.ShowMessageAsync("Could not open file", $"{Path.GetFileName(path)}\n\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task NewDocument()
    {
        var settings = await Dialogs.NewDocumentAsync(Tools.Settings.Background);
        if (settings == null) return;
        var doc = Document.CreateBlank(settings.Width, settings.Height, settings.Name, settings.Background);
        doc.MarkSaved();
        AddDocument(doc);
    }

    public string NextUntitledName() => $"Untitled-{++_untitled}";

    [RelayCommand]
    private async Task Open()
    {
        var files = await Dialogs.PickFilesToOpenAsync();
        await OpenPathsAsync(files);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Save()
    {
        if (ActiveDocument != null) await SaveDocumentAsync(ActiveDocument, false);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAs()
    {
        if (ActiveDocument != null) await SaveDocumentAsync(ActiveDocument, true);
    }

    private async Task<bool> SaveDocumentAsync(DocumentViewModel d, bool saveAs)
    {
        Tools.CommitActive();
        EndTextEdit();
        var path = d.Document.FilePath;
        if (saveAs || path == null)
        {
            var suggested = Path.GetFileNameWithoutExtension(d.Document.Name);
            path = await Dialogs.PickProjectSavePathAsync(suggested + ProjectFile.Extension);
            if (path == null) return false;
        }
        try
        {
            StatusMessage = "Saving…";
            var target = path;
            ProjectFile.Save(d.Document, target);
            StatusMessage = $"Saved {Path.GetFileName(target)}";
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync("Could not save", ex.Message);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Export()
    {
        if (Doc == null) return;
        Tools.CommitActive();
        var result = await Dialogs.ExportAsync(Doc);
        if (result == null) return;
        try
        {
            var (path, options) = result.Value;
            var doc = Doc;
            await Task.Run(() => ImageIO.Export(doc, path, options));
            StatusMessage = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync("Could not export", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void CloseDocument()
    {
        if (ActiveDocument != null) Factory.CloseDockable(ActiveDocument);
    }

    [RelayCommand] private void Exit() => Dialogs.Exit();

    // ------------------------------------------------------------------ edit

    private void BeforeEdit()
    {
        Tools.CancelActive();
        EndTextEdit();
        Panels.Adjustments.Cancel();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Undo()
    {
        if (Doc == null) return;
        if (Tools.CancelActive()) return; // Esc-like: first undo abandons an unfinished crop/transform
        EndTextEdit();
        Panels.Adjustments.Cancel();
        Doc.History.Undo();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Redo()
    {
        BeforeEdit();
        Doc?.History.Redo();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Copy()
    {
        if (Doc == null) return;
        _clipboard = LayerOps.Copy(Doc) ?? _clipboard;
        StatusMessage = _clipboard == null ? "Nothing to copy." : "Copied.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void CopyMerged()
    {
        if (Doc == null) return;
        _clipboard = LayerOps.Copy(Doc, merged: true) ?? _clipboard;
        StatusMessage = "Copied merged.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Cut()
    {
        if (Doc == null) return;
        BeforeEdit();
        _clipboard = LayerOps.Cut(Doc) ?? _clipboard;
    }

    [RelayCommand]
    private void Paste()
    {
        if (_clipboard == null)
        {
            StatusMessage = "The clipboard is empty.";
            return;
        }
        if (Doc == null)
        {
            var doc = Document.CreateBlank(_clipboard.Pixels.Width, _clipboard.Pixels.Height, NextUntitledName(), null);
            doc.MarkSaved();
            AddDocument(doc);
        }
        BeforeEdit();
        LayerOps.Paste(Doc!, _clipboard);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Clear()
    {
        if (Doc == null) return;
        BeforeEdit();
        if (!PixelOps.ClearSelection(Doc)) StatusMessage = PixelOps.CannotEditReason(Doc) ?? "";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FillForeground()
    {
        if (Doc == null) return;
        BeforeEdit();
        if (!PixelOps.Fill(Doc, Tools.Settings.Foreground)) StatusMessage = PixelOps.CannotEditReason(Doc) ?? "";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FillBackground()
    {
        if (Doc == null) return;
        BeforeEdit();
        if (!PixelOps.Fill(Doc, Tools.Settings.Background)) StatusMessage = PixelOps.CannotEditReason(Doc) ?? "";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FreeTransform()
    {
        if (ActiveDocument == null) return;
        EndTextEdit();
        Panels.Adjustments.Cancel();
        Tools.BeginFreeTransform(ActiveDocument);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipLayerHorizontal() => FlipLayer(true);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipLayerVertical() => FlipLayer(false);

    private void FlipLayer(bool horizontal)
    {
        if (Doc == null) return;
        BeforeEdit();
        using var s = TransformSession.Begin(Doc);
        if (s == null)
        {
            StatusMessage = "Nothing to flip on this layer.";
            return;
        }
        var b = s.SourceBounds;
        s.SetMatrix(horizontal
            ? new SKMatrix(-1, 0, b.Left + b.Right, 0, 1, 0, 0, 0, 1)
            : new SKMatrix(1, 0, 0, 0, -1, b.Top + b.Bottom, 0, 0, 1));
        s.Commit(horizontal ? "Flip Horizontal" : "Flip Vertical");
    }

    [RelayCommand]
    private void SwapColors() => Tools.Settings.SwapColors();

    [RelayCommand]
    private void ResetColors() => Tools.Settings.ResetColors();

    // ------------------------------------------------------------------ image

    [RelayCommand]
    private void Adjust(string id)
    {
        BeforeEdit();
        Panels.Adjustments.Begin(id);
    }

    [RelayCommand]
    private async Task Filter(string id)
    {
        if (ActiveDocument == null) return;
        BeforeEdit();
        var op = OperationCatalog.Filters.Select(f => f()).First(o => o.Id == id);
        var preview = OperationPreview.Start(ActiveDocument, op, out var reason);
        if (preview == null)
        {
            StatusMessage = reason ?? "";
            await Dialogs.ShowMessageAsync(op.Name, reason ?? "This layer can't be filtered.");
            return;
        }
        preview.Schedule();
        if (await Dialogs.RunFilterAsync(preview)) preview.Commit();
        else preview.Cancel();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ImageSize()
    {
        if (Doc == null) return;
        BeforeEdit();
        var size = await Dialogs.ImageSizeAsync(Doc);
        if (size != null) ImageTransforms.ResizeImage(Doc, size.Value.Width, size.Value.Height);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task CanvasSize()
    {
        if (Doc == null) return;
        BeforeEdit();
        var r = await Dialogs.CanvasSizeAsync(Doc);
        if (r != null) ImageTransforms.ResizeCanvas(Doc, r.Value.Width, r.Value.Height, r.Value.AnchorX, r.Value.AnchorY, Tools.Settings.Background);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Rotate(string degrees)
    {
        if (Doc == null) return;
        BeforeEdit();
        ImageTransforms.Rotate(Doc, float.Parse(degrees, System.Globalization.CultureInfo.InvariantCulture));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task RotateArbitrary()
    {
        if (Doc == null) return;
        BeforeEdit();
        var angle = await Dialogs.AskNumberAsync("Rotate Canvas", "Angle (clockwise)", 15, -359.99, 359.99, "°");
        if (angle is { } a && Math.Abs(a) > 0.001) ImageTransforms.Rotate(Doc, (float)a);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipCanvas(string direction)
    {
        if (Doc == null) return;
        BeforeEdit();
        ImageTransforms.Flip(Doc, direction == "h");
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void CropToSelection()
    {
        if (Doc == null) return;
        BeforeEdit();
        if (Doc.Selection.IsEmpty) StatusMessage = "Make a selection first (or use the Crop tool).";
        else ImageTransforms.Crop(Doc, Doc.Selection.Bounds);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Flatten()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.Flatten(Doc);
    }

    // ------------------------------------------------------------------ layer

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void NewLayer()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.AddLayer(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void NewGroup()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.AddGroup(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void DuplicateLayer()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.Duplicate(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void DeleteLayer()
    {
        if (Doc == null) return;
        BeforeEdit();
        int before = Doc.AllLayers.Count();
        LayerOps.Delete(Doc);
        if (Doc.AllLayers.Count() == before) StatusMessage = "A document needs at least one layer.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void GroupLayers()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.GroupActive(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Ungroup()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.Ungroup(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddMaskReveal()
    {
        if (Doc == null) return;
        BeforeEdit();
        if (Doc.ActiveLayer?.Mask != null) StatusMessage = "This layer already has a mask.";
        LayerOps.AddMask(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddMaskHide()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.AddMask(Doc, hideAll: true);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void DeleteMask()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.DeleteMask(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ApplyMask()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.ApplyMask(Doc);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Rasterize()
    {
        if (Doc == null) return;
        BeforeEdit();
        if (LayerOps.Rasterize(Doc) == null) StatusMessage = "Select a text layer to rasterize.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void MergeDown()
    {
        if (Doc == null) return;
        BeforeEdit();
        int before = Doc.AllLayers.Count();
        LayerOps.MergeDown(Doc);
        if (Doc.AllLayers.Count() == before) StatusMessage = "Merge Down needs a pixel layer below the active layer.";
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void BringForward()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.Move(Doc, +1);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SendBackward()
    {
        if (Doc == null) return;
        BeforeEdit();
        LayerOps.Move(Doc, -1);
    }

    // ------------------------------------------------------------------ select

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SelectAll()
    {
        if (Doc == null) return;
        Tools.CancelActive();
        Doc.SetSelection("Select All", SelectionOps.All(Doc.Width, Doc.Height));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Deselect()
    {
        if (Doc == null || Doc.Selection.IsEmpty) return;
        Tools.CancelActive();
        Doc.SetSelection("Deselect", Selection.None);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void InvertSelection()
    {
        if (Doc == null) return;
        Tools.CancelActive();
        Doc.SetSelection("Select Inverse", SelectionOps.Invert(Doc.Selection, Doc.Width, Doc.Height));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Feather()
    {
        if (Doc == null) return;
        if (Doc.Selection.IsEmpty)
        {
            StatusMessage = "Make a selection first.";
            return;
        }
        var r = await Dialogs.AskNumberAsync("Feather Selection", "Feather radius", 5, 0.1, 250, "px");
        if (r is { } radius) Doc.SetSelection("Feather", SelectionOps.Feather(Doc.Selection, (float)radius));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SelectLayerPixels()
    {
        if (Doc?.ActivePixelLayer is { } p) Doc.SetSelection("Load Selection", SelectionOps.FromAlpha(p.Pixels));
    }

    // ------------------------------------------------------------------ view / window / help

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ZoomIn() => ActiveDocument?.ZoomStep(+1, null, ActiveDocument.Canvas?.CurrentRenderScale ?? 1);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ZoomOut() => ActiveDocument?.ZoomStep(-1, null, ActiveDocument.Canvas?.CurrentRenderScale ?? 1);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FitOnScreen() => ActiveDocument?.Fit();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ActualPixels() => ActiveDocument?.ActualPixels();

    [RelayCommand] private void ToggleRulers() => ShowRulers = !ShowRulers;

    [RelayCommand]
    private void ResetLayout()
    {
        var layout = Factory.CreateLayout();
        Factory.InitLayout(layout);
        Layout = layout;
        if (ActiveDocument != null) Factory.Activate(ActiveDocument);
        StatusMessage = "Panels reset to the default layout.";
    }

    [RelayCommand]
    private void ShowPanelById(string id)
    {
        var panel = Panels.All.FirstOrDefault(p => p.Id == id);
        if (panel != null) ShowPanel(panel);
    }

    public void ShowPanel(PanelBase panel) => Factory.ShowPanel(panel);

    [RelayCommand] private Task Shortcuts() => Dialogs.ShowShortcutsAsync();
    [RelayCommand] private Task About() => Dialogs.ShowAboutAsync();

    [RelayCommand]
    private void SelectTool(ToolKind kind)
    {
        EndTextEdit();
        Tools.Select(kind);
        StatusMessage = Tools.ToolHint;
    }

    // ------------------------------------------------------------------ text tool

    public void BeginTextEdit(DocumentViewModel d, SKPoint pos)
    {
        EndTextEdit();
        var doc = d.Document;
        // Click on existing text edits it; anywhere else starts a new text layer.
        var hit = doc.AllLayers.OfType<TextLayer>().Reverse()
            .FirstOrDefault(t => t.Visible && TextRenderer.Measure(t.Text).Contains(pos));
        if (hit != null)
        {
            if (hit.Locked)
            {
                StatusMessage = "That text layer is locked.";
                return;
            }
            doc.ActiveLayer = hit;
            var s = Tools.Settings;
            s.FontFamily = hit.Text.FontFamily;
            s.FontSize = hit.Text.FontSize;
            s.TextAlign = hit.Text.Align;
            s.Bold = hit.Text.Bold;
            s.Italic = hit.Text.Italic;
            TextEdit = new TextEditSession(d, hit, false);
        }
        else
        {
            var s = Tools.Settings;
            var props = new TextProps("", s.FontFamily, s.FontSize, s.Foreground, s.TextAlign, s.Bold, s.Italic,
                pos.X, pos.Y - s.FontSize * 0.8f);
            var layer = LayerOps.AddText(doc, props);
            TextEdit = new TextEditSession(d, layer, true);
        }
        StatusMessage = "Type your text. Ctrl+Enter or Esc to finish; style it in the options bar or Properties.";
    }

    public void EndTextEdit()
    {
        if (TextEdit == null) return;
        var t = TextEdit;
        TextEdit = null;
        t.Finish();
    }

    private bool _applyingTextStyle;

    private void OnToolSettingChanged(string? name)
    {
        if (TextEdit == null || _applyingTextStyle) return;
        var s = Tools.Settings;
        var p = TextEdit.Layer.Text;
        TextProps? updated = name switch
        {
            nameof(ToolSettings.FontFamily) => p with { FontFamily = s.FontFamily },
            nameof(ToolSettings.FontSize) => p with { FontSize = s.FontSize },
            nameof(ToolSettings.TextAlign) => p with { Align = s.TextAlign },
            nameof(ToolSettings.Bold) => p with { Bold = s.Bold },
            nameof(ToolSettings.Italic) => p with { Italic = s.Italic },
            nameof(ToolSettings.Foreground) => p with { Color = s.Foreground },
            _ => null,
        };
        if (updated == null) return;
        _applyingTextStyle = true;
        try { TextEdit.Apply(updated); }
        finally { _applyingTextStyle = false; }
    }
}
