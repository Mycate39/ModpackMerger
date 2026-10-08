using System.Windows;
using Microsoft.Win32;
using ModpackMerger.App.Views;

namespace ModpackMerger.App.Services;

public interface IDialogService
{
    string? PickModpackFile();
    string? PickFolder(string? initialDirectory);
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
    string? EditApiKey(string? current);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current.MainWindow;

    public string? PickModpackFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importer un modpack",
            Filter = "Modpacks (*.zip, *.mrpack)|*.zip;*.mrpack|CurseForge (*.zip)|*.zip|Modrinth (*.mrpack)|*.mrpack",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le modpack personnel (dossier de l'instance ou son dossier « mods »)",
            InitialDirectory = initialDirectory ?? "",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public string? EditApiKey(string? current)
    {
        var window = new SettingsWindow(current) { Owner = Owner };
        return window.ShowDialog() == true ? window.ApiKey : null;
    }
}
