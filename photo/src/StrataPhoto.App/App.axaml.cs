using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Strata.Ui;
using StrataPhoto.App.ViewModels;
using StrataPhoto.App.Views;

namespace StrataPhoto.App;

public partial class App : Application
{
    public override void Initialize()
    {
        Icons.LoadFailed += CrashLog.Write;
        SharedTheme.Load();
        AvaloniaXamlLoader.Load(this);
        SharedTheme.Apply(Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            vm.Dialogs = window;
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                if (StartupArgs.Files.Count > 0) await vm.OpenPathsAsync(StartupArgs.Files);
                if (StartupArgs.SelfTest) await SelfTest.RunAsync(window, vm);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
