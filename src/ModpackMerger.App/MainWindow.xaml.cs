using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ModpackMerger.App.Animations;
using ModpackMerger.App.ViewModels;

namespace ModpackMerger.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>Jusqu'à cette date, les lignes des grilles apparaissent en cascade (après un import / une analyse).</summary>
    private DateTime _staggerUntil;
    private int _staggerIndex;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Défilement automatique du journal vers la dernière ligne.
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add) LogScroll.ScrollToEnd();
        };

        // Nouvelle liste de mods ou nouveau plan : on rejoue l'apparition en cascade.
        _viewModel.Mods.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) StartStagger(); };
        _viewModel.Plan.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) StartStagger(); };

        Loaded += (_, _) => MoveNavIndicator(animate: false);
        SizeChanged += (_, _) => MoveNavIndicator(animate: false);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedPage):
                // Après la mise en page, pour que la position de l'élément sélectionné soit à jour.
                Dispatcher.BeginInvoke(() => MoveNavIndicator(animate: true));
                break;
            case nameof(MainViewModel.IsNotificationOpen):
                if (_viewModel.IsNotificationOpen) ShowToast();
                else HideToast();
                break;
        }
    }

    // ───────────── Indicateur de navigation (comme NavigationView de WinUI) ─────────────

    private void MoveNavIndicator(bool animate)
    {
        var selected = NavItems.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (selected is null || !selected.IsLoaded) return;

        var y = selected.TranslatePoint(new Point(0, (selected.ActualHeight - NavIndicator.Height) / 2), NavHost).Y;
        var (scale, translate) = Anim.Transforms(NavIndicator);
        if (!animate)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = y;
            return;
        }

        // L'indicateur glisse, et s'étire pendant le trajet puis reprend sa taille.
        Anim.Animate(translate, TranslateTransform.YProperty, y, 420);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(1.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), Anim.Standard),
                new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), Anim.Decelerate),
            },
        });
    }

    // ───────────── Notification ─────────────

    private void ShowToast()
    {
        Toast.Visibility = Visibility.Visible;
        var (scale, translate) = Anim.Transforms(Toast);
        Anim.Animate(Toast, OpacityProperty, 1, 250, Anim.Standard, from: 0);
        Anim.Animate(translate, TranslateTransform.XProperty, 0, 550, Anim.Decelerate, from: 60);
        Anim.Animate(scale, ScaleTransform.ScaleXProperty, 1, 550, Anim.Decelerate, from: 0.96);
        Anim.Animate(scale, ScaleTransform.ScaleYProperty, 1, 550, Anim.Decelerate, from: 0.96);
        Anim.Pop(ToastIcon, from: 0.3, ms: 600);
    }

    private void HideToast()
    {
        if (Toast.Visibility != Visibility.Visible) return;
        var (_, translate) = Anim.Transforms(Toast);
        Anim.Animate(translate, TranslateTransform.XProperty, 40, 200, Anim.Standard);
        var fade = new DoubleAnimation { To = 0, Duration = Anim.Ms(200), EasingFunction = Anim.Standard };
        fade.Completed += (_, _) =>
        {
            if (!_viewModel.IsNotificationOpen) Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    // ───────────── Apparition des lignes en cascade ─────────────

    private void StartStagger()
    {
        _staggerUntil = DateTime.Now.AddMilliseconds(1500);
        _staggerIndex = 0;
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (DateTime.Now > _staggerUntil) return;
        var index = Math.Min(_staggerIndex++, 25);
        e.Row.Opacity = 0;
        Anim.Reveal(e.Row, delayMs: index * 22, offset: 12, ms: 380);
    }

    // ───────────── Glisser-déposer (fichier ou dossier) ─────────────

    private static string? DroppedModpack(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths) return null;
        var path = paths[0];
        return Directory.Exists(path) || Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".mrpack" ? path : null;
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        if (DroppedModpack(e) is null || _viewModel.IsBusy || DropOverlay.Visibility == Visibility.Visible) return;
        DropOverlay.Visibility = Visibility.Visible;
        Anim.Animate(DropOverlay, OpacityProperty, 1, 200, Anim.Standard, from: 0);
        var (scale, _) = Anim.Transforms(DropCard);
        Anim.Animate(scale, ScaleTransform.ScaleXProperty, 1, 450, Anim.Spring, from: 0.85);
        Anim.Animate(scale, ScaleTransform.ScaleYProperty, 1, 450, Anim.Spring, from: 0.85);

        // L'icône "respire" tant que le fichier survole la fenêtre.
        var (iconScale, _) = Anim.Transforms(DropIcon);
        var pulse = new DoubleAnimation(1, 1.15, Anim.Ms(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Anim.Standard };
        iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void OnOverlayDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedModpack(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnOverlayDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave est aussi levé en survolant un enfant : on ne ferme que si le curseur quitte vraiment la fenêtre.
        var position = e.GetPosition(DropOverlay);
        if (position.X > 0 && position.Y > 0 && position.X < DropOverlay.ActualWidth && position.Y < DropOverlay.ActualHeight) return;
        HideDropOverlay();
    }

    private async void OnOverlayDrop(object sender, DragEventArgs e)
    {
        var path = DroppedModpack(e);
        HideDropOverlay();
        if (path is not null) await _viewModel.ImportAsync(path);
    }

    private void HideDropOverlay()
    {
        var fade = new DoubleAnimation { To = 0, Duration = Anim.Ms(180), EasingFunction = Anim.Standard };
        fade.Completed += (_, _) =>
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            DropOverlay.BeginAnimation(OpacityProperty, null);
        };
        DropOverlay.BeginAnimation(OpacityProperty, fade);
    }
}
