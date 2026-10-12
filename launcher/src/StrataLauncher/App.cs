using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Strata.Ui;

namespace StrataLauncher;

internal static class Program
{
    /// <summary>Strata.exe [file]  opens the file in the right app; --self-test [report.png].</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
        return App.ExitCode;
    }
}

/// <summary>The Strata suite launcher: one tile per app, plus "open a file in the right app".</summary>
public sealed class App : Application
{
    public static int ExitCode { get; private set; }

    public override void Initialize()
    {
        SharedTheme.Load();
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Strata/")) { Source = new Uri("avares://Strata.Ui/Styles.axaml") });
        SharedTheme.Apply(Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            int st = Array.IndexOf(args, "--self-test");
            if (st < 0 && args.FirstOrDefault(File.Exists) is { } file && FileRouter.OpenFile(file))
            {
                desktop.Shutdown();
                return;
            }
            var window = new LauncherWindow();
            desktop.MainWindow = window;
            if (st >= 0)
            {
                string report = st + 1 < args.Length ? args[st + 1] : Path.Combine(Path.GetTempPath(), "strata-launcher.png");
                window.Opened += async (_, _) =>
                {
                    await Task.Delay(600);
                    using var rtb = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
                    rtb.Render(window);
                    rtb.Save(report);
                    File.WriteAllText(Path.ChangeExtension(report, ".txt"),
                        string.Join(Environment.NewLine, Enum.GetValues<SuiteApp>().Select(a => $"{a}: {SuiteApps.Find(a)?.Program ?? "not found"}")));
                    ExitCode = Enum.GetValues<SuiteApp>().Count(SuiteApps.IsAvailable) > 0 ? 0 : 1;
                    window.Close();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class FileRouter
{
    private static readonly string[] VideoExt = { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".mp3", ".wav", ".m4a", ".strata" };
    private static readonly string[] PhotoExt = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".sphoto" };

    public static SuiteApp? AppFor(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (path.EndsWith(".strata.json", StringComparison.OrdinalIgnoreCase) || VideoExt.Contains(ext)) return SuiteApp.Video;
        if (PhotoExt.Contains(ext)) return SuiteApp.Photo;
        if (ext == ".pdf") return SuiteApp.Pdf;
        return null;
    }

    public static bool OpenFile(string path) => AppFor(path) is { } app && SuiteApps.Launch(app, new[] { path });
}

public sealed class LauncherWindow : Window
{
    private readonly TextBlock _status = new() { Classes = { "dim", "small" }, TextWrapping = TextWrapping.Wrap };

    private static readonly (SuiteApp App, string Logo, string Blurb, string Build)[] Apps =
    {
        (SuiteApp.Video, "strata-video", "Edit video: timeline, effects, transitions, keyframes, text and MP4 export.", "video\\build.bat"),
        (SuiteApp.Photo, "strata-photo", "Edit images: layers, masks, brushes, selections, adjustments and filters.", "photo\\build.bat"),
        (SuiteApp.Pdf, "strata-pdf", "Work with PDFs: merge, split, annotate, sign, fill forms, redact and compress.", "pdf\\build.bat"),
    };

    public LauncherWindow()
    {
        Title = "Strata";
        Width = 860;
        Height = 440;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoaderOpen("avares://Strata/Assets/strata-suite.ico"));
        Background = new SolidColorBrush(SharedTheme.Get("bg"));

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var a in Apps) tiles.Children.Add(Tile(a.App, a.Logo, a.Blurb, a.Build));

        var open = new Button { Content = "Open a file…", HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open in Strata", AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } p) Open(p);
        };

        Content = new DockPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                Docked(new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "Strata", FontSize = 26, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = "Video, photo and PDF tools that share one look.", Classes = { "dim" } },
                    },
                }, Dock.Top),
                Docked(new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { open, _status } }, Dock.Bottom),
                tiles,
            },
        };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
#pragma warning disable CS0618 // IDataObject API: obsolete in Avalonia 11.3 but supported
            if (e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } p) Open(p);
#pragma warning restore CS0618
        });
        _status.Text = "Tip: drop a video, image or PDF here to open it in the right app.";
    }

    private static Stream AssetLoaderOpen(string uri) => Avalonia.Platform.AssetLoader.Open(new Uri(uri));

    private static T Docked<T>(T c, Dock d) where T : Control
    {
        DockPanel.SetDock(c, d);
        return c;
    }

    private void Open(string path)
    {
        var app = FileRouter.AppFor(path);
        if (app == null) _status.Text = $"Strata doesn't know which app opens \"{Path.GetFileName(path)}\".";
        else if (!SuiteApps.Launch(app.Value, new[] { path })) _status.Text = $"{SuiteApps.DisplayName(app.Value)} isn't built yet.";
        else _status.Text = $"Opened {Path.GetFileName(path)} in {SuiteApps.DisplayName(app.Value)}.";
    }

    private Control Tile(SuiteApp app, string logo, string blurb, string build)
    {
        bool available = SuiteApps.IsAvailable(app);
        var logoPath = Path.Combine(SharedTheme.SharedFolder, "icons", "app", logo + ".png");
        var button = new Button
        {
            Content = available ? "Open" : "Not built yet",
            IsEnabled = available,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "accent" },
        };
        button.Click += (_, _) =>
        {
            _status.Text = SuiteApps.Launch(app) ? $"Starting {SuiteApps.DisplayName(app)}…" : $"Could not start {SuiteApps.DisplayName(app)}.";
        };
        var stack = new StackPanel { Spacing = 10 };
        if (File.Exists(logoPath)) stack.Children.Add(new Image { Source = new Bitmap(logoPath), Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = SuiteApps.DisplayName(app), FontSize = 17, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = blurb, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Classes = { "dim" }, Height = 54 });
        stack.Children.Add(button);
        if (!available)
            stack.Children.Add(new TextBlock { Text = $"Build it with {build}", Classes = { "dim", "small" }, HorizontalAlignment = HorizontalAlignment.Center });
        return new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(SharedTheme.Get("bgPanel")),
            BorderBrush = new SolidColorBrush(SharedTheme.Get("border")),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }
}
