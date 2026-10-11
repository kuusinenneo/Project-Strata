using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using StrataPhoto.App.ViewModels.Panels;

namespace StrataPhoto.App.ViewModels;

/// <summary>
/// Builds the Photoshop-style layout: documents in the centre, panel stacks on the right.
/// Calling <see cref="CreateLayout"/> again gives the default layout back ("Reset Layout").
/// </summary>
public sealed class DockFactory : Factory
{
    private readonly MainViewModel _main;

    public DockFactory(MainViewModel main)
    {
        _main = main;
        HideToolsOnClose = false;
    }

    public IDocumentDock? Documents { get; private set; }
    public IRootDock? Root { get; private set; }

    public override IRootDock CreateLayout()
    {
        var p = _main.Panels;
        foreach (var panel in p.All) panel.Owner = null;

        var documents = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            Proportion = double.NaN,
            IsCollapsable = false,
            CanCreateDocument = false,
            VisibleDockables = CreateList<IDockable>(),
        };
        foreach (var d in _main.Documents) documents.VisibleDockables!.Add(d);
        documents.ActiveDockable = _main.ActiveDocument ?? _main.Documents.LastOrDefault();

        ToolDock Stack(string id, double proportion, params Tool[] tools) => new()
        {
            Id = id,
            Proportion = proportion,
            Alignment = Alignment.Right,
            VisibleDockables = CreateList<IDockable>(tools),
            ActiveDockable = tools[0],
        };

        var right = new ProportionalDock
        {
            Id = "RightPanels",
            Orientation = Orientation.Vertical,
            Proportion = 0.24,
            VisibleDockables = CreateList<IDockable>(
                Stack("ColorStack", 0.30, p.Color, p.Swatches),
                new ProportionalDockSplitter(),
                Stack("PropertiesStack", 0.32, p.Properties, p.Adjustments),
                new ProportionalDockSplitter(),
                Stack("LayersStack", 0.38, p.Layers, p.History)),
        };

        var main = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(documents, new ProportionalDockSplitter(), right),
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;

        Documents = documents;
        Root = root;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>();
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };
        DockableLocator = new Dictionary<string, Func<IDockable?>>();
        base.InitLayout(layout);
    }

    public void AddDocument(DocumentViewModel doc)
    {
        if (Documents == null) return;
        AddDockable(Documents, doc);
        SetActiveDockable(doc);
        SetFocusedDockable(Documents, doc);
    }

    public void Activate(DocumentViewModel doc)
    {
        if (Documents == null) return;
        SetActiveDockable(doc);
        SetFocusedDockable(Documents, doc);
    }

    /// <summary>Brings a panel to the front, re-adding it if it was closed.</summary>
    public void ShowPanel(Tool panel)
    {
        if (Root == null) return;
        if (panel.Owner is IDock owner && owner.VisibleDockables?.Contains(panel) == true)
        {
            SetActiveDockable(panel);
            return;
        }
        var stack = FindDockable(Root, d => d is ToolDock) as ToolDock;
        if (stack == null) return;
        AddDockable(stack, panel);
        SetActiveDockable(panel);
    }
}
