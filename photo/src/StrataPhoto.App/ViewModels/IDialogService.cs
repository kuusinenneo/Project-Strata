using SkiaSharp;
using StrataPhoto.Core.IO;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Operations;

namespace StrataPhoto.App.ViewModels;

public sealed record NewDocumentSettings(string Name, int Width, int Height, SKColor? Background);

public enum SaveChoice { Save, Discard, Cancel }

/// <summary>Dialogs and file pickers, implemented by the main window.</summary>
public interface IDialogService
{
    Task<IReadOnlyList<string>> PickFilesToOpenAsync();
    Task<string?> PickProjectSavePathAsync(string suggestedName);
    Task<NewDocumentSettings?> NewDocumentAsync(SKColor background);
    Task<(string Path, ExportOptions Options)?> ExportAsync(Document doc);
    Task<(int Width, int Height)?> ImageSizeAsync(Document doc);
    Task<(int Width, int Height, int AnchorX, int AnchorY)?> CanvasSizeAsync(Document doc);
    Task<double?> AskNumberAsync(string title, string label, double value, double min, double max, string unit);
    Task<SaveChoice> AskSaveChangesAsync(string documentName);
    Task<bool> RunFilterAsync(OperationPreview preview);
    Task ShowMessageAsync(string title, string message);
    Task ShowShortcutsAsync();
    Task ShowAboutAsync();
    void Exit();
}
