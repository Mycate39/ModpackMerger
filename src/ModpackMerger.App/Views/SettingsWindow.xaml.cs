using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace ModpackMerger.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(string? apiKey)
    {
        InitializeComponent();
        KeyBox.Password = apiKey ?? "";
    }

    public string ApiKey => KeyBox.Password;

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
