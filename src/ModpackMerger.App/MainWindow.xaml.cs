using System.Collections.Specialized;
using System.IO;
using System.Windows;
using ModpackMerger.App.ViewModels;

namespace ModpackMerger.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        // Défilement automatique du journal vers la dernière ligne.
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add) LogScroll.ScrollToEnd();
        };
    }

    private static string? DroppedModpack(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && Path.GetExtension(files[0]).ToLowerInvariant() is ".zip" or ".mrpack"
            ? files[0]
            : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedModpack(e) is not null && !_viewModel.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedModpack(e) is { } path)
            await _viewModel.ImportFileAsync(path);
    }
}
