using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModpackMerger.Mac.Services;
using ModpackMerger.Mac.ViewModels;

namespace ModpackMerger.Mac;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    /// <summary>Pack ouvert depuis le Finder (« Ouvrir avec », glisser sur l'icône du Dock) avant que la fenêtre soit prête.</summary>
    private string? _pendingPath;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var settings = new SettingsService();
            settings.Load();

            MainWindow? window = null;
            _viewModel = new MainViewModel(new DialogService(() => window), settings);
            window = new MainWindow(_viewModel);
            desktop.MainWindow = window;
            _viewModel.ShutdownRequested += (_, _) => desktop.Shutdown();

            BuildMenus(window, _viewModel);
            ListenForFinderFiles();

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                _ = new Views.ConfirmWindow("Erreur inattendue", e.Exception.Message, okOnly: true).ShowDialog<bool>(window);
            };

            // Ligne de commande : ModpackMerger <archive ou dossier> (macOS ajoute parfois -psn_…, ignoré).
            _pendingPath ??= args.FirstOrDefault(a => !a.StartsWith('-'));
            var justUpdated = args.Contains(UpdateService.UpdatedArgument, StringComparer.OrdinalIgnoreCase);
            window.Opened += async (_, _) =>
            {
                if (_pendingPath is { } path)
                {
                    _pendingPath = null;
                    await _viewModel.ImportAsync(path);
                }
                await _viewModel.OnStartedAsync(justUpdated);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Fichiers ou dossiers ouverts depuis le Finder (déclarés dans Info.plist).</summary>
    private void ListenForFinderFiles()
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable) return;
        activatable.Activated += async (_, e) =>
        {
            if (e is not FileActivatedEventArgs { Files: [{ } item, ..] } || item.TryGetLocalPath() is not { } path) return;
            if (_viewModel is null || ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.IsVisible: false })
                _pendingPath = path;
            else
                await _viewModel.ImportAsync(path);
        };
    }

    /// <summary>Barre de menus macOS : menu de l'application (Réglages ⌘,) et menu Fichier (Importer ⌘O).</summary>
    private void BuildMenus(Window window, MainViewModel vm)
    {
        var appMenu = new NativeMenu();
        appMenu.Add(new NativeMenuItem("Réglages…") { Command = vm.OpenSettingsCommand, Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta) });
        appMenu.Add(new NativeMenuItem("Rechercher les mises à jour…") { Command = vm.CheckForUpdatesCommand });
        NativeMenu.SetMenu(this, appMenu);

        var file = new NativeMenu();
        file.Add(new NativeMenuItem("Importer une archive…") { Command = vm.ImportFileCommand, Gesture = new KeyGesture(Key.O, KeyModifiers.Meta) });
        file.Add(new NativeMenuItem("Importer un dossier…") { Command = vm.ImportFolderCommand, Gesture = new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift) });
        file.Add(new NativeMenuItemSeparator());
        file.Add(new NativeMenuItem("Choisir le dossier cible…") { Command = vm.BrowseTargetCommand, Gesture = new KeyGesture(Key.T, KeyModifiers.Meta) });

        var actions = new NativeMenu();
        actions.Add(new NativeMenuItem("Analyser") { Command = vm.AnalyzeCommand, Gesture = new KeyGesture(Key.R, KeyModifiers.Meta) });
        actions.Add(new NativeMenuItem("Injecter") { Command = vm.InjectCommand, Gesture = new KeyGesture(Key.I, KeyModifiers.Meta) });
        actions.Add(new NativeMenuItem("Annuler l'opération") { Command = vm.CancelCommand, Gesture = new KeyGesture(Key.OemPeriod, KeyModifiers.Meta) });

        var view = new NativeMenu();
        string[] pages = ["Configuration", "Mods du pack", "Doublons & plan", "Journal"];
        for (var i = 0; i < pages.Length; i++)
        {
            var page = i;
            view.Add(new NativeMenuItem(pages[i])
            {
                Command = vm.NavigateCommand,
                CommandParameter = page.ToString(),
                Gesture = new KeyGesture(Key.D1 + page, KeyModifiers.Meta),
            });
        }

        NativeMenu.SetMenu(window, new NativeMenu
        {
            new NativeMenuItem("Fichier") { Menu = file },
            new NativeMenuItem("Actions") { Menu = actions },
            new NativeMenuItem("Présentation") { Menu = view },
        });
    }
}
