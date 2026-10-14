using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ModpackMerger.Mac.ViewModels;

namespace ModpackMerger.Mac;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = null!;

    public MainWindow() => InitializeComponent();

    public MainWindow(MainViewModel viewModel) : this()
    {
        DataContext = _viewModel = viewModel;

        if (OperatingSystem.IsMacOS())
        {
            // Barre de titre intégrée et barre latérale translucide (vibrancy), comme le Finder.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 32;
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
            Background = Brushes.Transparent;
        }

        // Défilement automatique du journal vers la dernière ligne.
        viewModel.Log.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
        };

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // ───────────── Glisser-déposer (fichier ou dossier depuis le Finder) ─────────────

    private static string? DroppedModpack(DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not [var item] || item.TryGetLocalPath() is not { } path) return null;
        return Directory.Exists(path) || Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".mrpack" ? path : null;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (DroppedModpack(e) is null || _viewModel.IsBusy) return;
        DropOverlay.Classes.Set("visible", true);
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DroppedModpack(e) is not null && !_viewModel.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.Classes.Set("visible", false);

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.Classes.Set("visible", false);
        if (DroppedModpack(e) is { } path) await _viewModel.ImportAsync(path);
    }
}
