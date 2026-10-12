using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using StrataPhoto.App.Controls;
using Strata.Ui;
using StrataPhoto.App.ViewModels;
using StrataPhoto.Core.IO;
using StrataPhoto.Core.Model;

namespace StrataPhoto.App.Views;

/// <summary>The app's modal dialogs, built in code on a common frame.</summary>
public static class Dialogs
{
    /// <summary>A dialog window with a content area and a row of buttons.</summary>
    private sealed class Frame<T> : Window
    {
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        public Frame(string title, Control content, double width = 380)
        {
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Icon = null;
            var root = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(_buttons, Avalonia.Controls.Dock.Bottom);
            _buttons.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(_buttons);
            root.Children.Add(content);
            Content = root;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { Close(default(T)); e.Handled = true; }
            };
        }

        public Button AddButton(string text, Func<T?> result, bool isDefault = false, bool isCancel = false)
        {
            var b = new Button { Content = text, MinWidth = 84, IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault) b.Classes.Add("accent");
            b.Click += (_, _) => Close(result());
            _buttons.Children.Add(b);
            return b;
        }
    }

    private static TextBlock Label(string text, bool dim = false)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        if (dim) t.Classes.Add("dim");
        return t;
    }

    private static NumericUpDown Number(double value, double min, double max, double step = 1, string format = "0") => new()
    {
        Value = (decimal)value,
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Increment = (decimal)step,
        FormatString = format,
        Width = 110,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Grid Form(params (string label, Control control)[] rows)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*"), RowSpacing = 8 };
        for (int i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Label(rows[i].label);
            Grid.SetRow(l, i);
            Grid.SetRow(rows[i].control, i);
            Grid.SetColumn(rows[i].control, 2);
            g.Children.Add(l);
            g.Children.Add(rows[i].control);
        }
        return g;
    }

    // ------------------------------------------------------------------ new document

    private static readonly (string Name, int W, int H)[] Presets =
    {
        ("Custom", 0, 0), ("1920 × 1080 (Full HD)", 1920, 1080), ("3840 × 2160 (4K UHD)", 3840, 2160),
        ("1080 × 1080 (Square post)", 1080, 1080), ("1080 × 1920 (Story)", 1080, 1920), ("1280 × 720 (HD)", 1280, 720),
        ("2480 × 3508 (A4, 300 ppi)", 2480, 3508), ("4000 × 4000", 4000, 4000), ("800 × 600", 800, 600),
    };

    public static async Task<NewDocumentSettings?> NewDocumentAsync(Window owner, string name, SKColor background)
    {
        var nameBox = new TextBox { Text = name };
        var w = Number(1920, 1, 30000);
        var h = Number(1080, 1, 30000);
        var preset = new ComboBox { ItemsSource = Presets.Select(p => p.Name).ToList(), SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var bg = new ComboBox { ItemsSource = new[] { "White", "Black", "Background colour", "Transparent" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        bool syncing = false;
        preset.SelectionChanged += (_, _) =>
        {
            var p = Presets[Math.Max(0, preset.SelectedIndex)];
            if (p.W == 0) return;
            syncing = true;
            w.Value = p.W;
            h.Value = p.H;
            syncing = false;
        };
        void Custom(object? s, NumericUpDownValueChangedEventArgs e)
        {
            if (!syncing) preset.SelectedIndex = 0;
        }
        w.ValueChanged += Custom;
        h.ValueChanged += Custom;
        var form = Form(("Name", nameBox), ("Preset", preset), ("Width (px)", w), ("Height (px)", h), ("Background", bg));
        var dlg = new Frame<NewDocumentSettings>("New Document", form);
        dlg.AddButton("Create", () => new NewDocumentSettings(
            string.IsNullOrWhiteSpace(nameBox.Text) ? name : nameBox.Text.Trim(), (int)(w.Value ?? 1), (int)(h.Value ?? 1),
            bg.SelectedIndex switch { 0 => SKColors.White, 1 => SKColors.Black, 2 => background, _ => null }), isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        dlg.Opened += (_, _) => nameBox.Focus();
        return await dlg.ShowDialog<NewDocumentSettings?>(owner);
    }

    // ------------------------------------------------------------------ export

    public static async Task<(string Path, ExportOptions Options)?> ExportAsync(Window owner, Document doc)
    {
        var format = new ComboBox { ItemsSource = new[] { "PNG", "JPEG", "WebP" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var quality = new ValueSlider { Minimum = 1, Maximum = 100, Value = 90, Unit = "%" };
        var w = Number(doc.Width, 1, 30000);
        var h = Number(doc.Height, 1, 30000);
        var keep = new CheckBox { Content = "Keep proportions", IsChecked = true };
        var estimate = Label("", dim: true);
        bool syncing = false;
        ExportOptions Options() => new(
            format.SelectedIndex switch { 1 => ExportFormat.Jpeg, 2 => ExportFormat.Webp, _ => ExportFormat.Png },
            (int)quality.Value, (int)(w.Value ?? doc.Width), (int)(h.Value ?? doc.Height));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        int generation = 0;
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            int gen = ++generation;
            var opts = Options();
            estimate.Text = "Estimating size…";
            long bytes = await Task.Run(() => ImageIO.EstimateSize(doc, opts));
            if (gen == generation) estimate.Text = $"Estimated file size: {FormatBytes(bytes)}";
        };
        void Changed()
        {
            quality.IsEnabled = format.SelectedIndex != 0;
            timer.Stop();
            timer.Start();
        }
        format.SelectionChanged += (_, _) => Changed();
        quality.PropertyChanged += (_, e) => { if (e.Property == ValueSlider.ValueProperty) Changed(); };
        w.ValueChanged += (_, _) =>
        {
            if (!syncing && keep.IsChecked == true && w.Value is { } v)
            {
                syncing = true;
                h.Value = Math.Max(1, Math.Round(v * doc.Height / doc.Width));
                syncing = false;
            }
            Changed();
        };
        h.ValueChanged += (_, _) =>
        {
            if (!syncing && keep.IsChecked == true && h.Value is { } v)
            {
                syncing = true;
                w.Value = Math.Max(1, Math.Round(v * doc.Width / doc.Height));
                syncing = false;
            }
            Changed();
        };
        var form = Form(("Format", format), ("Quality", quality), ("Width (px)", w), ("Height (px)", h), ("", keep));
        var content = new StackPanel { Spacing = 10, Children = { form, estimate, Label("JPEG has no transparency; transparent areas become white.", dim: true) } };
        var dlg = new Frame<ExportOptions>("Export As", content, 420);
        dlg.AddButton("Export…", Options, isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        Changed();
        var opts = await dlg.ShowDialog<ExportOptions?>(owner);
        timer.Stop();
        if (opts == null) return null;
        var ext = opts.Format switch { ExportFormat.Jpeg => ".jpg", ExportFormat.Webp => ".webp", _ => ".png" };
        var type = new FilePickerFileType(opts.Format.ToString().ToUpperInvariant()) { Patterns = new[] { "*" + ext } };
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export As",
            SuggestedFileName = Path.GetFileNameWithoutExtension(doc.Name) + ext,
            DefaultExtension = ext,
            FileTypeChoices = new[] { type },
            ShowOverwritePrompt = true,
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return null;
        if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
        return (path, opts);
    }

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        _ => $"{b / 1024.0 / 1024.0:0.##} MB",
    };

    // ------------------------------------------------------------------ image / canvas size

    public static async Task<(int, int)?> ImageSizeAsync(Window owner, Document doc)
    {
        var w = Number(doc.Width, 1, 30000);
        var h = Number(doc.Height, 1, 30000);
        var pct = Number(100, 1, 1000, 1, "0.#");
        var keep = new CheckBox { Content = "Keep proportions", IsChecked = true };
        bool syncing = false;
        void FromW()
        {
            if (syncing || w.Value is not { } v) return;
            syncing = true;
            if (keep.IsChecked == true) h.Value = Math.Max(1, Math.Round(v * doc.Height / doc.Width));
            pct.Value = Math.Round(v * 100 / doc.Width, 1);
            syncing = false;
        }
        void FromH()
        {
            if (syncing || h.Value is not { } v) return;
            syncing = true;
            if (keep.IsChecked == true) w.Value = Math.Max(1, Math.Round(v * doc.Width / doc.Height));
            syncing = false;
        }
        w.ValueChanged += (_, _) => FromW();
        h.ValueChanged += (_, _) => FromH();
        pct.ValueChanged += (_, _) =>
        {
            if (syncing || pct.Value is not { } p) return;
            syncing = true;
            w.Value = Math.Max(1, Math.Round(doc.Width * p / 100));
            h.Value = Math.Max(1, Math.Round(doc.Height * p / 100));
            syncing = false;
        };
        var form = Form(("Width (px)", w), ("Height (px)", h), ("Scale (%)", pct), ("", keep));
        var info = Label($"Current size: {doc.Width} × {doc.Height} px. All layers are resampled (bicubic).", dim: true);
        var dlg = new Frame<(int, int)?>("Image Size", new StackPanel { Spacing = 10, Children = { form, info } });
        dlg.AddButton("OK", () => ((int)(w.Value ?? doc.Width), (int)(h.Value ?? doc.Height)), isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        return await dlg.ShowDialog<(int, int)?>(owner);
    }

    public static async Task<(int, int, int, int)?> CanvasSizeAsync(Window owner, Document doc)
    {
        var w = Number(doc.Width, 1, 30000);
        var h = Number(doc.Height, 1, 30000);
        int ax = 1, ay = 1;
        var anchor = new UniformGrid { Rows = 3, Columns = 3, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left };
        var buttons = new List<ToggleButton>();
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            int cx = x, cy = y;
            var b = new ToggleButton { Margin = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, IsChecked = x == 1 && y == 1 };
            b.Click += (_, _) =>
            {
                ax = cx;
                ay = cy;
                foreach (var o in buttons) o.IsChecked = o == b;
            };
            buttons.Add(b);
            anchor.Children.Add(b);
        }
        var form = Form(("Width (px)", w), ("Height (px)", h), ("Anchor", anchor));
        var info = Label("New areas are transparent (the Background layer is filled with the background colour).", dim: true);
        var dlg = new Frame<(int, int, int, int)?>("Canvas Size", new StackPanel { Spacing = 10, Children = { form, info } });
        dlg.AddButton("OK", () => ((int)(w.Value ?? doc.Width), (int)(h.Value ?? doc.Height), ax, ay), isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        return await dlg.ShowDialog<(int, int, int, int)?>(owner);
    }

    // ------------------------------------------------------------------ simple prompts

    public static async Task<double?> AskNumberAsync(Window owner, string title, string label, double value, double min, double max, string unit)
    {
        var n = Number(value, min, max, 1, "0.##");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Label(label), n, Label(unit, dim: true) } };
        var dlg = new Frame<double?>(title, row, 340);
        dlg.AddButton("OK", () => (double?)(double)(n.Value ?? (decimal)value), isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        dlg.Opened += (_, _) => n.Focus();
        return await dlg.ShowDialog<double?>(owner);
    }

    public static async Task<SaveChoice> AskSaveChangesAsync(Window owner, string name)
    {
        var dlg = new Frame<SaveChoice?>("Strata Photo", Label($"Save changes to \"{name}\" before closing?"), 400);
        dlg.AddButton("Save", () => SaveChoice.Save, isDefault: true);
        dlg.AddButton("Don't Save", () => SaveChoice.Discard);
        dlg.AddButton("Cancel", () => SaveChoice.Cancel, isCancel: true);
        return await dlg.ShowDialog<SaveChoice?>(owner) ?? SaveChoice.Cancel;
    }

    public static async Task MessageAsync(Window owner, string title, string message)
    {
        var dlg = new Frame<bool>(title, Label(message), 420);
        dlg.AddButton("OK", () => true, isDefault: true);
        await dlg.ShowDialog<bool>(owner);
    }

    // ------------------------------------------------------------------ filters

    public static async Task<bool> RunFilterAsync(Window owner, OperationPreview preview)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var p in preview.Params)
            panel.Children.Add(ParamEditor(p));
        var previewBox = new CheckBox { Content = "Preview", IsChecked = true };
        previewBox.IsCheckedChanged += (_, _) => preview.PreviewEnabled = previewBox.IsChecked == true;
        panel.Children.Add(previewBox);
        var dlg = new Frame<bool>(preview.Title, panel, 420);
        dlg.AddButton("OK", () => true, isDefault: true);
        dlg.AddButton("Cancel", () => false, isCancel: true);
        dlg.Position = new PixelPoint(owner.Position.X + 80, owner.Position.Y + 120);
        dlg.WindowStartupLocation = WindowStartupLocation.Manual;
        return await dlg.ShowDialog<bool>(owner);
    }

    /// <summary>Slider row or checkbox for one adjustment/filter parameter.</summary>
    public static Control ParamEditor(ParamViewModel p)
    {
        if (p.IsBool)
        {
            var cb = new CheckBox { Content = p.Label, IsChecked = p.BoolValue };
            cb.IsCheckedChanged += (_, _) => p.BoolValue = cb.IsChecked == true;
            return cb;
        }
        var label = Label(p.Label);
        var slider = new ValueSlider { Minimum = p.Min, Maximum = p.Max, Step = p.Step, Format = p.Format, Unit = p.Unit, Value = p.Value };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == ValueSlider.ValueProperty) p.Value = slider.Value;
        };
        p.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ParamViewModel.Value) && Math.Abs(slider.Value - p.Value) > 1e-9) slider.Value = p.Value;
        };
        return new StackPanel { Spacing = 2, Children = { label, slider } };
    }

    // ------------------------------------------------------------------ help

    private static readonly (string Keys, string Action)[] ShortcutList =
    {
        ("V  M  L  W  C  I", "Move, Marquee (again: Ellipse), Lasso, Magic Wand, Crop, Eyedropper"),
        ("B  E  S  G  T  U", "Brush, Eraser, Clone Stamp, Fill (again: Gradient), Text, Shapes (cycles)"),
        ("H  Z  Space", "Hand, Zoom, temporary Hand while held"),
        ("[  ]", "Brush size smaller / larger"),
        ("X  D", "Swap colours, default black/white"),
        ("Ctrl+Z / Ctrl+Shift+Z", "Undo / Redo"),
        ("Ctrl+J  Ctrl+Shift+N", "Duplicate layer, new layer"),
        ("Ctrl+G  Ctrl+Shift+G  Ctrl+E", "Group, ungroup, merge down"),
        ("Ctrl+[  Ctrl+]", "Move layer down / up"),
        ("Ctrl+A  Ctrl+D  Ctrl+Shift+I", "Select all, deselect, invert selection"),
        ("Shift+F6", "Feather selection"),
        ("Ctrl+X  Ctrl+C  Ctrl+Shift+C  Ctrl+V", "Cut, copy, copy merged, paste as new layer"),
        ("Delete  Alt+Backspace  Ctrl+Backspace", "Clear, fill with foreground, fill with background"),
        ("Ctrl+T  Enter  Esc", "Free transform, apply, cancel"),
        ("Ctrl+L  Ctrl+M  Ctrl+U  Ctrl+B  Ctrl+I", "Levels, Curves, Hue/Saturation, Color Balance, Invert"),
        ("Ctrl+Plus  Ctrl+Minus  Ctrl+0  Ctrl+1", "Zoom in, zoom out, fit on screen, 100%"),
        ("Ctrl/Alt+wheel  wheel  Shift+wheel", "Zoom at cursor, scroll, scroll sideways"),
        ("Ctrl+N  Ctrl+O  Ctrl+S  Ctrl+Shift+S  Ctrl+Shift+E", "New, open, save, save as, export"),
        ("Ctrl+R  F1", "Toggle rulers, this list"),
    };

    public static async Task ShortcutsAsync(Window owner)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*"), RowSpacing = 6 };
        for (int i = 0; i < ShortcutList.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = ShortcutList[i].Keys, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), Foreground = new SolidColorBrush(SharedTheme.Get("accentSoft")) };
            var a = Label(ShortcutList[i].Action);
            Grid.SetRow(k, i);
            Grid.SetRow(a, i);
            Grid.SetColumn(a, 2);
            grid.Children.Add(k);
            grid.Children.Add(a);
        }
        var dlg = new Frame<bool>("Keyboard Shortcuts", grid, 760);
        dlg.AddButton("Close", () => true, isDefault: true);
        await dlg.ShowDialog<bool>(owner);
    }

    public static async Task AboutAsync(Window owner)
    {
        var logoPath = Path.Combine(SharedTheme.SharedFolder, "icons", "app", "strata-photo.png");
        var content = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        if (File.Exists(logoPath))
            content.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(logoPath), Width = 72, Height = 72 });
        content.Children.Add(new TextBlock { Text = "Strata Photo", FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        var version = typeof(Dialogs).Assembly.GetName().Version;
        content.Children.Add(new TextBlock { Text = $"Version {version?.ToString(3)}", HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } });
        content.Children.Add(new TextBlock
        {
            Text = "Layered image editor. Part of Project Strata, sharing its design system with Strata Video.\nBuilt with .NET, Avalonia, SkiaSharp and Dock.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Classes = { "dim" },
        });
        var dlg = new Frame<bool>("About Strata Photo", content, 380);
        dlg.AddButton("Close", () => true, isDefault: true);
        await dlg.ShowDialog<bool>(owner);
    }
}
