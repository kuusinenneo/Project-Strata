using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Model;
using StrataPhoto.Core.Operations;
using StrataPhoto.Core.Selections;

namespace StrataPhoto.App.ViewModels;

/// <summary>One slider/checkbox of an adjustment or filter.</summary>
public sealed partial class ParamViewModel : ObservableObject
{
    private readonly Action _changed;

    public ParamViewModel(ParamDef def, double value, Action changed)
    {
        Def = def;
        _value = value;
        _changed = changed;
    }

    public ParamDef Def { get; }
    public string Label => Def.Label;
    public bool IsBool => Def.Unit == "bool";
    public bool IsNumber => !IsBool;
    public double Min => Def.Min;
    public double Max => Def.Max;
    public double Step => Def.Step;
    public string Format => Def.Decimals > 0 ? "0." + new string('0', Def.Decimals) : "0";
    public string Unit => IsBool ? "" : Def.Unit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoolValue))]
    private double _value;

    public bool BoolValue
    {
        get => Value >= 0.5;
        set => Value = value ? 1 : 0;
    }

    partial void OnValueChanged(double value) => _changed();

    public void Reset() => Value = Def.Default;
}

/// <summary>
/// Live preview of an adjustment or filter on the active layer. Results are computed on a
/// background thread (newest request wins) and shown through the layer's preview pixels.
/// </summary>
public sealed partial class OperationPreview : ObservableObject, IDisposable
{
    private readonly DocumentViewModel _doc;
    private readonly PixelLayer? _layer;     // null when editing a mask (no live preview there)
    private readonly SKBitmap _target;
    private readonly Selection _selection;
    private int _generation;
    private SKBitmap? _shown;
    private bool _done;

    private OperationPreview(DocumentViewModel doc, ImageOperation op, SKBitmap target, PixelLayer? layer)
    {
        _doc = doc;
        Operation = op;
        _target = target;
        _layer = layer;
        _selection = doc.Document.Selection;
        foreach (var def in op.Params)
            Params.Add(new ParamViewModel(def, def.Default, Schedule));
    }

    /// <summary>Starts a preview session on the active layer, or returns null with a reason.</summary>
    public static OperationPreview? Start(DocumentViewModel doc, ImageOperation op, out string? reason)
    {
        reason = PixelOps.CannotEditReason(doc.Document);
        if (reason != null) return null;
        var target = doc.Document.EditTarget(out var layer, out bool mask)!;
        return new OperationPreview(doc, op, target, mask ? null : layer as PixelLayer);
    }

    public ImageOperation Operation { get; }
    public string Title => Operation.Name;
    public ObservableCollection<ParamViewModel> Params { get; } = new();
    public bool HasParams => Params.Count > 0;

    [ObservableProperty] private bool _previewEnabled = true;
    [ObservableProperty] private bool _busy;

    partial void OnPreviewEnabledChanged(bool value) => Schedule();

    public DocumentViewModel Document => _doc;

    public ParamValues Values()
    {
        var v = new ParamValues();
        foreach (var p in Params) v[p.Def.Key] = p.Value;
        return v;
    }

    /// <summary>Recompute the preview (call after changing <see cref="ImageOperation.Extra"/>, e.g. curve points).</summary>
    public void Schedule()
    {
        if (_done || _layer == null) return;
        int gen = ++_generation;
        if (!PreviewEnabled)
        {
            ShowPreview(null);
            return;
        }
        var values = Values();
        var extra = Operation.Extra;
        Busy = true;
        Task.Run(() =>
        {
            // Skip stale requests while the user is still dragging a slider.
            Thread.Sleep(15);
            if (gen != _generation) return null;
            var op = Operation;
            lock (op) // Extra is shared; operations themselves are thread-safe
            {
                op.Extra = extra;
                return PixelOps.Compute(_target, _selection, (s, d, r) => op.Apply(s, d, r, values));
            }
        }).ContinueWith(t =>
        {
            var result = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _generation || _done)
                {
                    result?.Dispose();
                    return;
                }
                Busy = false;
                if (result != null) ShowPreview(result);
            });
        });
    }

    private void ShowPreview(SKBitmap? bmp)
    {
        if (_layer == null) return;
        var old = _shown;
        _shown = bmp;
        _layer.PreviewPixels = bmp;
        _doc.Document.Invalidate(_selection.EffectiveBounds(_doc.Document.Width, _doc.Document.Height));
        old?.Dispose();
    }

    /// <summary>Applies the operation to the layer as one undo step.</summary>
    public bool Commit()
    {
        if (_done) return false;
        _generation++;
        var values = Values();
        var op = Operation;
        SKBitmap result;
        lock (op)
            result = PixelOps.Compute(_target, _selection, (s, d, r) => op.Apply(s, d, r, values));
        End();
        bool ok = PixelOps.CommitComputed(_doc.Document, op.Name, result);
        result.Dispose();
        return ok;
    }

    public void Cancel() => End();

    private void End()
    {
        if (_done) return;
        _done = true;
        _generation++;
        if (_layer != null)
        {
            _layer.PreviewPixels = null;
            _doc.Document.Invalidate(_selection.EffectiveBounds(_doc.Document.Width, _doc.Document.Height));
        }
        _shown?.Dispose();
        _shown = null;
    }

    public void ResetParams()
    {
        foreach (var p in Params) p.Reset();
        if (Operation is Curves c) c.Points = Curves.Identity;
        Schedule();
    }

    public void Dispose() => End();
}
