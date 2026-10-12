using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SkiaSharp;
using Strata.Ui;
using StrataPhoto.App.ViewModels;
using StrataPhoto.Core.IO;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Tools;

// Drag and drop uses the IDataObject API, obsolete in Avalonia 11.3 but still supported there.
#pragma warning disable CS0618

namespace StrataPhoto.App.Views;

public partial class MainWindow : Window, IDialogService
{
    private readonly Dictionary<ToolKind, RadioButton> _toolButtons = new();
    private bool _closeConfirmed;
    private bool _spaceDown;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
        ColorPair.SquareClicked += bg =>
        {
            if (Vm == null) return;
            Vm.Panels.Color.EditingForeground = !bg;
            Vm.ShowPanel(Vm.Panels.Color);
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm == null) return;
        BuildToolStrip();
        Vm.Tools.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ToolController.Current)) SyncToolButtons();
        };
        Vm.Tools.Settings.PropertyChanged += (_, _) => SyncColors();
        SyncColors();
    }

    private void BuildToolStrip()
    {
        ToolsPanel.Children.Clear();
        _toolButtons.Clear();
        ToolKind? previousGroupEnd = null;
        foreach (var info in ToolInfo.All)
        {
            // thin separators between tool families
            if (info.Kind is ToolKind.Crop or ToolKind.Brush or ToolKind.Fill or ToolKind.Text or ToolKind.Hand && previousGroupEnd != null)
                ToolsPanel.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 4), Background = this.FindResource("Strata.borderBrush") as Avalonia.Media.IBrush });
            var button = new RadioButton
            {
                Classes = { "tool" },
                GroupName = "tools",
                Focusable = false,
                Content = new IconView { Icon = info.Icon },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            ToolTip.SetTip(button, $"{info.Name} ({info.Key})");
            var kind = info.Kind;
            button.Click += (_, _) => Vm?.SelectToolCommand.Execute(kind);
            _toolButtons[kind] = button;
            ToolsPanel.Children.Add(button);
            previousGroupEnd = kind;
        }
        SyncToolButtons();
    }

    private void SyncToolButtons()
    {
        if (Vm == null) return;
        foreach (var (kind, b) in _toolButtons) b.IsChecked = kind == Vm.Tools.Current;
    }

    private void SyncColors()
    {
        if (Vm == null) return;
        var s = Vm.Tools.Settings;
        ColorPair.ForegroundColor = Avalonia.Media.Color.FromArgb(255, s.Foreground.Red, s.Foreground.Green, s.Foreground.Blue);
        ColorPair.BackgroundColor = Avalonia.Media.Color.FromArgb(255, s.Background.Red, s.Background.Green, s.Background.Blue);
    }

    // ------------------------------------------------------------------ keyboard

    private static bool IsTextInput(object? focused) =>
        focused is TextBox || (focused is Visual v && v.FindAncestorOfType<NumericUpDown>() != null)
                           || (focused is Visual v2 && v2.FindAncestorOfType<TextBox>() != null);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;
        var focused = FocusManager?.GetFocusedElement();
        var mods = e.KeyModifiers;
        bool ctrl = mods.HasFlag(KeyModifiers.Control), shift = mods.HasFlag(KeyModifiers.Shift), alt = mods.HasFlag(KeyModifiers.Alt);

        if (IsTextInput(focused))
        {
            // Text boxes keep their keys, except the ones that finish typing on the canvas.
            if (vm.TextEdit != null && (e.Key == Key.Escape || (e.Key == Key.Enter && ctrl)))
            {
                vm.EndTextEdit();
                vm.ActiveDocument?.Canvas?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Focus();
                e.Handled = true;
            }
            return;
        }
        if (focused is ComboBox { IsDropDownOpen: true }) return;

        e.Handled = HandleShortcut(vm, e.Key, ctrl, shift, alt);
    }

    private bool HandleShortcut(MainViewModel vm, Key key, bool ctrl, bool shift, bool alt)
    {
        void Run(System.Windows.Input.ICommand c, object? p = null)
        {
            if (c.CanExecute(p)) c.Execute(p);
        }

        if (ctrl)
        {
            switch (key)
            {
                case Key.N when shift: Run(vm.NewLayerCommand); return true;
                case Key.N: Run(vm.NewDocumentCommand); return true;
                case Key.O: Run(vm.OpenCommand); return true;
                case Key.S when shift: Run(vm.SaveAsCommand); return true;
                case Key.S: Run(vm.SaveCommand); return true;
                case Key.E when shift: Run(vm.ExportCommand); return true;
                case Key.W when alt && shift: Run(vm.ExportCommand); return true;
                case Key.E: Run(vm.MergeDownCommand); return true;
                case Key.W: Run(vm.CloseDocumentCommand); return true;
                case Key.Z when shift: Run(vm.RedoCommand); return true;
                case Key.Z when alt: Run(vm.UndoCommand); return true;
                case Key.Z: Run(vm.UndoCommand); return true;
                case Key.Y: Run(vm.RedoCommand); return true;
                case Key.X: Run(vm.CutCommand); return true;
                case Key.C when alt: Run(vm.CanvasSizeCommand); return true;
                case Key.C when shift: Run(vm.CopyMergedCommand); return true;
                case Key.C: Run(vm.CopyCommand); return true;
                case Key.V: Run(vm.PasteCommand); return true;
                case Key.J: Run(vm.DuplicateLayerCommand); return true;
                case Key.G when shift: Run(vm.UngroupCommand); return true;
                case Key.G: Run(vm.GroupLayersCommand); return true;
                case Key.D: Run(vm.DeselectCommand); return true;
                case Key.A: Run(vm.SelectAllCommand); return true;
                case Key.I when alt: Run(vm.ImageSizeCommand); return true;
                case Key.I when shift: Run(vm.InvertSelectionCommand); return true;
                case Key.I: Run(vm.AdjustCommand, "invert"); return true;
                case Key.T: Run(vm.FreeTransformCommand); return true;
                case Key.L: Run(vm.AdjustCommand, "levels"); return true;
                case Key.M: Run(vm.AdjustCommand, "curves"); return true;
                case Key.U: Run(vm.AdjustCommand, "hue-saturation"); return true;
                case Key.B when alt && shift: Run(vm.AdjustCommand, "black-white"); return true;
                case Key.B: Run(vm.AdjustCommand, "color-balance"); return true;
                case Key.R: Run(vm.ToggleRulersCommand); return true;
                case Key.OemPlus or Key.Add: Run(vm.ZoomInCommand); return true;
                case Key.OemMinus or Key.Subtract: Run(vm.ZoomOutCommand); return true;
                case Key.D0 or Key.NumPad0: Run(vm.FitOnScreenCommand); return true;
                case Key.D1 or Key.NumPad1: Run(vm.ActualPixelsCommand); return true;
                case Key.OemCloseBrackets: Run(vm.BringForwardCommand); return true;
                case Key.OemOpenBrackets: Run(vm.SendBackwardCommand); return true;
                case Key.Back: Run(vm.FillBackgroundCommand); return true;
                case Key.Enter: return vm.Tools.CommitActive();
            }
            return false;
        }

        switch (key)
        {
            case Key.Escape:
                if (vm.TextEdit != null) { vm.EndTextEdit(); return true; }
                if (vm.Tools.CancelActive()) return true;
                vm.Panels.Adjustments.Cancel();
                return true;
            case Key.Enter or Key.Return:
                return vm.Tools.CommitActive();
            case Key.Delete or Key.Back when alt && key == Key.Back:
                Run(vm.FillForegroundCommand);
                return true;
            case Key.Delete or Key.Back:
                Run(vm.ClearCommand);
                return true;
            case Key.F1: Run(vm.ShortcutsCommand); return true;
            case Key.F6 when shift: Run(vm.FeatherCommand); return true;
            case Key.F6: Run(vm.ShowPanelByIdCommand, "Color"); return true;
            case Key.F7: Run(vm.ShowPanelByIdCommand, "Layers"); return true;
            case Key.Space:
                if (!_spaceDown)
                {
                    _spaceDown = true;
                    vm.ActiveDocument?.Canvas?.SetSpaceHeld(true);
                }
                return true;
            case Key.OemOpenBrackets: vm.Tools.StepBrushSize(-1); return true;
            case Key.OemCloseBrackets: vm.Tools.StepBrushSize(+1); return true;
            case Key.Left or Key.Right or Key.Up or Key.Down when vm.Tools.Current == ToolKind.Move && vm.ActiveDocument != null:
            {
                int step = shift ? 10 : 1;
                int dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
                int dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
                MoveTool.Nudge(vm.ActiveDocument.Document, dx, dy);
                return true;
            }
        }
        if (alt || shift && key != Key.M) return false;

        ToolKind[]? group = key switch
        {
            Key.V => new[] { ToolKind.Move },
            Key.M => new[] { ToolKind.RectMarquee, ToolKind.EllipseMarquee },
            Key.L => new[] { ToolKind.Lasso },
            Key.W => new[] { ToolKind.MagicWand },
            Key.C => new[] { ToolKind.Crop },
            Key.I => new[] { ToolKind.Eyedropper },
            Key.B => new[] { ToolKind.Brush },
            Key.E => new[] { ToolKind.Eraser },
            Key.S => new[] { ToolKind.CloneStamp },
            Key.G => new[] { ToolKind.Fill, ToolKind.Gradient },
            Key.T => new[] { ToolKind.Text },
            Key.U => new[] { ToolKind.Rectangle, ToolKind.Ellipse, ToolKind.Line },
            Key.H => new[] { ToolKind.Hand },
            Key.Z => new[] { ToolKind.Zoom },
            _ => null,
        };
        if (group != null)
        {
            vm.EndTextEdit();
            vm.Tools.SelectFromShortcut(group);
            vm.StatusMessage = vm.Tools.ToolHint;
            return true;
        }
        switch (key)
        {
            case Key.X: Run(vm.SwapColorsCommand); return true;
            case Key.D: Run(vm.ResetColorsCommand); return true;
        }
        return false;
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceDown)
        {
            _spaceDown = false;
            Vm?.ActiveDocument?.Canvas?.SetSpaceHeld(false);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ drag & drop, closing

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm == null) return;
        var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
        if (files is { Count: > 0 })
            await Vm.OpenPathsAsync(files.Where(f => ImageIO.IsImageFile(f) || f.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)));
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || Vm == null || StartupArgs.SelfTest) return;
        if (Vm.Documents.All(d => !d.Document.IsDirty)) return;
        e.Cancel = true;
        if (await Vm.ConfirmExitAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ------------------------------------------------------------------ IDialogService

    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" },
    };

    private static readonly FilePickerFileType ProjectFiles = new("Strata Photo project")
    {
        Patterns = new[] { "*" + ProjectFile.Extension },
    };

    private static readonly FilePickerFileType AllSupported = new("All supported files")
    {
        Patterns = ImageFiles.Patterns!.Append("*" + ProjectFile.Extension).ToArray(),
    };

    public async Task<IReadOnlyList<string>> PickFilesToOpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = true,
            FileTypeFilter = new[] { AllSupported, ImageFiles, ProjectFiles },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
    }

    public async Task<string?> PickProjectSavePathAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = suggestedName,
            DefaultExtension = ProjectFile.Extension,
            FileTypeChoices = new[] { ProjectFiles },
            ShowOverwritePrompt = true,
        });
        var path = file?.TryGetLocalPath();
        if (path != null && !path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)) path += ProjectFile.Extension;
        return path;
    }

    public Task<NewDocumentSettings?> NewDocumentAsync(SKColor background) =>
        Dialogs.NewDocumentAsync(this, Vm!.NextUntitledName(), background);

    public Task<(string Path, ExportOptions Options)?> ExportAsync(Document doc) => Dialogs.ExportAsync(this, doc);
    public Task<(int Width, int Height)?> ImageSizeAsync(Document doc) => Dialogs.ImageSizeAsync(this, doc);
    public Task<(int Width, int Height, int AnchorX, int AnchorY)?> CanvasSizeAsync(Document doc) => Dialogs.CanvasSizeAsync(this, doc);

    public Task<double?> AskNumberAsync(string title, string label, double value, double min, double max, string unit) =>
        Dialogs.AskNumberAsync(this, title, label, value, min, max, unit);

    public Task<SaveChoice> AskSaveChangesAsync(string documentName) => Dialogs.AskSaveChangesAsync(this, documentName);
    public Task<bool> RunFilterAsync(OperationPreview preview) => Dialogs.RunFilterAsync(this, preview);
    public Task ShowMessageAsync(string title, string message) => Dialogs.MessageAsync(this, title, message);
    public Task ShowShortcutsAsync() => Dialogs.ShortcutsAsync(this);
    public Task ShowAboutAsync() => Dialogs.AboutAsync(this);
    public void Exit() => Close();
}
