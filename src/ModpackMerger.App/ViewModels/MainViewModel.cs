using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModpackMerger.App.Services;
using ModpackMerger.Core.Download;
using ModpackMerger.Core.Injection;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;
using ModpackMerger.Core.Resolution;
using ModpackMerger.Core.Updates;

namespace ModpackMerger.App.ViewModels;

public enum NotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

public partial class MainViewModel : ObservableObject
{
    public const int SetupPage = 0, ModsPage = 1, PlanPage = 2, LogPage = 3;

    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly HttpClient _http = HttpClientFactory.Create();
    private CancellationTokenSource? _cts;
    private ModpackInfo? _pack;
    private int _notificationVersion;
    private readonly UpdateService _updates;

    public MainViewModel(IDialogService dialogs, SettingsService settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        _updates = new UpdateService(_http);
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = o => o is ModItemViewModel m && MatchesSearch(m);
        ModsView.SortDescriptions.Add(new SortDescription(nameof(ModItemViewModel.DisplayName), ListSortDirection.Ascending));
        _targetFolder = settings.Current.LastTargetFolder;
        AddLog("Prêt. Importez un modpack : fichier .zip / .mrpack ou dossier (instance, dossier « mods »…).");
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ICollectionView ModsView { get; }
    public ObservableCollection<PlanItemViewModel> Plan { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] private string _packTitle = "";
    [ObservableProperty] private string _packDetails = "";
    [ObservableProperty] private string _packFormatLabel = "";
    [ObservableProperty] private string _statusText = "Importez un modpack pour commencer.";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMaximum = 1;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private int _selectedPage;
    [ObservableProperty] private string _searchText = "";

    /// <summary>Chemin saisi ou collé dans la carte « Modpack source » (dossier ou archive).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportPathCommand))]
    private string _sourcePathInput = "";

    /// <summary>Version affichée dans le volet de navigation, ex. "1.0.0" (sans le hash de commit).</summary>
    public static string AppVersion { get; } =
        (System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "")
        .Split('+')[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPack))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private bool _packLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(InjectCommand), nameof(ImportFileCommand),
        nameof(ImportFolderCommand), nameof(ImportPathCommand), nameof(CancelCommand), nameof(BrowseTargetCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(OpenTargetFolderCommand))]
    private string? _targetFolder;

    // Mise à jour automatique (bandeau dans le volet de navigation).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(UpdateTitle))]
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand), nameof(CheckForUpdatesCommand))]
    private bool _isUpdating;

    [ObservableProperty] private double _updateProgress;

    public bool IsUpdateAvailable => AvailableUpdate is not null;
    public string UpdateTitle => AvailableUpdate is null ? "" : $"Version {AvailableUpdate.Version} disponible";

    // Notification façon InfoBar (Windows 11), fermée automatiquement.
    [ObservableProperty] private bool _isNotificationOpen;
    [ObservableProperty] private string _notificationTitle = "";
    [ObservableProperty] private string _notificationMessage = "";
    [ObservableProperty] private NotificationSeverity _notificationSeverity;

    public bool HasPack => PackLoaded;
    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetFolder);
    public bool HasPlan => Plan.Count > 0;
    public int SelectedCount => Mods.Count(m => m.IsSelected);
    public int ConflictCount => Plan.Count(p => p.IsConflict);
    public string SelectionSummary => $"{SelectedCount} / {Mods.Count} mods sélectionnés";

    public string PlanSummary
    {
        get
        {
            if (Plan.Count == 0) return "Lancez l'analyse pour comparer la sélection avec votre modpack personnel.";
            var identical = Plan.Count(p => p.Kind == ConflictKind.Identical);
            return $"{Plan.Count - ConflictCount - identical} nouveau(x) · {ConflictCount} doublon(s) à arbitrer · {identical} déjà présent(s)";
        }
    }

    partial void OnSearchTextChanged(string value) => ModsView.Refresh();

    partial void OnTargetFolderChanged(string? value) => InvalidatePlan();

    private bool MatchesSearch(ModItemViewModel m) =>
        string.IsNullOrWhiteSpace(SearchText)
        || m.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
        || m.FileName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Paramètre en texte pour être utilisable directement depuis le XAML (CommandParameter="1").</summary>
    [RelayCommand]
    private void Navigate(string page) => SelectedPage = int.Parse(page);

    // ───────────── 1. Import (fichier ou dossier) ─────────────

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportFile()
    {
        if (_dialogs.PickModpackFile() is { } path)
            await ImportAsync(path);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportFolder()
    {
        var initial = Directory.Exists(SourcePathInput) ? SourcePathInput : null;
        if (_dialogs.PickFolder("Choisir le modpack source (dossier de l'instance, dossier « mods » ou pack décompressé)", initial) is { } path)
            await ImportAsync(path);
    }

    private bool CanImportPath() => !IsBusy && !string.IsNullOrWhiteSpace(SourcePathInput);

    /// <summary>Importe le chemin tapé ou collé (Entrée ou bouton « Importer »).</summary>
    [RelayCommand(CanExecute = nameof(CanImportPath))]
    private async Task ImportPath()
    {
        if (NormalizeUserPath(SourcePathInput) is not { } path)
        {
            Notify("Chemin invalide", "Ce chemin contient des caractères non autorisés.", NotificationSeverity.Error);
            return;
        }
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            Notify("Chemin introuvable", $"Aucun dossier ni fichier à cet emplacement :\n{path}", NotificationSeverity.Error);
            return;
        }
        await ImportAsync(path);
    }

    /// <summary>
    /// Nettoie un chemin collé : espaces, guillemets ajoutés par « Copier en tant que chemin » de l'explorateur,
    /// variables d'environnement (%APPDATA%…). Renvoie null si le chemin est invalide.
    /// </summary>
    internal static string? NormalizeUserPath(string input)
    {
        var path = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"', '\'').Trim());
        if (path.Length == 0) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Importe une archive (.zip / .mrpack) ou un dossier.</summary>
    public async Task ImportAsync(string path)
    {
        if (IsBusy) return;
        ModpackInfo pack;
        try
        {
            pack = await Task.Run(() => new ModpackReader().Read(path));
        }
        catch (Exception ex) when (ex is ModpackFormatException or IOException or UnauthorizedAccessException)
        {
            AddLog($"Import impossible : {ex.Message}");
            Notify("Import impossible", ex.Message, NotificationSeverity.Error);
            return;
        }

        _pack = pack;
        SourcePathInput = path;
        foreach (var mod in Mods) mod.PropertyChanged -= OnModPropertyChanged;
        Mods.Clear();
        foreach (var entry in pack.Mods)
        {
            var item = new ModItemViewModel(entry);
            item.PropertyChanged += OnModPropertyChanged;
            Mods.Add(item);
        }
        InvalidatePlan();

        PackFormatLabel = pack.Format switch
        {
            ModpackFormat.CurseForge => "CurseForge",
            ModpackFormat.Modrinth => "Modrinth",
            _ => "Dossier",
        };
        PackTitle = pack.Version is null ? pack.Name : $"{pack.Name} — v{pack.Version}";
        PackDetails = string.Join("  ·  ", new[]
        {
            pack.MinecraftVersion is null ? null : $"Minecraft {pack.MinecraftVersion}",
            pack.Loader,
            pack.Author is null ? null : $"par {pack.Author}",
            $"{pack.Mods.Count} mods",
        }.Where(s => !string.IsNullOrEmpty(s)));
        PackLoaded = true;
        NotifySelectionChanged();
        AddLog($"Modpack importé : {PackTitle} ({PackFormatLabel}, {pack.Mods.Count} mods) depuis {path}");

        // Sans dossier cible, on reste sur la configuration ; sinon on passe directement au choix des mods.
        if (HasTarget) SelectedPage = ModsPage;

        await RunAsync("Identification des mods…", async ct =>
        {
            IsProgressIndeterminate = true;
            var resolver = new ModpackResolver(_http, _settings.Current.CurseForgeApiKey);
            var log = new Progress<string>(AddLog); // créé sur le thread UI pour y ramener les messages
            await Task.Run(() => resolver.ResolveAsync(pack, log, ct), ct);
            foreach (var mod in Mods) mod.Refresh();
            ModsView.Refresh();
            StatusText = "Mods identifiés. Décochez ceux à exclure, puis lancez l'analyse.";
            Notify("Modpack importé", $"{pack.Name} : {pack.Mods.Count} mods détectés.", NotificationSeverity.Success);
        });
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModItemViewModel.IsSelected)) return;
        NotifySelectionChanged();
        InvalidatePlan();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectedCount));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll() => SetVisibleSelection(true);

    [RelayCommand]
    private void SelectNone() => SetVisibleSelection(false);

    /// <summary>N'agit que sur les mods visibles avec le filtre de recherche courant.</summary>
    private void SetVisibleSelection(bool selected)
    {
        foreach (var mod in ModsView.Cast<ModItemViewModel>()) mod.IsSelected = selected;
    }

    // ───────────── 2. Dossier cible ─────────────

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void BrowseTarget()
    {
        if (_dialogs.PickFolder("Choisir le modpack personnel (dossier de l'instance ou son dossier « mods »)", TargetFolder) is not { } folder)
            return;

        // Si l'utilisateur choisit le dossier de l'instance, on cible son sous-dossier "mods".
        foreach (var sub in new[] { "mods", Path.Combine(".minecraft", "mods"), Path.Combine("minecraft", "mods") })
        {
            var mods = Path.Combine(folder, sub);
            if (!string.Equals(Path.GetFileName(folder), "mods", StringComparison.OrdinalIgnoreCase) && Directory.Exists(mods))
            {
                AddLog($"Sous-dossier « {sub} » détecté, cible : {mods}");
                folder = mods;
                break;
            }
        }

        TargetFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(HasTarget))]
    private void OpenTargetFolder()
    {
        if (Directory.Exists(TargetFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TargetFolder}\"") { UseShellExecute = true });
    }

    // ───────────── 3. Analyse (téléchargement + détection des doublons) ─────────────

    private bool CanAnalyze() => !IsBusy && _pack is not null && HasTarget && Mods.Any(m => m.IsSelected);

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private Task Analyze() => RunAsync("Téléchargement des mods…", async ct =>
    {
        var pack = _pack!;
        var target = TargetFolder!;

        if (IsSameFolder(pack, target))
        {
            Notify("Source et cible identiques", "Le modpack source et le dossier cible sont le même dossier.", NotificationSeverity.Warning);
            StatusText = "Analyse annulée : la source et la cible sont identiques.";
            return;
        }

        if (!Directory.Exists(target)
            && !_dialogs.Confirm("Dossier introuvable", $"Le dossier suivant n'existe pas :\n{target}\n\nLe créer lors de l'injection ?"))
        {
            StatusText = "Analyse annulée : dossier cible introuvable.";
            return;
        }

        var selected = Mods.Where(m => m.IsSelected).Select(m => m.Entry).ToList();
        AddLog($"Analyse de {selected.Count} mods vers {target}");
        _settings.Current.LastTargetFolder = target;
        _settings.Save();

        ProgressMaximum = selected.Count;
        var downloader = new ModDownloader(_http, ModDownloader.DefaultCacheDirectory);
        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressValue = p.Completed;
            StatusText = $"Récupération {p.Completed}/{p.Total} : {p.CurrentItem}";
        });
        var result = await Task.Run(() => downloader.DownloadAsync(pack, selected, progress, ct), ct);

        foreach (var failure in result.Failures)
            AddLog($"ÉCHEC du téléchargement : {failure.Entry.DisplayName} — {failure.Reason}");

        // Sans clé API CurseForge, le nom affiché vient du nom de fichier : le jar donne mieux.
        foreach (var mod in result.Downloaded.Where(d => d.Entry.Source == ModSource.CurseForge && d.Metadata.Name is not null
                                                         && string.IsNullOrEmpty(_settings.Current.CurseForgeApiKey)))
            mod.Entry.DisplayName = mod.Metadata.Name!;
        foreach (var mod in Mods) mod.Refresh();

        IsProgressIndeterminate = true;
        StatusText = "Analyse du dossier cible…";
        var installed = await Task.Run(() => ConflictAnalyzer.ScanModsFolder(target), ct);
        var plan = ConflictAnalyzer.Analyze(result.Downloaded, installed);

        Plan.Clear();
        // Conflits d'abord, puis nouveaux, puis identiques.
        foreach (var item in plan.OrderBy(p => p.IsConflict ? 0 : p.Kind == ConflictKind.New ? 1 : 2))
            Plan.Add(new PlanItemViewModel(item));
        NotifyPlanChanged();

        AddLog($"Dossier cible : {installed.Count} jar(s) existant(s). {PlanSummary}");
        if (result.Failures.Count == 0)
        {
            StatusText = "Analyse terminée. Vérifiez les doublons puis cliquez sur « Injecter ».";
            Notify("Analyse terminée", PlanSummary, ConflictCount > 0 ? NotificationSeverity.Warning : NotificationSeverity.Success);
        }
        else
        {
            StatusText = $"Analyse terminée avec {result.Failures.Count} échec(s) de téléchargement (voir le journal).";
            Notify("Analyse terminée avec des erreurs", $"{result.Failures.Count} mod(s) n'ont pas pu être récupérés.", NotificationSeverity.Error);
        }
        SelectedPage = PlanPage;
    });

    /// <summary>Évite d'injecter un dossier dans lui-même (source « dossier » = cible).</summary>
    private static bool IsSameFolder(ModpackInfo pack, string target)
    {
        if (pack.Format != ModpackFormat.Folder) return false;
        var t = Path.GetFullPath(target).TrimEnd('\\', '/');
        var source = Path.GetFullPath(pack.SourcePath).TrimEnd('\\', '/');
        return new[] { source, Path.Combine(source, "mods"), Path.Combine(source, ".minecraft", "mods"), Path.Combine(source, "minecraft", "mods") }
            .Any(s => string.Equals(s, t, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void SetAllConflicts(ConflictAction action)
    {
        foreach (var item in Plan.Where(p => p.IsConflict && p.AvailableActions.Contains(action)))
            item.Action = action;
    }

    // ───────────── 4. Injection ─────────────

    private bool CanInject() => !IsBusy && Plan.Count > 0;

    [RelayCommand(CanExecute = nameof(CanInject))]
    private async Task Inject()
    {
        var items = Plan.Select(p => p.Item).ToList();
        var install = items.Count(i => i.Action is ConflictAction.Install or ConflictAction.KeepBoth);
        var replace = items.Count(i => i.Action == ConflictAction.Replace);
        if (install + replace == 0)
        {
            Notify("Rien à faire", "Tous les mods du plan sont marqués « Ignorer ».", NotificationSeverity.Informational);
            return;
        }

        if (!_dialogs.Confirm("Confirmer l'injection",
                $"{install} mod(s) seront ajoutés et {replace} remplacé(s) dans :\n{TargetFolder}\n\n" +
                "Les fichiers remplacés seront déplacés dans un dossier « mods-backup » à côté du dossier mods.\n\nContinuer ?"))
            return;

        await RunAsync("Injection…", async ct =>
        {
            ProgressMaximum = items.Count;
            var progress = new Progress<InjectionProgress>(p =>
            {
                ProgressValue = p.Completed;
                StatusText = p.Message;
                AddLog(p.Message);
            });
            var report = await ModInjector.InjectAsync(items, TargetFolder!, progress, ct);

            var summary = $"{report.Installed} ajouté(s), {report.Replaced} remplacé(s), {report.Skipped} ignoré(s), {report.Errors.Count} erreur(s).";
            AddLog("Terminé : " + summary);
            if (report.BackupDirectory is not null) AddLog($"Sauvegarde des fichiers remplacés : {report.BackupDirectory}");
            StatusText = report.Errors.Count == 0 ? "Injection terminée avec succès." : "Injection terminée avec des erreurs (voir le journal).";
            Notify(report.Errors.Count == 0 ? "Injection réussie" : "Injection terminée avec des erreurs", summary,
                report.Errors.Count == 0 ? NotificationSeverity.Success : NotificationSeverity.Error);

            Plan.Clear(); // le plan est consommé : relancer l'analyse avant une nouvelle injection
            NotifyPlanChanged();
            SelectedPage = LogPage;
        });
    }

    // ───────────── Divers ─────────────

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenSettings()
    {
        if (_dialogs.EditSettings(_settings.Current.CurseForgeApiKey, _settings.Current.CheckUpdatesOnStartup) is not { } result) return;
        var key = string.IsNullOrWhiteSpace(result.ApiKey) ? null : result.ApiKey.Trim();
        var keyChanged = key != _settings.Current.CurseForgeApiKey;
        _settings.Current.CurseForgeApiKey = key;
        _settings.Current.CheckUpdatesOnStartup = result.CheckUpdatesOnStartup;
        _settings.Save();
        var message = !keyChanged ? "Réglages enregistrés."
            : key is null ? "Clé API CurseForge supprimée."
            : "Clé API CurseForge enregistrée (prise en compte au prochain import).";
        AddLog(message);
        Notify("Réglages enregistrés", message, NotificationSeverity.Informational);
    }

    // ───────────── Mise à jour automatique ─────────────

    /// <summary>Appelé une fois la fenêtre affichée.</summary>
    public async Task OnStartedAsync(bool justUpdated)
    {
        if (justUpdated)
        {
            AddLog($"Mise à jour installée : version {AppInstaller.CurrentVersion}.");
            Notify("Mise à jour installée", $"Modpack Merger est maintenant en version {AppInstaller.CurrentVersion}.", NotificationSeverity.Success);
        }
        else if (_settings.Current.CheckUpdatesOnStartup && UpdateService.IsSupported)
        {
            await CheckForUpdatesCoreAsync(userInitiated: false);
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotUpdating))]
    private Task CheckForUpdates() => CheckForUpdatesCoreAsync(userInitiated: true);

    private bool IsNotUpdating() => !IsUpdating;

    private async Task CheckForUpdatesCoreAsync(bool userInitiated)
    {
        if (!UpdateService.IsSupported)
        {
            if (userInitiated) Notify("Mises à jour", "Indisponible dans une version de développement.", NotificationSeverity.Informational);
            return;
        }

        var update = await _updates.CheckAsync(CancellationToken.None);
        AvailableUpdate = update;
        if (update is not null)
        {
            AddLog($"Mise à jour disponible : version {update.Version} (actuelle : {AppInstaller.CurrentVersion}).");
            Notify("Mise à jour disponible", $"La version {update.Version} est prête à être installée.", NotificationSeverity.Informational);
        }
        else if (userInitiated)
        {
            Notify("Aucune mise à jour", $"Tu as déjà la dernière version ({AppInstaller.CurrentVersion}).", NotificationSeverity.Success);
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotUpdating))]
    private async Task InstallUpdate()
    {
        if (AvailableUpdate is not { } update) return;

        // Copie non installée ("Utiliser sans installer") : on ne peut pas se remplacer, on ouvre la page de la release.
        if (!AppInstaller.IsRunningInstalledCopy)
        {
            Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            return;
        }

        if (IsBusy)
        {
            Notify("Opération en cours", "Attends la fin de l'opération en cours avant de mettre à jour.", NotificationSeverity.Warning);
            return;
        }

        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            AddLog($"Téléchargement de la version {update.Version}…");
            var progress = new Progress<double>(p => UpdateProgress = p);
            var installer = await _updates.DownloadAsync(update, progress, CancellationToken.None);
            AddLog("Installation de la mise à jour : l'application va redémarrer.");
            UpdateService.LaunchUpdater(installer);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
                                   or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            IsUpdating = false;
            AddLog("ÉCHEC de la mise à jour : " + ex.Message);
            Notify("Mise à jour impossible", ex.Message, NotificationSeverity.Error);
        }
    }

    [RelayCommand]
    private void CloseNotification() => IsNotificationOpen = false;

    private bool IsIdle() => !IsBusy;

    /// <summary>Affiche une notification qui se ferme seule après quelques secondes.</summary>
    private async void Notify(string title, string message, NotificationSeverity severity)
    {
        var version = ++_notificationVersion;
        // Fermer puis rouvrir relance l'animation d'entrée quand une notification en remplace une autre.
        IsNotificationOpen = false;
        NotificationTitle = title;
        NotificationMessage = message;
        NotificationSeverity = severity;
        IsNotificationOpen = true;

        await Task.Delay(severity == NotificationSeverity.Error ? 9000 : 5000);
        if (version == _notificationVersion) IsNotificationOpen = false;
    }

    /// <summary>Exécute une opération longue avec gestion commune : état occupé, annulation, erreurs.</summary>
    private async Task RunAsync(string status, Func<CancellationToken, Task> operation)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusText = status;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        try
        {
            await operation(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Opération annulée.";
            AddLog("Opération annulée par l'utilisateur.");
            Notify("Opération annulée", "L'opération en cours a été interrompue.", NotificationSeverity.Informational);
        }
        catch (Exception ex)
        {
            StatusText = "Erreur : " + ex.Message;
            AddLog("ERREUR : " + ex);
            Notify("Erreur", ex.Message, NotificationSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void InvalidatePlan()
    {
        if (Plan.Count == 0) return;
        Plan.Clear();
        NotifyPlanChanged();
        StatusText = "Sélection ou dossier cible modifié : relancez l'analyse.";
    }

    private void NotifyPlanChanged()
    {
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(ConflictCount));
        InjectCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message) => Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
}
