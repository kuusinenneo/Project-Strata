using System.ComponentModel;
using Avalonia.Controls;
using StrataPhoto.App.ViewModels;
using StrataPhoto.App.ViewModels.Panels;
using StrataPhoto.Core.Operations;

namespace StrataPhoto.App.Views.Panels;

public partial class AdjustmentsPanel : UserControl
{
    private AdjustmentsPanelViewModel? _vm;
    private readonly List<Control> _generated = new();

    public AdjustmentsPanel()
    {
        InitializeComponent();
        Curve.PointsChanged += points =>
        {
            if (_vm?.Session?.Operation is Curves c)
            {
                c.Points = points;
                _vm.Session.Schedule();
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as AdjustmentsPanelViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        BuildEditors();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdjustmentsPanelViewModel.Session)) BuildEditors();
    }

    /// <summary>Generates a slider/checkbox per parameter of the current adjustment.</summary>
    private void BuildEditors()
    {
        foreach (var c in _generated) Editors.Children.Remove(c);
        _generated.Clear();
        var session = _vm?.Session;
        if (session == null) return;
        if (session.Operation is Curves curves) Curve.Points = curves.Points;
        foreach (ParamViewModel p in session.Params)
        {
            var editor = Dialogs.ParamEditor(p);
            _generated.Add(editor);
            Editors.Children.Add(editor);
        }
    }
}
