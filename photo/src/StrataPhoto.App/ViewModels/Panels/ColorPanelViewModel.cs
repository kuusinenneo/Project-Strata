using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using StrataPhoto.Core.Operations;

namespace StrataPhoto.App.ViewModels.Panels;

/// <summary>
/// Colour panel: foreground/background swatches, an HSV picker and RGB/hex entry for whichever of
/// the two is being edited.
/// </summary>
public sealed partial class ColorPanelViewModel : PanelBase
{
    private bool _syncing;
    private float _h, _s, _v;

    public ColorPanelViewModel(MainViewModel main) : base(main, "Color", "Color", "color")
    {
        main.Tools.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "Foreground" or "Background") SyncFromSettings();
        };
        SyncFromSettings();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingBackground))]
    private bool _editingForeground = true;

    public bool EditingBackground
    {
        get => !EditingForeground;
        set => EditingForeground = !value;
    }

    partial void OnEditingForegroundChanged(bool value) => SyncFromSettings();

    public Color Foreground => ToAvalonia(Main.Tools.Settings.Foreground);
    public Color Background => ToAvalonia(Main.Tools.Settings.Background);

    private SKColor Edited
    {
        get => EditingForeground ? Main.Tools.Settings.Foreground : Main.Tools.Settings.Background;
        set
        {
            if (EditingForeground) Main.Tools.Settings.Foreground = value;
            else Main.Tools.Settings.Background = value;
        }
    }

    /// <summary>Hue 0..360.</summary>
    public double Hue
    {
        get => _h * 360;
        set { _h = (float)(value / 360); FromHsv(); }
    }

    /// <summary>Saturation 0..1.</summary>
    public double Saturation
    {
        get => _s;
        set { _s = (float)Math.Clamp(value, 0, 1); FromHsv(); }
    }

    /// <summary>Value (brightness) 0..1.</summary>
    public double Brightness
    {
        get => _v;
        set { _v = (float)Math.Clamp(value, 0, 1); FromHsv(); }
    }

    public Color HueColor
    {
        get
        {
            ColorMath.HsvToRgb(_h, 1, 1, out var r, out var g, out var b);
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
    }

    public double Red { get => Edited.Red; set => SetRgb(r: value); }
    public double Green { get => Edited.Green; set => SetRgb(g: value); }
    public double Blue { get => Edited.Blue; set => SetRgb(b: value); }

    public string Hex
    {
        get => $"{Edited.Red:X2}{Edited.Green:X2}{Edited.Blue:X2}";
        set
        {
            if (_syncing) return;
            var s = value.Trim().TrimStart('#');
            if (s.Length == 6 && SKColor.TryParse("#" + s, out var c)) Edited = c.WithAlpha(255);
        }
    }

    private void SetRgb(double? r = null, double? g = null, double? b = null)
    {
        if (_syncing) return;
        var c = Edited;
        Edited = new SKColor((byte)Math.Clamp(r ?? c.Red, 0, 255), (byte)Math.Clamp(g ?? c.Green, 0, 255),
            (byte)Math.Clamp(b ?? c.Blue, 0, 255));
    }

    private void FromHsv()
    {
        if (_syncing) return;
        ColorMath.HsvToRgb(_h, _s, _v, out var r, out var g, out var b);
        _syncing = true;
        Edited = new SKColor((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        _syncing = false;
        Notify(includeHsv: true);
    }

    private void SyncFromSettings()
    {
        if (!_syncing)
        {
            var c = Edited;
            ColorMath.RgbToHsv(c.Red / 255f, c.Green / 255f, c.Blue / 255f, out var h, out var s, out var v);
            if (s > 0.001f && v > 0.001f) _h = h; // keep hue when the colour is grey
            if (v > 0.001f) _s = s;
            _v = v;
        }
        Notify(includeHsv: !_syncing);
    }

    private void Notify(bool includeHsv)
    {
        _syncing = true;
        try
        {
            OnPropertyChanged(nameof(Foreground));
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(Red));
            OnPropertyChanged(nameof(Green));
            OnPropertyChanged(nameof(Blue));
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(HueColor));
            if (includeHsv)
            {
                OnPropertyChanged(nameof(Hue));
                OnPropertyChanged(nameof(Saturation));
                OnPropertyChanged(nameof(Brightness));
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    [RelayCommand] private void Swap() => Main.Tools.Settings.SwapColors();
    [RelayCommand] private void ResetColors() => Main.Tools.Settings.ResetColors();

    public static Color ToAvalonia(SKColor c) => Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
}

public sealed partial class SwatchItem : ObservableObject
{
    public SwatchItem(SKColor color) => Color = color;
    public SKColor Color { get; }
    public Color AvaloniaColor => ColorPanelViewModel.ToAvalonia(Color);
    public IBrush Brush => new SolidColorBrush(AvaloniaColor);
    public string Tooltip => $"#{Color.Red:X2}{Color.Green:X2}{Color.Blue:X2}";
}

/// <summary>Swatches panel: click a swatch to use it as the foreground (Alt+click: background).</summary>
public sealed partial class SwatchesPanelViewModel : PanelBase
{
    private static readonly string[] Defaults =
    {
        "#000000", "#404040", "#808080", "#bfbfbf", "#ffffff", "#ff0000", "#ff8000", "#ffff00", "#80ff00", "#00ff00",
        "#00ff80", "#00ffff", "#0080ff", "#0000ff", "#8000ff", "#ff00ff", "#ff0080", "#800000", "#804000", "#808000",
        "#408000", "#008000", "#008040", "#008080", "#004080", "#000080", "#400080", "#800080", "#800040", "#ffc0c0",
        "#ffe0c0", "#ffffc0", "#c0ffc0", "#c0ffff", "#c0e0ff", "#e0c0ff", "#ffc0e0", "#f5deb3", "#d2b48c", "#8b4513",
        "#409cff", "#2c4a6e", "#ff8a3d", "#1e1f22",
    };

    private static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Strata Photo", "swatches.json");

    public SwatchesPanelViewModel(MainViewModel main) : base(main, "Swatches", "Swatches", "swatches")
    {
        IEnumerable<string> colors = Defaults;
        try
        {
            if (File.Exists(StorePath))
                colors = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? colors.ToList();
        }
        catch
        {
            // fall back to the defaults
        }
        foreach (var c in colors)
            if (SKColor.TryParse(c, out var col)) Items.Add(new SwatchItem(col));
    }

    public ObservableCollection<SwatchItem> Items { get; } = new();

    public void Pick(SwatchItem item, bool background)
    {
        if (background) Main.Tools.Settings.Background = item.Color;
        else Main.Tools.Settings.Foreground = item.Color;
    }

    [RelayCommand]
    private void AddForeground()
    {
        Items.Add(new SwatchItem(Main.Tools.Settings.Foreground));
        Save();
    }

    public void Remove(SwatchItem item)
    {
        Items.Remove(item);
        Save();
    }

    [RelayCommand]
    private void ResetSwatches()
    {
        Items.Clear();
        foreach (var c in Defaults) Items.Add(new SwatchItem(SKColor.Parse(c)));
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Items.Select(i => i.Tooltip).ToList()));
        }
        catch
        {
            // not critical
        }
    }
}
