using Avalonia.Controls;
using Avalonia.Interactivity;
using StrataPhoto.App.ViewModels;

namespace StrataPhoto.App.Views;

public partial class OptionsBar : UserControl
{
    public OptionsBar() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnCommit(object? sender, RoutedEventArgs e) => Vm?.Tools.CommitActive();

    private void OnCancel(object? sender, RoutedEventArgs e) => Vm?.Tools.CancelActive();
}
