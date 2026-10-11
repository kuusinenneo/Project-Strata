using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StrataPhoto.App.ViewModels.Panels;

public sealed partial class HistoryItem : ObservableObject
{
    public HistoryItem(int position, string name, bool isBase)
    {
        Position = position;
        Name = name;
        IsBase = isBase;
    }

    /// <summary>History position after this step (0 = the initial state).</summary>
    public int Position { get; }
    public string Name { get; }
    public bool IsBase { get; }
    public string Icon => IsBase ? "image" : "history";

    [ObservableProperty] private bool _isUndone;
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>The History panel: every step of the active document; click one to go back to it.</summary>
public sealed partial class HistoryPanelViewModel : PanelBase
{
    private DocumentViewModel? _doc;
    private bool _syncing;

    public HistoryPanelViewModel(MainViewModel main) : base(main, "History", "History", "history") { }

    public ObservableCollection<HistoryItem> Items { get; } = new();

    [ObservableProperty] private HistoryItem? _selected;

    partial void OnSelectedChanged(HistoryItem? value)
    {
        if (_syncing || value == null || _doc == null) return;
        Main.Tools.CancelActive();
        Main.Panels.Adjustments.Cancel();
        _doc.Document.History.GoTo(value.Position);
    }

    [RelayCommand] private void Undo() => Main.UndoCommand.Execute(null);
    [RelayCommand] private void Redo() => Main.RedoCommand.Execute(null);

    public override void OnDocumentChanged(DocumentViewModel? doc)
    {
        if (_doc != doc)
        {
            if (_doc != null) _doc.Document.History.Changed -= Rebuild;
            _doc = doc;
            if (_doc != null) _doc.Document.History.Changed += Rebuild;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        _syncing = true;
        try
        {
            Items.Clear();
            if (_doc == null) return;
            var h = _doc.Document.History;
            Items.Add(new HistoryItem(0, h.BaseName, true));
            for (int i = 0; i < h.Entries.Count; i++)
                Items.Add(new HistoryItem(i + 1, h.Entries[i].Name, false));
            foreach (var item in Items)
            {
                item.IsUndone = item.Position > h.Position;
                item.IsCurrent = item.Position == h.Position;
            }
            Selected = Items.FirstOrDefault(i => i.IsCurrent);
        }
        finally
        {
            _syncing = false;
        }
    }
}
