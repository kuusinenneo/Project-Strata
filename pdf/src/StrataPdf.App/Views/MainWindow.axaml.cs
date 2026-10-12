// Drag and drop uses the IDataObject API, obsolete in Avalonia 11.3 but still supported there.
#pragma warning disable CS0618

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Strata.Ui;
using StrataPdf.App.ViewModels;
using StrataPdf.Core.IO;

namespace StrataPdf.App.Views;

public partial class MainWindow : Window, IPdfDialogs
{
    private readonly Dictionary<PdfTool, RadioButton> _toolButtons = new();
    private bool _closeConfirmed, _syncingThumbs, _spaceDown;
    private ThumbItem? _dragThumb;
    private Point _dragStart;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDropFiles);
        DragDrop.SetAllowDrop(this, true);
        Thumbs.SelectionChanged += (_, _) => SyncThumbSelection();
        Thumbs.AddHandler(PointerPressedEvent, OnThumbPressed, RoutingStrategies.Tunnel);
        Thumbs.AddHandler(PointerMovedEvent, OnThumbMoved, RoutingStrategies.Tunnel);
        Thumbs.AddHandler(PointerReleasedEvent, (_, _) => _dragThumb = null, RoutingStrategies.Tunnel);
        Thumbs.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains("strata-page") ? DragDropEffects.Move : DragDropEffects.None);
        Thumbs.AddHandler(DragDrop.DropEvent, OnThumbDrop);
        DragDrop.SetAllowDrop(Thumbs, true);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;
    internal Controls.DocumentCanvas DocumentCanvas => Canvas;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm == null) return;
        BuildTools();
        Vm.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(MainViewModel.Tool)) SyncTools();
            if (a.PropertyName == nameof(MainViewModel.SignaturePng))
                SignaturePreview.Source = Vm.SignaturePng == null ? null : BitmapConvert.FromPng(Vm.SignaturePng);
            if (a.PropertyName == nameof(MainViewModel.CurrentPage) && !_syncingThumbs && Vm.CurrentPage < Vm.Thumbs.Count)
                Thumbs.ScrollIntoView(Vm.Thumbs[Vm.CurrentPage]);
        };
        Vm.DocumentChanged += () => Vm.LoadFormFieldsIfSourcesChanged();
    }

    private void BuildTools()
    {
        ToolsPanel.Children.Clear();
        foreach (var info in ToolInfo.All)
        {
            if (info.Tool is PdfTool.Highlight or PdfTool.Signature or PdfTool.Zoom)
                ToolsPanel.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 4), Background = this.FindResource("Strata.borderBrush") as Avalonia.Media.IBrush });
            var b = new RadioButton { Classes = { "tool" }, GroupName = "tools", Focusable = false, Content = new IconView { Icon = info.Icon } };
            ToolTip.SetTip(b, $"{info.Name} ({info.Key})");
            var tool = info.Tool;
            b.Click += (_, _) => Vm?.SelectToolCommand.Execute(tool);
            _toolButtons[tool] = b;
            ToolsPanel.Children.Add(b);
        }
        SyncTools();
    }

    private void SyncTools()
    {
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == Vm?.Tool;
    }

    // ------------------------------------------------------------------ thumbnails

    private void SyncThumbSelection()
    {
        if (Vm == null) return;
        Vm.SelectedThumbs.Clear();
        Vm.SelectedThumbs.AddRange(Thumbs.SelectedItems?.OfType<ThumbItem>() ?? Enumerable.Empty<ThumbItem>());
        if (Thumbs.SelectedItem is ThumbItem t)
        {
            _syncingThumbs = true;
            Vm.GoToPage(t.Number - 1);
            _syncingThumbs = false;
        }
    }

    private static ThumbItem? ThumbAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as ThumbItem;

    private void OnThumbPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragThumb = ThumbAt(e.Source);
        _dragStart = e.GetPosition(Thumbs);
    }

    private async void OnThumbMoved(object? sender, PointerEventArgs e)
    {
        if (_dragThumb == null || !e.GetCurrentPoint(Thumbs).Properties.IsLeftButtonPressed) return;
        if (Math.Abs(e.GetPosition(Thumbs).Y - _dragStart.Y) < 8) return;
        var data = new DataObject();
        data.Set("strata-page", _dragThumb);
        _dragThumb = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnThumbDrop(object? sender, DragEventArgs e)
    {
        if (Vm?.Workspace is not { } ws || e.Data.Get("strata-page") is not ThumbItem dragged) return;
        var target = ThumbAt(e.Source);
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (target == null || row == null || target == dragged) return;
        bool after = e.GetPosition(row).Y > row.Bounds.Height / 2;
        var moving = Vm.SelectedThumbs.Contains(dragged) ? Vm.SelectedThumbs.Select(t => t.Page.Id).ToList() : new List<Guid> { dragged.Page.Id };
        int index = ws.IndexOf(target.Page.Id) + (after ? 1 : 0);
        ws.MovePages(moving, index);
        Vm.StatusMessage = $"Moved {moving.Count} page(s)";
    }

    // ------------------------------------------------------------------ menu handlers

    private void OnShowDocumentTab(object? sender, RoutedEventArgs e) => Tabs.SelectedItem = DocTab;
    private void OnShowFormTab(object? sender, RoutedEventArgs e) => Tabs.SelectedItem = FormTab;

    private async void OnExtractImages(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await ExtractImagesToPhotoAsync(Vm);
    }

    /// <summary>Saves the images of the selected pages (or all pages) and opens them in Strata Photo.</summary>
    public async Task<IReadOnlyList<string>> ExtractImagesToPhotoAsync(MainViewModel vm, bool launch = true, bool quiet = false)
    {
        var ws = vm.Workspace;
        if (ws == null) return Array.Empty<string>();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Strata PDF",
            Path.GetFileNameWithoutExtension(ws.Name));
        var pages = vm.SelectedThumbs.Count > 0 ? vm.SelectedThumbs.Select(t => t.Page).ToList() : ws.Pages.ToList();
        var files = new List<string>();
        int skipped = 0;
        await Task.Run(() =>
        {
            foreach (var group in pages.GroupBy(p => p.SourceId))
            {
                var found = PdfImages.Extract(ws.Sources[group.Key].Path, group.Select(p => p.SourceIndex + 1).Distinct(), folder, out int s);
                skipped += s;
                files.AddRange(found.Select(f => f.FileName));
            }
        });
        if (files.Count == 0)
        {
            if (quiet) return files;
            await MessageAsync("Extract Images", skipped > 0
                ? $"The images on these pages use formats Strata PDF can't extract yet ({skipped} skipped)."
                : "No embedded images were found on these pages.");
            return files;
        }
        vm.StatusMessage = $"Extracted {files.Count} image(s) to {folder}" + (skipped > 0 ? $" ({skipped} skipped)" : "");
        if (launch && !SuiteApps.Launch(SuiteApp.Photo, files.Take(20)))
            await MessageAsync("Extract Images", $"Saved {files.Count} image(s) to:\n{folder}\n\nStrata Photo was not found next to Strata PDF, so they were not opened. Build it with photo\\build.bat.");
        return files;
    }

    // ------------------------------------------------------------------ keyboard

    private static bool IsTextInput(object? focused) =>
        focused is TextBox || (focused is Visual v && (v.FindAncestorOfType<TextBox>() != null || v.FindAncestorOfType<NumericUpDown>() != null));

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || IsTextInput(FocusManager?.GetFocusedElement())) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        void Run(System.Windows.Input.ICommand c, object? p = null)
        {
            if (c.CanExecute(p)) c.Execute(p);
        }
        bool handled = true;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.O: Run(vm.OpenCommand); break;
                case Key.I: Run(vm.InsertPdfCommand); break;
                case Key.S when shift: Run(vm.SaveAsCommand); break;
                case Key.S: Run(vm.SaveCommand); break;
                case Key.W: Run(vm.CloseDocumentCommand); break;
                case Key.Z when shift: Run(vm.RedoCommand); break;
                case Key.Z: Run(vm.UndoCommand); break;
                case Key.Y: Run(vm.RedoCommand); break;
                case Key.R when shift: Run(vm.RotateLeftCommand); break;
                case Key.R: Run(vm.RotateRightCommand); break;
                case Key.Up: Run(vm.MovePagesUpCommand); break;
                case Key.Down: Run(vm.MovePagesDownCommand); break;
                case Key.Delete: Run(vm.DeletePagesCommand); break;
                case Key.OemPlus or Key.Add: Run(vm.ZoomInCommand); break;
                case Key.OemMinus or Key.Subtract: Run(vm.ZoomOutCommand); break;
                case Key.D0 or Key.NumPad0: Run(vm.FitPageCommand); break;
                case Key.D1 or Key.NumPad1: Run(vm.ActualSizeCommand); break;
                case Key.D2 or Key.NumPad2: Run(vm.FitWidthCommand); break;
                default: handled = false; break;
            }
        }
        else
        {
            var tool = ToolInfo.All.FirstOrDefault(t => t.Key == e.Key.ToString() && !shift);
            switch (e.Key)
            {
                case Key.Delete or Key.Back: Run(vm.DeleteAnnotationCommand); break;
                case Key.Escape: vm.Selection = null; break;
                case Key.PageDown: Run(vm.NextPageCommand); break;
                case Key.PageUp: Run(vm.PreviousPageCommand); break;
                case Key.Home: Run(vm.FirstPageCommand); break;
                case Key.End: Run(vm.LastPageCommand); break;
                case Key.F1: Run(vm.ShortcutsCommand); break;
                case Key.Space:
                    if (!_spaceDown)
                    {
                        _spaceDown = true;
                        Canvas.SetSpaceHeld(true);
                    }
                    break;
                default:
                    if (tool != null) vm.SelectToolCommand.Execute(tool.Tool);
                    else handled = false;
                    break;
            }
        }
        e.Handled = handled;
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spaceDown) return;
        _spaceDown = false;
        Canvas.SetSpaceHeld(false);
        e.Handled = true;
    }

    private async void OnDropFiles(object? sender, DragEventArgs e)
    {
        if (Vm == null || e.Data.Contains("strata-page")) return;
        var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>()
            .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (files is not { Count: > 0 }) return;
        if (Vm.Workspace == null) await Vm.OpenPathsAsync(files);
        else
            foreach (var f in files) Vm.Workspace.InsertPdf(f, Vm.CurrentPage + 1);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || Vm?.Workspace == null || StartupArgs.SelfTest) return;
        if (!Vm.Workspace.IsDirty)
        {
            Vm.SetWorkspace(null);
            return;
        }
        e.Cancel = true;
        if (await Vm.ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Vm.SetWorkspace(null);
            Close();
        }
    }

    // ------------------------------------------------------------------ IPdfDialogs

    private static readonly FilePickerFileType PdfType = new("PDF documents") { Patterns = new[] { "*.pdf" } };

    public async Task<IReadOnlyList<string>> PickPdfsAsync(string title, bool multiple)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = new[] { PdfType },
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickSavePathAsync(string title, string suggestedName)
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = ".pdf",
            FileTypeChoices = new[] { PdfType },
            ShowOverwritePrompt = true,
        });
        return f?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return f.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task<string?> AskTextAsync(string title, string label, string initial, bool multiline) =>
        PdfDialogs.AskTextAsync(this, title, label, initial, multiline);

    public Task<byte[]?> SignatureAsync() => PdfDialogs.SignatureAsync(this);
    public Task<SaveOptions?> CompressOptionsAsync(long currentSize) => PdfDialogs.CompressAsync(this, currentSize);
    public Task<int?> AskNumberAsync(string title, string label, int value, int min, int max) => PdfDialogs.AskNumberAsync(this, title, label, value, min, max);
    public Task<bool?> AskSaveChangesAsync(string name) => PdfDialogs.AskSaveChangesAsync(this, name);
    public Task MessageAsync(string title, string text) => PdfDialogs.MessageAsync(this, title, text);
    public Task ShowShortcutsAsync() => PdfDialogs.ShortcutsAsync(this);
    public Task ShowAboutAsync() => PdfDialogs.AboutAsync(this);
    public void Exit() => Close();
}
