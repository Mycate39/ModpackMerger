using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModpackMerger.Mac.Views;

/// <summary>Boîte de confirmation (Avalonia n'a pas de MessageBox) ; renvoie true si l'utilisateur continue.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    public ConfirmWindow(string title, string message, bool okOnly = false) : this()
    {
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        if (okOnly)
        {
            NoButton.IsVisible = false;
            YesButton.Content = "OK";
        }
    }

    private void OnYes(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNo(object? sender, RoutedEventArgs e) => Close(false);
}
