using System.Windows;
using ModpackMerger.App.Animations;
using ModpackMerger.App.Services;
using ModpackMerger.App.ViewModels;
using ModpackMerger.App.Views;

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

        var args = e.Args.ToList();

        // Désinstallation, lancée depuis Paramètres Windows › Applications.
        if (args.Contains(AppInstaller.UninstallArgument, StringComparer.OrdinalIgnoreCase))
        {
            if (MessageBox.Show("Désinstaller Modpack Merger ?\n\nTes réglages sont conservés ; tes modpacks ne sont pas touchés.",
                    "Désinstaller", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                AppInstaller.Uninstall();
                MessageBox.Show("Modpack Merger a été désinstallé.", "Désinstallation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        // Exécutable téléchargé : proposer l'installation (ou la mise à jour) avant tout.
        if (AppInstaller.ShouldOfferInstall(args))
        {
            // La fenêtre d'installation ne doit pas fermer l'application en se fermant.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var installer = new InstallWindow();
            installer.ShowDialog();

            switch (installer.Choice)
            {
                case InstallChoice.Installed:
                    AppInstaller.LaunchInstalled(args);
                    Shutdown();
                    return;
                case InstallChoice.Cancelled:
                    Shutdown();
                    return;
            }
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        var settings = new SettingsService();
        settings.Load();

        var viewModel = new MainViewModel(new DialogService(), settings);
        var window = new MainWindow(viewModel);
        MainWindow = window;

        // Permet "Ouvrir avec…" depuis l'explorateur : ModpackMerger.exe <archive ou dossier>
        var packPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (packPath is not null)
            window.Loaded += async (_, _) => await viewModel.ImportAsync(packPath);

        window.Show();
    }
}
