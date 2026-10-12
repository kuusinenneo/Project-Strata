using PdfSharp.Fonts;

namespace StrataPdf.Core.IO;

/// <summary>
/// PDFsharp must be told where fonts come from before the first font is used; call
/// <see cref="Ensure"/> at startup (it is also called by the saver).
/// </summary>
public static class PdfFonts
{
    private static readonly object Lock = new();
    private static bool _done;

    public static void Ensure()
    {
        lock (Lock)
        {
            if (_done) return;
            _done = true;
            // Note: PDFsharp locks this setting once it has been read, so it is set without checking it.
            if (OperatingSystem.IsWindows() && GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }
}
