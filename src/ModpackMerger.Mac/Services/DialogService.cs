using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ModpackMerger.Mac.Views;

namespace ModpackMerger.Mac.Services;

public interface IDialogService
{
    Task<string?> PickModpackFileAsync();
    Task<string?> PickFolderAsync(string title, string? initialDirectory);
    Task<bool> ConfirmAsync(string title, string message);
    /// <summary>Ouvre les réglages ; renvoie null si l'utilisateur annule.</summary>
    Task<(string ApiKey, bool CheckUpdatesOnStartup)?> EditSettingsAsync(string? apiKey, bool checkUpdatesOnStartup);
    void OpenFolder(string path);
    void OpenUrl(string url);
}

/// <summary>Sélecteurs de fichiers natifs (NSOpenPanel sur Mac), boîtes de confirmation et ouverture dans le Finder.</summary>
public sealed class DialogService(Func<Window?> owner) : IDialogService
{
    private Window Owner => owner() ?? throw new InvalidOperationException("Fenêtre principale indisponible.");

    public async Task<string?> PickModpackFileAsync()
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importer un modpack (archive)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Modpacks (.zip, .mrpack)") { Patterns = ["*.zip", "*.mrpack"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title, string? initialDirectory)
    {
        var storage = Owner.StorageProvider;
        var start = Directory.Exists(initialDirectory) ? await storage.TryGetFolderFromPathAsync(initialDirectory) : null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        new ConfirmWindow(title, message).ShowDialog<bool>(Owner);

    public async Task<(string ApiKey, bool CheckUpdatesOnStartup)?> EditSettingsAsync(string? apiKey, bool checkUpdatesOnStartup)
    {
        var window = new SettingsWindow(apiKey, checkUpdatesOnStartup);
        return await window.ShowDialog<bool>(Owner) ? (window.ApiKey, window.CheckUpdatesOnStartup) : null;
    }

    public void OpenFolder(string path) => _ = Owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));

    public void OpenUrl(string url) => _ = Owner.Launcher.LaunchUriAsync(new Uri(url));
}
