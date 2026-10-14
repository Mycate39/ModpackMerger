using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModpackMerger.Core.Download;
using ModpackMerger.Core.Injection;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;
using ModpackMerger.Core.Resolution;
using ModpackMerger.Core.Updates;
using ModpackMerger.Mac.Services;

namespace ModpackMerger.Mac.ViewModels;

public enum NotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Même logique que l'application Windows ; les boîtes de dialogue sont asynchrones (Avalonia)
/// et les chemins suivent les conventions de macOS.
/// </summary>
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

    /// <summary>Demande la fermeture de l'application (après avoir lancé une mise à jour).</summary>
    public event EventHandler? ShutdownRequested;

    public MainViewModel(IDialogService dialogs, SettingsService settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        _updates = new UpdateService(_http);
        ModsView = new DataGridCollectionView(Mods) { Filter = o => o is ModItemViewModel m && MatchesSearch(m) };
        ModsView.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(ModItemViewModel.DisplayName), ListSortDirection.Ascending));
        _targetFolder = settings.Current.LastTargetFolder;
        AddLog("Prêt. Importez un modpack : fichier .zip / .mrpack ou dossier (instance, dossier « mods »…).");
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public DataGridCollectionView ModsView { get; }
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

    public static string AppVersion => AppPaths.CurrentVersion;

    /// <summary>Exemple de dossier cible affiché quand le champ est vide.</summary>
    public static string TargetWatermark => AppPaths.DefaultMinecraftMods.Replace(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPack))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private bool _packLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(InjectCommand), nameof(ImportFileCommand),
        nameof(ImportFolderCommand), nameof(ImportPathCommand), nameof(CancelCommand), nameof(BrowseTargetCommand),
        nameof(UseMinecraftFolderCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(OpenTargetFolderCommand))]
    private string? _targetFolder;

    // Mise à jour automatique (bandeau dans la barre latérale).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(UpdateTitle))]
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand), nameof(CheckForUpdatesCommand))]
    private bool _isUpdating;

    [ObservableProperty] private double _updateProgress;

    public bool IsUpdateAvailable => AvailableUpdate is not null;
    public string UpdateTitle => AvailableUpdate is null ? "" : $"Version {AvailableUpdate.Version} disponible";

    // Notification en haut à droite, fermée automatiquement.
    [ObservableProperty] private bool _isNotificationOpen;
    [ObservableProperty] private string _notificationTitle = "";
    [ObservableProperty] private string _notificationMessage = "";
    [ObservableProperty] private NotificationSeverity _notificationSeverity;

    public bool HasPack => PackLoaded;
    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetFolder);
    public bool HasPlan => Plan.Count > 0;
    public int SelectedCount => Mods.Count(m => m.IsSelected);
    public int ConflictCount => Plan.Count(p => p.IsConflict);
    public bool HasConflicts => ConflictCount > 0;
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

    [RelayCommand]
    private void Navigate(string page) => SelectedPage = int.Parse(page);

    // ───────────── 1. Import (fichier ou dossier) ─────────────

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportFile()
    {
        if (await _dialogs.PickModpackFileAsync() is { } path)
            await ImportAsync(path);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportFolder()
    {
        var initial = Directory.Exists(SourcePathInput) ? SourcePathInput : null;
        if (await _dialogs.PickFolderAsync("Choisir le modpack source (dossier de l'instance, dossier « mods » ou pack décompressé)", initial) is { } path)
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
    /// Nettoie un chemin collé : espaces, guillemets, « ~ » du dossier personnel, échappements du Terminal
    /// (glisser un dossier dans le Terminal donne « Mon\ Dossier »). Renvoie null si le chemin est invalide.
    /// </summary>
    internal static string? NormalizeUserPath(string input)
    {
        var path = input.Trim().Trim('"', '\'').Trim();
        if (path.Length == 0) return null;
        if (!Path.Exists(path) && path.Contains("\\ ")) path = path.Replace("\\ ", " ");
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        path = Environment.ExpandEnvironmentVariables(path);
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
        foreach (var mod in ModsView.Cast<ModItemViewModel>().ToList()) mod.IsSelected = selected;
    }

    // ───────────── 2. Dossier cible ─────────────

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseTarget()
    {
        if (await _dialogs.PickFolderAsync("Choisir le modpack personnel (dossier de l'instance ou son dossier « mods »)", TargetFolder) is not { } folder)
            return;
        TargetFolder = ResolveModsFolder(folder);
    }

    /// <summary>Raccourci vers le dossier mods du lanceur Minecraft officiel (~/Library/Application Support/minecraft/mods).</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void UseMinecraftFolder()
    {
        TargetFolder = AppPaths.DefaultMinecraftMods;
        if (!Directory.Exists(TargetFolder))
            Notify("Dossier absent", "Le dossier mods du lanceur Minecraft n'existe pas encore : il sera créé lors de l'injection.",
                NotificationSeverity.Informational);
    }

    /// <summary>Si l'utilisateur choisit le dossier de l'instance, on cible son sous-dossier "mods".</summary>
    private string ResolveModsFolder(string folder)
    {
        if (string.Equals(Path.GetFileName(folder), "mods", StringComparison.OrdinalIgnoreCase)) return folder;
        foreach (var sub in new[] { "mods", Path.Combine(".minecraft", "mods"), Path.Combine("minecraft", "mods") })
        {
            var mods = Path.Combine(folder, sub);
            if (Directory.Exists(mods))
            {
                AddLog($"Sous-dossier « {sub} » détecté, cible : {mods}");
                return mods;
            }
        }
        return folder;
    }

    [RelayCommand(CanExecute = nameof(HasTarget))]
    private void OpenTargetFolder()
    {
        if (Directory.Exists(TargetFolder)) _dialogs.OpenFolder(TargetFolder);
    }

    // ───────────── 3. Analyse (téléchargement + détection des doublons) ─────────────

    private bool CanAnalyze() => !IsBusy && _pack is not null && HasTarget && Mods.Any(m => m.IsSelected);

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private Task Analyze() => RunAsync("Téléchargement des mods…", async ct =>
    {
        var pack = _pack!;
        var target = NormalizeUserPath(TargetFolder!) ?? TargetFolder!;

        if (IsSameFolder(pack, target))
        {
            Notify("Source et cible identiques", "Le modpack source et le dossier cible sont le même dossier.", NotificationSeverity.Warning);
            StatusText = "Analyse annulée : la source et la cible sont identiques.";
            return;
        }

        if (!Directory.Exists(target)
            && !await _dialogs.ConfirmAsync("Dossier introuvable", $"Le dossier suivant n'existe pas :\n{target}\n\nLe créer lors de l'injection ?"))
        {
            StatusText = "Analyse annulée : dossier cible introuvable.";
            return;
        }

        var selected = Mods.Where(m => m.IsSelected).Select(m => m.Entry).ToList();
        AddLog($"Analyse de {selected.Count} mods vers {target}");
        _settings.Current.LastTargetFolder = target;
        _settings.Save();

        ProgressMaximum = selected.Count;
        var downloader = new ModDownloader(_http, AppPaths.CacheDirectory);
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

    [RelayCommand]
    private void ReplaceAllConflicts() => SetAllConflicts(ConflictAction.Replace);

    [RelayCommand]
    private void SkipAllConflicts() => SetAllConflicts(ConflictAction.Skip);

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

        var target = NormalizeUserPath(TargetFolder!) ?? TargetFolder!;
        if (!await _dialogs.ConfirmAsync("Confirmer l'injection",
                $"{install} mod(s) seront ajoutés et {replace} remplacé(s) dans :\n{target}\n\n" +
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
            var report = await ModInjector.InjectAsync(items, target, progress, ct);

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
    private async Task OpenSettings()
    {
        if (await _dialogs.EditSettingsAsync(_settings.Current.CurseForgeApiKey, _settings.Current.CheckUpdatesOnStartup) is not { } result) return;
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
            AddLog($"Mise à jour installée : version {AppVersion}.");
            Notify("Mise à jour installée", $"Modpack Merger est maintenant en version {AppVersion}.", NotificationSeverity.Success);
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
            AddLog($"Mise à jour disponible : version {update.Version} (actuelle : {AppVersion}).");
            Notify("Mise à jour disponible", $"La version {update.Version} est prête à être installée.", NotificationSeverity.Informational);
        }
        else if (userInitiated)
        {
            Notify("Aucune mise à jour", $"Tu as déjà la dernière version ({AppVersion}).", NotificationSeverity.Success);
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotUpdating))]
    private async Task InstallUpdate()
    {
        if (AvailableUpdate is not { } update) return;

        // Hors paquet .app (ex. dotnet run), on ne peut pas se remplacer : on ouvre la page de la release.
        if (!UpdateService.CanSelfUpdate)
        {
            _dialogs.OpenUrl(update.PageUrl);
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
            var zip = await _updates.DownloadAsync(update, progress, CancellationToken.None);
            AddLog("Installation de la mise à jour : l'application va redémarrer.");
            await UpdateService.PrepareAndLaunchUpdaterAsync(zip);
            ShutdownRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
                                   or InvalidOperationException or System.ComponentModel.Win32Exception or TaskCanceledException)
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
        OnPropertyChanged(nameof(HasConflicts));
        InjectCommand.NotifyCanExecuteChanged();
    }

    private void AddLog(string message) => Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
}
