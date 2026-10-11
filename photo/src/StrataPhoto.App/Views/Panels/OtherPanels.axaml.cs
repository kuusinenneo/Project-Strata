using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StrataPhoto.App.ViewModels.Panels;

namespace StrataPhoto.App.Views.Panels;

public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel() => InitializeComponent();
}

public partial class HistoryPanel : UserControl
{
    public HistoryPanel() => InitializeComponent();
}

public partial class ColorPanel : UserControl
{
    public ColorPanel()
    {
        InitializeComponent();
        Pair.SquareClicked += bg =>
        {
            if (DataContext is ColorPanelViewModel vm) vm.EditingForeground = !bg;
        };
    }
}

public partial class SwatchesPanel : UserControl
{
    public SwatchesPanel()
    {
        InitializeComponent();
        Grid.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (DataContext is not SwatchesPanelViewModel vm) return;
            if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<Border>(includeSelf: true)?.DataContext is not SwatchItem item) return;
            var props = e.GetCurrentPoint(Grid).Properties;
            if (props.IsRightButtonPressed) vm.Remove(item);
            else vm.Pick(item, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
