using Avalonia.Controls;
using Avalonia.Interactivity;
using ModpackMerger.Mac.Services;

namespace ModpackMerger.Mac.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(string? apiKey, bool checkUpdatesOnStartup) : this()
    {
        KeyBox.Text = apiKey ?? "";
        CheckUpdatesBox.IsChecked = checkUpdatesOnStartup;
        VersionText.Text = $"Version installée : {AppPaths.CurrentVersion}";
    }

    public string ApiKey => KeyBox.Text ?? "";

    public bool CheckUpdatesOnStartup => CheckUpdatesBox.IsChecked == true;

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
