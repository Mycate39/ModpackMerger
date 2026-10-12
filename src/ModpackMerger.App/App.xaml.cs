using System.Windows;
using ModpackMerger.App.Animations;
using ModpackMerger.App.Services;
using ModpackMerger.App.ViewModels;

namespace ModpackMerger.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Anim.RegisterButtonPressFeedback();

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

        // Permet "Ouvrir avec…" depuis l'explorateur : ModpackMerger.exe <archive ou dossier>
        if (e.Args is [var packPath, ..])
            window.Loaded += async (_, _) => await viewModel.ImportAsync(packPath);

        window.Show();
    }
}
