using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using StrataPhoto.Core.Editing;
using StrataPhoto.Core.Model;

namespace StrataPhoto.App.ViewModels;

/// <summary>
/// Typing into a text layer with the Text tool. Every keystroke re-renders the layer; the whole
/// typing session is merged into one "Edit Type" history step.
/// </summary>
public sealed partial class TextEditSession : ObservableObject
{
    private static int _counter;
    private readonly string _mergeKey;
    private readonly bool _isNew;
    private readonly int _historyAtStart;

    public TextEditSession(DocumentViewModel doc, TextLayer layer, bool isNew)
    {
        Doc = doc;
        Layer = layer;
        _isNew = isNew;
        _text = layer.Text.Text;
        _mergeKey = $"typing:{layer.Id}:{Interlocked.Increment(ref _counter)}";
        _historyAtStart = doc.Document.History.Position;
    }

    public DocumentViewModel Doc { get; }
    public TextLayer Layer { get; }

    [ObservableProperty] private string _text;

    partial void OnTextChanged(string value) => Apply(Layer.Text with { Text = value });

    public void Apply(TextProps props)
    {
        if (!Doc.Document.Root.Contains(Layer)) return;
        LayerOps.EditText(Doc.Document, Layer, props, _mergeKey);
        OnPropertyChanged(nameof(Anchor));
    }

    /// <summary>Top-left of the text box in document coordinates (for placing the editor).</summary>
    public SKRect Bounds => TextRenderer.Measure(Layer.Text);

    public SKPoint Anchor => new(Bounds.Left, Bounds.Bottom);

    /// <summary>Ends editing. A new layer that is still empty is removed again.</summary>
    public void Finish()
    {
        if (_isNew && string.IsNullOrWhiteSpace(Layer.Text.Text))
        {
            var h = Doc.Document.History;
            // Nothing typed: undo the layer creation itself so no trace is left in History.
            if (h.Position == _historyAtStart && h.Position >= 1 && h.Entries[h.Position - 1].Name == "Type Layer")
                h.Undo();
            else
                LayerOps.Delete(Doc.Document, Layer);
        }
    }
}
