using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Strata.Ui;
using StrataPdf.App.ViewModels;
using StrataPdf.Core.IO;

namespace StrataPdf.App.Views;

/// <summary>Strata PDF's modal dialogs, built in code.</summary>
public static class PdfDialogs
{
    private sealed class Frame<T> : Window
    {
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        public Frame(string title, Control content, double width = 400)
        {
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
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
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (dim) t.Classes.Add("dim");
        return t;
    }

    public static async Task<string?> AskTextAsync(Window owner, string title, string label, string initial, bool multiline)
    {
        var box = new TextBox { Text = initial, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 80 : 0 };
        var dlg = new Frame<string>(title, new StackPanel { Spacing = 6, Children = { Label(label), box } }, 420);
        dlg.AddButton("OK", () => box.Text ?? "", isDefault: !multiline);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        dlg.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return await dlg.ShowDialog<string?>(owner);
    }

    public static async Task<int?> AskNumberAsync(Window owner, string title, string label, int value, int min, int max)
    {
        var n = new NumericUpDown { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Width = 120 };
        var dlg = new Frame<int?>(title, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Label(label), n } }, 340);
        dlg.AddButton("OK", () => (int?)(int)(n.Value ?? value), isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        return await dlg.ShowDialog<int?>(owner);
    }

    /// <summary>true = save, false = discard, null = cancel.</summary>
    public static async Task<bool?> AskSaveChangesAsync(Window owner, string name)
    {
        var dlg = new Frame<string>("Strata PDF", Label($"Save changes to \"{name}\"?"), 400);
        dlg.AddButton("Save", () => "save", isDefault: true);
        dlg.AddButton("Don't Save", () => "discard");
        dlg.AddButton("Cancel", () => null, isCancel: true);
        return await dlg.ShowDialog<string?>(owner) switch { "save" => true, "discard" => false, _ => null };
    }

    public static async Task MessageAsync(Window owner, string title, string text)
    {
        var dlg = new Frame<bool>(title, Label(text), 440);
        dlg.AddButton("OK", () => true, isDefault: true);
        await dlg.ShowDialog<bool>(owner);
    }

    public static async Task<SaveOptions?> CompressAsync(Window owner, long currentSize)
    {
        var standard = new RadioButton { Content = "Standard: compress streams (lossless, text stays selectable)", IsChecked = true, GroupName = "c" };
        var strong = new RadioButton { Content = "Strong: store pages as JPEG images (smallest for scans and photos)", GroupName = "c" };
        var dpi = new ValueSlider { Label = "Resolution", Minimum = 50, Maximum = 300, Value = 120, Unit = "dpi" };
        var quality = new ValueSlider { Label = "JPEG quality", Minimum = 20, Maximum = 95, Value = 70, Unit = "%" };
        void Sync() => dpi.IsEnabled = quality.IsEnabled = strong.IsChecked == true;
        strong.IsCheckedChanged += (_, _) => Sync();
        Sync();
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Label(currentSize > 0 ? $"Current file: {MainViewModel.FormatSize(currentSize)}. The compressed version is saved as a copy." : "The compressed version is saved as a copy."),
                standard, strong, dpi, quality,
                Label("Strong compression turns text into pixels: it can no longer be selected or searched.", dim: true),
            },
        };
        var dlg = new Frame<SaveOptions>("Save Compressed Copy", content, 480);
        dlg.AddButton("Continue…", () => strong.IsChecked == true
            ? new SaveOptions(CompressionLevel.Strong, (int)dpi.Value, (int)quality.Value)
            : new SaveOptions(), isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        return await dlg.ShowDialog<SaveOptions?>(owner);
    }

    public static async Task<byte[]?> SignatureAsync(Window owner)
    {
        var pad = new SignaturePad { Height = 180 };
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) => pad.Clear();
        var load = new Button { Content = "Load image…" };
        byte[]? loaded = null;
        var info = Label("Draw your signature with the mouse or a pen, or load an image (PNG/JPG).", dim: true);
        load.Click += async (_, _) =>
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Signature image",
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            using var bmp = SKBitmap.Decode(path);
            if (bmp == null) return;
            using var img = SKImage.FromBitmap(bmp);
            loaded = img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
            info.Text = $"Using {Path.GetFileName(path)}.";
            pad.Clear();
        };
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                info,
                new Border { Background = Brushes.White, CornerRadius = new CornerRadius(4), Child = pad },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { clear, load } },
            },
        };
        var dlg = new Frame<byte[]>("Signature", content, 520);
        dlg.AddButton("Use Signature", () => pad.HasInk ? pad.ToPng() : loaded, isDefault: true);
        dlg.AddButton("Cancel", () => null, isCancel: true);
        return await dlg.ShowDialog<byte[]?>(owner);
    }

    private static readonly (string Keys, string Action)[] ShortcutList =
    {
        ("V  H  M  P  T", "Select, Hand, Highlight, Pen, Text box"),
        ("R  E  A  L  N  S  X  Z", "Rectangle, Ellipse, Arrow, Line, Sticky note, Signature, Redact, Zoom"),
        ("Space", "Temporary hand (drag to scroll)"),
        ("Delete", "Delete the selected annotation"),
        ("Ctrl+Z  Ctrl+Shift+Z", "Undo, redo"),
        ("Ctrl+R  Ctrl+Shift+R", "Rotate page(s) right / left"),
        ("Ctrl+Up  Ctrl+Down  Ctrl+Delete", "Move page(s) up / down, delete page(s)"),
        ("PageUp  PageDown  Home  End", "Previous / next / first / last page"),
        ("Ctrl+Plus  Ctrl+Minus  Ctrl+1  Ctrl+2  Ctrl+0", "Zoom in / out, 100%, fit width, fit page"),
        ("Ctrl+O  Ctrl+I  Ctrl+S  Ctrl+Shift+S", "Open, insert/merge PDF, save, save as"),
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
        var dlg = new Frame<bool>("Keyboard Shortcuts", grid, 680);
        dlg.AddButton("Close", () => true, isDefault: true);
        await dlg.ShowDialog<bool>(owner);
    }

    public static async Task AboutAsync(Window owner)
    {
        var logo = Path.Combine(SharedTheme.SharedFolder, "icons", "app", "strata-pdf.png");
        var content = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        if (File.Exists(logo)) content.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(logo), Width = 72, Height = 72 });
        content.Children.Add(new TextBlock { Text = "Strata PDF", FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = "PDF toolkit: view, merge, split, reorder, annotate, sign, fill forms, redact, stamp and compress.\n" +
                   "Part of Project Strata. Built with .NET, Avalonia, PDFium (PDFiumCore) and PDFsharp.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Classes = { "dim" },
        });
        var dlg = new Frame<bool>("About Strata PDF", content, 400);
        dlg.AddButton("Close", () => true, isDefault: true);
        await dlg.ShowDialog<bool>(owner);
    }
}

/// <summary>Draw a signature with the mouse or a pen; exported as a trimmed transparent PNG.</summary>
public sealed class SignaturePad : Control
{
    private readonly List<List<Point>> _strokes = new();
    private List<Point>? _current;

    public SignaturePad()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        ClipToBounds = true;
    }

    public bool HasInk => _strokes.Any(s => s.Count > 1);

    public void Clear()
    {
        _strokes.Clear();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        var guide = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 205)), 1);
        ctx.DrawLine(guide, new Point(20, Bounds.Height - 40), new Point(Bounds.Width - 20, Bounds.Height - 40));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(20, 30, 90)), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (var s in _strokes)
            for (int i = 1; i < s.Count; i++) ctx.DrawLine(pen, s[i - 1], s[i]);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _current = new List<Point> { e.GetPosition(this) };
        _strokes.Add(_current);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_current == null) return;
        foreach (var p in e.GetIntermediatePoints(this)) _current.Add(p.Position);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _current = null;
        e.Pointer.Capture(null);
    }

    /// <summary>Adds a stroke programmatically (self-test).</summary>
    public void AddStroke(IEnumerable<Point> points)
    {
        _strokes.Add(points.ToList());
        InvalidateVisual();
    }

    public byte[] ToPng()
    {
        var all = _strokes.SelectMany(s => s).ToList();
        const float scale = 3, pad = 6;
        float x0 = (float)all.Min(p => p.X) - pad, y0 = (float)all.Min(p => p.Y) - pad;
        float x1 = (float)all.Max(p => p.X) + pad, y1 = (float)all.Max(p => p.Y) + pad;
        int w = (int)Math.Ceiling((x1 - x0) * scale), h = (int)Math.Ceiling((y1 - y0) * scale);
        using var bmp = new SKBitmap(new SKImageInfo(Math.Max(1, w), Math.Max(1, h), SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = new SKColor(20, 30, 90), StrokeWidth = 2.4f * scale, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
        {
            c.Clear(SKColors.Transparent);
            c.Scale(scale);
            c.Translate(-x0, -y0);
            foreach (var s in _strokes.Where(s => s.Count > 1))
            {
                using var path = new SKPath();
                path.MoveTo((float)s[0].X, (float)s[0].Y);
                foreach (var p in s.Skip(1)) path.LineTo((float)p.X, (float)p.Y);
                c.DrawPath(path, paint);
            }
        }
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
