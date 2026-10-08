using System.Windows;
using ModpackMerger.App.Services;
using ModpackMerger.App.ViewModels;

namespace ModpackMerger.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Erreur inattendue", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = new SettingsService();
        settings.Load();

        var viewModel = new MainViewModel(new DialogService(), settings);
        var window = new MainWindow(viewModel);
        MainWindow = window;

        // Permet "Ouvrir avec…" depuis l'explorateur : ModpackMerger.App.exe chemin\du\pack.mrpack
        if (e.Args is [var packPath, ..])
            window.Loaded += async (_, _) => await viewModel.ImportFileAsync(packPath);

        window.Show();
    }
}
