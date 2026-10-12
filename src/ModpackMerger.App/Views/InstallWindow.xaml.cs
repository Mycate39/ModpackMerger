using System.IO;
using System.Windows;
using ModpackMerger.App.Animations;
using ModpackMerger.App.Services;
using ModpackMerger.Core.Injection;

namespace ModpackMerger.App.Views;

public enum InstallChoice
{
    Cancelled,
    Installed,
    Portable,
}

/// <summary>Proposée au lancement de l'exe téléchargé : installation, mise à jour ou utilisation sans installer.</summary>
public partial class InstallWindow : Window
{
    public InstallChoice Choice { get; private set; } = InstallChoice.Cancelled;

    public InstallWindow()
    {
        InitializeComponent();
        LocationText.Text = AppInstaller.InstallDirectory;

        var current = AppInstaller.CurrentVersion;
        var installed = AppInstaller.InstalledVersion;
        switch (installed is null ? (int?)null : VersionComparer.Compare(current, installed))
        {
            case null when installed is null:
                VersionText.Text = $"Version {current}";
                break;
            case > 0:
                Title = Heading.Text = "Mettre à jour Modpack Merger";
                InstallLabel.Text = "Mettre à jour";
                VersionText.Text = $"Version installée {installed}  →  nouvelle version {current}";
                break;
            default:
                Title = Heading.Text = "Réinstaller Modpack Merger";
                InstallLabel.Text = "Réinstaller";
                VersionText.Text = $"Version {installed} déjà installée (cet exécutable : {current})";
                break;
        }

        Loaded += (_, _) => Anim.Pop(Logo, from: 0.4, ms: 650);
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        ErrorBar.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Visible;
        var desktop = DesktopShortcut.IsChecked == true;
        try
        {
            await Task.Run(() => AppInstaller.Install(desktop));
            Choice = InstallChoice.Installed;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Progress.Visibility = Visibility.Hidden;
            InstallButton.IsEnabled = true;
            ErrorText.Text = ex is IOException
                ? "Impossible de remplacer la version installée : ferme Modpack Merger s'il est ouvert, puis réessaie."
                : $"Installation impossible : {ex.Message}";
            ErrorBar.Visibility = Visibility.Visible;
        }
    }

    private void OnPortable(object sender, RoutedEventArgs e)
    {
        Choice = InstallChoice.Portable;
        DialogResult = true;
    }
}
