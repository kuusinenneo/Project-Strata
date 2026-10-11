using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrataPhoto.Core.Operations;

namespace StrataPhoto.App.ViewModels.Panels;

public sealed record AdjustmentEntry(string Id, string Name, string Icon, Func<ImageOperation> Create);

/// <summary>
/// Adjustments panel: pick an adjustment, tune it with a live preview on the active layer, then
/// Apply (one undo step) or Cancel.
/// </summary>
public sealed partial class AdjustmentsPanelViewModel : PanelBase
{
    public AdjustmentsPanelViewModel(MainViewModel main) : base(main, "Adjustments", "Adjustments", "adjust")
    {
        Entries = OperationCatalog.Adjustments.Select(f =>
        {
            var op = f();
            string icon = op.Id switch
            {
                "brightness-contrast" => "adjust",
                "levels" or "curves" => "properties",
                "exposure" => "fx",
                "hue-saturation" or "color-balance" => "color",
                "black-white" => "mask",
                _ => "swap",
            };
            return new AdjustmentEntry(op.Id, op.Name, icon, f);
        }).ToList();
    }

    public IReadOnlyList<AdjustmentEntry> Entries { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsPicking), nameof(IsCurves))]
    private OperationPreview? _session;

    [ObservableProperty] private string _message = "Choose an adjustment to apply to the active layer.";

    public bool IsEditing => Session != null;
    public bool IsPicking => Session == null;
    public bool IsCurves => Session?.Operation is Curves;

    [RelayCommand]
    public void Begin(string id)
    {
        Cancel();
        var doc = Main.ActiveDocument;
        if (doc == null)
        {
            Message = "Open or create an image first.";
            return;
        }
        var entry = Entries.First(e => e.Id == id);
        Main.Tools.CancelActive();
        var session = OperationPreview.Start(doc, entry.Create(), out var reason);
        if (session == null)
        {
            Message = reason ?? "";
            Main.StatusMessage = reason ?? "";
            return;
        }
        Session = session;
        Main.ShowPanel(this);
        if (!session.HasParams && session.Operation is not Curves)
        {
            Apply(); // Invert: nothing to tune
            return;
        }
        session.Schedule();
    }

    [RelayCommand]
    private void Apply()
    {
        if (Session == null) return;
        var s = Session;
        Session = null;
        if (s.Commit()) Message = $"Applied {s.Title}.";
    }

    [RelayCommand]
    public void Cancel()
    {
        if (Session == null) return;
        Session.Cancel();
        Session = null;
    }

    [RelayCommand]
    private void Reset() => Session?.ResetParams();

    public override void OnDocumentChanged(DocumentViewModel? doc)
    {
        if (Session != null && Session.Document != doc) Cancel();
    }
}
