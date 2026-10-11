using Avalonia;

namespace StrataPhoto.App;

internal static class Program
{
    /// <summary>
    /// StrataPhoto.exe [image or .sphoto files...]
    /// StrataPhoto.exe --self-test [report.png]   start, exercise the editor, save a screenshot, exit
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };
        StartupArgs.Parse(args);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return 1;
        }
        return StartupArgs.ExitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // GPU rendering through ANGLE (Direct3D 11), with a software fallback.
                RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software },
            })
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 512L * 1024 * 1024 })
            .LogToTrace();
}

internal static class StartupArgs
{
    public static List<string> Files { get; } = new();
    public static bool SelfTest { get; private set; }
    public static string? SelfTestReport { get; private set; }
    public static int ExitCode { get; set; }

    public static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--self-test")
            {
                SelfTest = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) SelfTestReport = args[++i];
            }
            else if (File.Exists(args[i]))
            {
                Files.Add(Path.GetFullPath(args[i]));
            }
        }
    }
}

internal static class CrashLog
{
    public static string Folder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strata Photo");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string FilePath => Path.Combine(Folder, "strata-photo-errors.log");

    public static void Write(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            File.AppendAllText(FilePath, $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch
        {
            // nothing sensible left to do
        }
    }
}
