using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Strata.Ui;

/// <summary>Label + slider + numeric box bound to one value (used in the options bar and panels).</summary>
public sealed class ValueSlider : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ValueSlider, string?>(nameof(Label));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Step), 1);

    public static readonly StyledProperty<string> FormatProperty =
        AvaloniaProperty.Register<ValueSlider, string>(nameof(Format), "0");

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<ValueSlider, string?>(nameof(Unit));

    public static readonly StyledProperty<double> SliderWidthProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(SliderWidth), double.NaN);

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<ValueSlider, bool>(nameof(Compact));

    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Slider _slider = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 60 };
    private readonly NumericUpDown _number = new()
    {
        Width = 74,
        ShowButtonSpinner = false,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(6, 0, 0, 0),
    };
    private readonly TextBlock _unit = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), Opacity = 0.7 };
    private bool _updating;

    public ValueSlider()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        Grid.SetColumn(_label, 0);
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_number, 2);
        Grid.SetColumn(_unit, 3);
        grid.Children.Add(_label);
        grid.Children.Add(_slider);
        grid.Children.Add(_number);
        grid.Children.Add(_unit);
        Content = grid;

        _slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_updating) Commit(_slider.Value);
        };
        _number.ValueChanged += (_, e) =>
        {
            if (!_updating && e.NewValue is { } v) Commit((double)v);
        };
        Sync();
    }

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double SliderWidth { get => GetValue(SliderWidthProperty); set => SetValue(SliderWidthProperty, value); }
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    private void Commit(double v)
    {
        v = Math.Clamp(v, Minimum, Maximum);
        if (Step > 0) v = Math.Round(v / Step) * Step;
        if (Math.Abs(v - Value) < 1e-9) return;
        Value = v;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty ||
            change.Property == LabelProperty || change.Property == UnitProperty || change.Property == FormatProperty ||
            change.Property == StepProperty || change.Property == SliderWidthProperty || change.Property == CompactProperty)
            Sync();
    }

    private void Sync()
    {
        _updating = true;
        try
        {
            _label.Text = Label;
            _label.IsVisible = !string.IsNullOrEmpty(Label);
            _unit.Text = Unit;
            _unit.IsVisible = !string.IsNullOrEmpty(Unit);
            _slider.Minimum = Minimum;
            _slider.Maximum = Maximum;
            _slider.SmallChange = Step;
            _slider.LargeChange = Math.Max(Step, (Maximum - Minimum) / 10);
            _slider.Value = Value;
            _slider.Width = SliderWidth;
            _number.Minimum = (decimal)Minimum;
            _number.Maximum = (decimal)Maximum;
            _number.Increment = (decimal)Step;
            _number.FormatString = Format;
            _number.Value = (decimal)Value;
            _number.Width = Compact ? 56 : 74;
        }
        finally
        {
            _updating = false;
        }
    }
}
