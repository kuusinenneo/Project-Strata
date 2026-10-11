using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using StrataPhoto.App.ViewModels;

namespace StrataPhoto.App.Views;

public partial class DocumentView : UserControl
{
    private DocumentViewModel? _vm;
    private MainViewModel? _main;
    private TextEditSession? _session;
    private bool _syncingText;

    public DocumentView()
    {
        InitializeComponent();
        TextInput.TextChanged += (_, _) =>
        {
            // TextChanged can arrive late (after a programmatic update), so only push real differences.
            if (!_syncingText && _session != null && (TextInput.Text ?? "") != _session.Text) _session.Text = TextInput.Text ?? "";
        };
        FinishText.Click += (_, _) =>
        {
            _main?.EndTextEdit();
            Canvas.Focus();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_main != null) _main.PropertyChanged -= OnMainChanged;
        if (_vm != null) _vm.ViewChanged -= PlaceEditor;
        _vm = DataContext as DocumentViewModel;
        _main = _vm?.Main;
        if (_main != null)
        {
            _main.PropertyChanged += OnMainChanged;
            ApplyRulers();
            OnTextEditChanged();
        }
        if (_vm != null) _vm.ViewChanged += PlaceEditor;
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowRulers)) ApplyRulers();
        else if (e.PropertyName == nameof(MainViewModel.TextEdit)) OnTextEditChanged();
    }

    private void ApplyRulers()
    {
        bool show = _main?.ShowRulers ?? true;
        TopRuler.IsVisible = LeftRuler.IsVisible = Corner.IsVisible = show;
        Root.ColumnDefinitions[0].Width = new GridLength(show ? 18 : 0);
        Root.RowDefinitions[0].Height = new GridLength(show ? 18 : 0);
    }

    private void OnTextEditChanged()
    {
        if (_session != null) _session.PropertyChanged -= OnSessionChanged;
        _session = _main?.TextEdit?.Doc == _vm ? _main?.TextEdit : null;
        TextEditor.IsVisible = _session != null;
        if (_session == null) return;
        _session.PropertyChanged += OnSessionChanged;
        _syncingText = true;
        TextInput.Text = _session.Text;
        _syncingText = false;
        PlaceEditor();
        TextInput.Focus();
        TextInput.CaretIndex = TextInput.Text?.Length ?? 0;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextEditSession.Anchor)) PlaceEditor();
        else if (e.PropertyName == nameof(TextEditSession.Text) && _session != null && TextInput.Text != _session.Text)
        {
            _syncingText = true;
            TextInput.Text = _session.Text;
            _syncingText = false;
        }
    }

    /// <summary>Keeps the editor just below the text on the canvas, inside the view.</summary>
    private void PlaceEditor()
    {
        if (_session == null || Canvas.ViewModel == null) return;
        var p = Canvas.ToView(_session.Anchor);
        double w = 274, h = TextEditor.Bounds.Height > 0 ? TextEditor.Bounds.Height : 130;
        double x = Math.Clamp(p.X, 4, Math.Max(4, Overlay.Bounds.Width - w - 4));
        double y = Math.Clamp(p.Y + 8, 4, Math.Max(4, Overlay.Bounds.Height - h - 4));
        Avalonia.Controls.Canvas.SetLeft(TextEditor, x);
        Avalonia.Controls.Canvas.SetTop(TextEditor, y);
    }
}
