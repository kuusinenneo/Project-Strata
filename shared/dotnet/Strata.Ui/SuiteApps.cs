using System.Diagnostics;

namespace Strata.Ui;

public enum SuiteApp { Video, Photo, Pdf }

/// <summary>
/// Finds and starts the other Strata apps. Looked up, in order: an environment variable
/// (STRATA_VIDEO_EXE / STRATA_PHOTO_EXE / STRATA_PDF_EXE), a sibling folder in a combined suite
/// folder (…\StrataPhoto\StrataPhoto.exe), each app's build output in the repository, and finally
/// the development builds (or Python source for Strata Video).
/// </summary>
public static class SuiteApps
{
    public static string DisplayName(SuiteApp app) => app switch
    {
        SuiteApp.Video => "Strata Video",
        SuiteApp.Photo => "Strata Photo",
        _ => "Strata PDF",
    };

    private static (string Env, string[] Relative) Candidates(SuiteApp app) => app switch
    {
        SuiteApp.Video => ("STRATA_VIDEO_EXE", new[]
        {
            Path.Combine("StrataVideo", "StrataVideo.exe"),
            Path.Combine("video", "dist", "StrataVideo", "StrataVideo.exe"),
        }),
        SuiteApp.Photo => ("STRATA_PHOTO_EXE", new[]
        {
            Path.Combine("StrataPhoto", "StrataPhoto.exe"),
            Path.Combine("photo", "publish", "StrataPhoto", "StrataPhoto.exe"),
            Path.Combine("photo", "src", "StrataPhoto.App", "bin", "Release", "net10.0", "StrataPhoto.exe"),
            Path.Combine("photo", "src", "StrataPhoto.App", "bin", "Debug", "net10.0", "StrataPhoto.exe"),
        }),
        _ => ("STRATA_PDF_EXE", new[]
        {
            Path.Combine("StrataPDF", "StrataPDF.exe"),
            Path.Combine("pdf", "publish", "StrataPDF", "StrataPDF.exe"),
            Path.Combine("pdf", "src", "StrataPdf.App", "bin", "Release", "net10.0", "StrataPDF.exe"),
            Path.Combine("pdf", "src", "StrataPdf.App", "bin", "Debug", "net10.0", "StrataPDF.exe"),
        }),
    };

    /// <summary>How to start an app: (program, arguments before the files), or null if not found.</summary>
    public static (string Program, string[] Args)? Find(SuiteApp app)
    {
        var (env, relative) = Candidates(app);
        var fromEnv = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return (fromEnv, Array.Empty<string>());
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var rel in relative)
            {
                var candidate = Path.Combine(dir.FullName, rel);
                if (File.Exists(candidate)) return (candidate, Array.Empty<string>());
            }
            if (app == SuiteApp.Video)
            {
                // From source: video\.venv\Scripts\pythonw.exe video\main.py
                var py = Path.Combine(dir.FullName, "video", ".venv", "Scripts", "pythonw.exe");
                var main = Path.Combine(dir.FullName, "video", "main.py");
                if (File.Exists(py) && File.Exists(main)) return (py, new[] { main });
            }
        }
        return null;
    }

    public static bool IsAvailable(SuiteApp app) => Find(app) != null;

    /// <summary>Starts the app with these files. False if it couldn't be found or started.</summary>
    public static bool Launch(SuiteApp app, IEnumerable<string>? files = null)
    {
        var found = Find(app);
        if (found == null) return false;
        var psi = new ProcessStartInfo(found.Value.Program)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(found.Value.Args.FirstOrDefault() ?? found.Value.Program)!,
        };
        foreach (var a in found.Value.Args) psi.ArgumentList.Add(a);
        foreach (var f in files ?? Enumerable.Empty<string>()) psi.ArgumentList.Add(f);
        try
        {
            return Process.Start(psi) != null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
