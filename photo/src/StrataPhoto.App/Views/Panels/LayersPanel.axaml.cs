using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StrataPhoto.App.ViewModels.Panels;

// Drag and drop uses the IDataObject API, obsolete in Avalonia 11.3 but still supported there.
#pragma warning disable CS0618

namespace StrataPhoto.App.Views.Panels;

/// <summary>
/// Layers panel interactions: click to select, click the mask thumbnail to paint on the mask,
/// Ctrl+click a thumbnail to load its pixels as a selection, double-click the name to rename,
/// drag rows to reorder (drop on a group's middle to put the layer inside).
/// </summary>
public partial class LayersPanel : UserControl
{
    private const string DragFormat = "strata-layer";
    private LayerItemViewModel? _pressed;
    private Point _pressPos;
    private bool _dragging;

    public LayersPanel()
    {
        InitializeComponent();
        List.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        List.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, (_, _) => _pressed = null, RoutingStrategies.Tunnel);
        List.AddHandler(DoubleTappedEvent, OnDoubleTapped);
        List.AddHandler(KeyDownEvent, OnRenameKey, RoutingStrategies.Tunnel);
        List.AddHandler(LostFocusEvent, OnRenameLostFocus);
        DragDrop.SetAllowDrop(List, true);
        List.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        List.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private LayersPanelViewModel? Vm => DataContext as LayersPanelViewModel;

    private static LayerItemViewModel? ItemAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as LayerItemViewModel;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var item = ItemAt(e.Source);
        if (item == null || Vm == null) return;
        if (e.Source is Visual v && v.FindAncestorOfType<TextBox>(includeSelf: true) != null) return;
        var thumb = (e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true);
        while (thumb != null && thumb.Tag is not string) thumb = thumb.FindAncestorOfType<Border>();
        if (thumb?.Tag is "mask")
        {
            Vm.SelectMask(item, true);
            e.Handled = true;
            return;
        }
        if (thumb?.Tag is "pixels")
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Vm.SelectPixels(item);
                e.Handled = true;
                return;
            }
            Vm.SelectMask(item, false);
        }
        _pressed = item;
        _pressPos = e.GetPosition(List);
    }

    private async void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed == null || _dragging || !e.GetCurrentPoint(List).Properties.IsLeftButtonPressed) return;
        var d = e.GetPosition(List) - _pressPos;
        if (Math.Abs(d.Y) < 6) return;
        _dragging = true;
        var data = new DataObject();
        data.Set(DragFormat, _pressed);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            _pressed = null;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DragFormat) && ItemAt(e.Source) != null ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm == null || e.Data.Get(DragFormat) is not LayerItemViewModel dragged) return;
        var target = ItemAt(e.Source);
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (target == null || row == null || target == dragged) return;
        double y = e.GetPosition(row).Y / Math.Max(1, row.Bounds.Height);
        bool into = target.IsGroup && y > 0.3 && y < 0.7;
        Vm.Drop(dragged, target, below: y >= 0.5, into);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var item = ItemAt(e.Source);
        if (item == null) return;
        item.IsEditingName = true;
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        var box = row?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "RenameBox");
        if (box != null)
        {
            box.Text = item.Name;
            Dispatcher(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private static void Dispatcher(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a, Avalonia.Threading.DispatcherPriority.Input);

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { Name: "RenameBox" } box || box.DataContext is not LayerItemViewModel item) return;
        if (e.Key == Key.Enter)
        {
            item.CommitRename(box.Text ?? item.Name);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditingName = false;
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { Name: "RenameBox" } box && box.DataContext is LayerItemViewModel { IsEditingName: true } item)
            item.CommitRename(box.Text ?? item.Name);
    }
}
