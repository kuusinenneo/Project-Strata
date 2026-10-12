using Avalonia;

namespace StrataPdf.App;

internal static class Program
{
    /// <summary>
    /// StrataPDF.exe [file.pdf ...]          open (several files are merged)
    /// StrataPDF.exe --self-test [report.png] exercise the app, save a screenshot, exit
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
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software } })
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
    public static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strata PDF");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "strata-pdf-errors.log");
        }
    }

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
