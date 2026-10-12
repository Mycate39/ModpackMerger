using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ModpackMerger.App.Services;

namespace ModpackMerger.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(string? apiKey, bool checkUpdatesOnStartup)
    {
        InitializeComponent();
        KeyBox.Password = apiKey ?? "";
        CheckUpdatesBox.IsChecked = checkUpdatesOnStartup;
        VersionText.Text = $"Version installée : {AppInstaller.CurrentVersion}";
    }

    public string ApiKey => KeyBox.Password;

    public bool CheckUpdatesOnStartup => CheckUpdatesBox.IsChecked == true;

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
