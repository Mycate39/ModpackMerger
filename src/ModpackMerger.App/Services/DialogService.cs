using System.Windows;
using Microsoft.Win32;
using ModpackMerger.App.Views;

namespace ModpackMerger.App.Services;

public interface IDialogService
{
    string? PickModpackFile();
    string? PickFolder(string title, string? initialDirectory);
    bool Confirm(string title, string message);
    /// <summary>Ouvre les réglages ; renvoie null si l'utilisateur annule.</summary>
    (string ApiKey, bool CheckUpdatesOnStartup)? EditSettings(string? apiKey, bool checkUpdatesOnStartup);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current.MainWindow;

    public string? PickModpackFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importer un modpack (archive)",
            Filter = "Modpacks (*.zip, *.mrpack)|*.zip;*.mrpack|CurseForge (*.zip)|*.zip|Modrinth (*.mrpack)|*.mrpack",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory ?? "",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public (string ApiKey, bool CheckUpdatesOnStartup)? EditSettings(string? apiKey, bool checkUpdatesOnStartup)
    {
        var window = new SettingsWindow(apiKey, checkUpdatesOnStartup) { Owner = Owner };
        return window.ShowDialog() == true ? (window.ApiKey, window.CheckUpdatesOnStartup) : null;
    }
}
