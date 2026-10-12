using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ModpackMerger.App.Animations;

/// <summary>
/// Animations réutilisables façon Windows 11 (courbes "decelerate" de Fluent), déclarées en XAML via des propriétés attachées :
/// <list type="bullet">
/// <item><c>Entrance</c> / <c>EntranceDelay</c> : apparition (fondu + glissement) au chargement, échelonnable.</item>
/// <item><c>RevealOnVisible</c> : même animation à chaque fois que l'élément redevient visible (pages, panneaux).</item>
/// <item><c>PopOnVisible</c> : apparition avec léger rebond (pastilles, coches).</item>
/// <item><c>HoverLift</c> : la carte se soulève et son ombre grandit au survol.</item>
/// <item><c>PopValue</c> : pulsation quand la valeur liée change (compteurs).</item>
/// <item><c>SmoothValue</c> : barre de progression qui glisse vers la nouvelle valeur.</item>
/// <item><c>Float</c> : flottement continu (illustrations des états vides).</item>
/// </list>
/// </summary>
public static class Anim
{
    public static readonly IEasingFunction Decelerate = Freeze(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 });
    public static readonly IEasingFunction Standard = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });

    private static IEasingFunction Freeze(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }

    public static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(ms));

    /// <summary>Garantit un groupe Scale + Translate modifiable sur l'élément et le renvoie.</summary>
    public static (ScaleTransform Scale, TranslateTransform Translate) Transforms(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] } && !s.IsFrozen)
            return (s, t);

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform();
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, translate);
    }

    public static void Animate(Animatable target, DependencyProperty property, double to, double ms, IEasingFunction? easing = null, double? from = null, double delayMs = 0)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = Ms(ms),
            EasingFunction = easing ?? Decelerate,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        if (from.HasValue) animation.From = from.Value;
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Animate(UIElement target, DependencyProperty property, double to, double ms, IEasingFunction? easing = null, double? from = null, double delayMs = 0)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = Ms(ms),
            EasingFunction = easing ?? Decelerate,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        if (from.HasValue) animation.From = from.Value;
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Apparition Fluent : fondu + glissement vertical, avec délai optionnel.</summary>
    public static void Reveal(UIElement element, double delayMs = 0, double offset = 24, double ms = 500)
    {
        var (_, translate) = Transforms(element);
        Animate(element, UIElement.OpacityProperty, 1, ms * 0.8, Standard, from: 0, delayMs: delayMs);
        Animate(translate, TranslateTransform.YProperty, 0, ms, Decelerate, from: offset, delayMs: delayMs);
    }

    /// <summary>Petit rebond d'échelle (0.6 → 1).</summary>
    public static void Pop(UIElement element, double from = 0.6, double ms = 420)
    {
        var (scale, _) = Transforms(element);
        Animate(element, UIElement.OpacityProperty, 1, ms * 0.5, Standard, from: 0);
        Animate(scale, ScaleTransform.ScaleXProperty, 1, ms, Spring, from: from);
        Animate(scale, ScaleTransform.ScaleYProperty, 1, ms, Spring, from: from);
    }

    // ───────────── Entrance ─────────────

    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnEntranceChanged));

    public static bool GetEntrance(DependencyObject d) => (bool)d.GetValue(EntranceProperty);
    public static void SetEntrance(DependencyObject d, bool value) => d.SetValue(EntranceProperty, value);

    public static readonly DependencyProperty EntranceDelayProperty = DependencyProperty.RegisterAttached(
        "EntranceDelay", typeof(double), typeof(Anim), new PropertyMetadata(0d));

    public static double GetEntranceDelay(DependencyObject d) => (double)d.GetValue(EntranceDelayProperty);
    public static void SetEntranceDelay(DependencyObject d, double value) => d.SetValue(EntranceDelayProperty, value);

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.Opacity = 0;
        element.Loaded += (_, _) => Reveal(element, GetEntranceDelay(element));
    }

    // ───────────── RevealOnVisible ─────────────

    public static readonly DependencyProperty RevealOnVisibleProperty = DependencyProperty.RegisterAttached(
        "RevealOnVisible", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnRevealOnVisibleChanged));

    public static bool GetRevealOnVisible(DependencyObject d) => (bool)d.GetValue(RevealOnVisibleProperty);
    public static void SetRevealOnVisible(DependencyObject d, bool value) => d.SetValue(RevealOnVisibleProperty, value);

    private static void OnRevealOnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) Reveal(element, GetEntranceDelay(element), offset: 32, ms: 450);
        };
    }

    // ───────────── PopOnVisible ─────────────

    public static readonly DependencyProperty PopOnVisibleProperty = DependencyProperty.RegisterAttached(
        "PopOnVisible", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnPopOnVisibleChanged));

    public static bool GetPopOnVisible(DependencyObject d) => (bool)d.GetValue(PopOnVisibleProperty);
    public static void SetPopOnVisible(DependencyObject d, bool value) => d.SetValue(PopOnVisibleProperty, value);

    private static void OnPopOnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) Pop(element);
        };
    }

    // ───────────── HoverLift ─────────────

    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject d, bool value) => d.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        var shadow = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Direction = 270, Opacity = 0.10, Color = Colors.Black };
        element.Effect = shadow;
        element.MouseEnter += (_, _) =>
        {
            Animate(Transforms(element).Translate, TranslateTransform.YProperty, -3, 250);
            Animate(shadow, DropShadowEffect.BlurRadiusProperty, 22, 250);
            Animate(shadow, DropShadowEffect.ShadowDepthProperty, 5, 250);
            Animate(shadow, DropShadowEffect.OpacityProperty, 0.22, 250);
        };
        element.MouseLeave += (_, _) =>
        {
            Animate(Transforms(element).Translate, TranslateTransform.YProperty, 0, 350);
            Animate(shadow, DropShadowEffect.BlurRadiusProperty, 8, 350);
            Animate(shadow, DropShadowEffect.ShadowDepthProperty, 1, 350);
            Animate(shadow, DropShadowEffect.OpacityProperty, 0.10, 350);
        };
    }

    // ───────────── PopValue ─────────────

    public static readonly DependencyProperty PopValueProperty = DependencyProperty.RegisterAttached(
        "PopValue", typeof(object), typeof(Anim), new PropertyMetadata(null, OnPopValueChanged));

    public static object? GetPopValue(DependencyObject d) => d.GetValue(PopValueProperty);
    public static void SetPopValue(DependencyObject d, object? value) => d.SetValue(PopValueProperty, value);

    private static void OnPopValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.OldValue is null || !element.IsVisible) return;
        var (scale, _) = Transforms(element);
        Animate(scale, ScaleTransform.ScaleXProperty, 1, 380, Spring, from: 1.25);
        Animate(scale, ScaleTransform.ScaleYProperty, 1, 380, Spring, from: 1.25);
    }

    // ───────────── SmoothValue ─────────────

    public static readonly DependencyProperty SmoothValueProperty = DependencyProperty.RegisterAttached(
        "SmoothValue", typeof(double), typeof(Anim), new PropertyMetadata(0d, OnSmoothValueChanged));

    public static double GetSmoothValue(DependencyObject d) => (double)d.GetValue(SmoothValueProperty);
    public static void SetSmoothValue(DependencyObject d, double value) => d.SetValue(SmoothValueProperty, value);

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RangeBase range) return;
        var to = (double)e.NewValue;
        // Retour à zéro (nouvelle opération) : instantané, pas de barre qui "recule".
        if (to < range.Value) range.BeginAnimation(RangeBase.ValueProperty, null);
        Animate(range, RangeBase.ValueProperty, to, to < range.Value ? 1 : 400);
    }

    // ───────────── Float ─────────────

    public static readonly DependencyProperty FloatProperty = DependencyProperty.RegisterAttached(
        "Float", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnFloatChanged));

    public static bool GetFloat(DependencyObject d) => (bool)d.GetValue(FloatProperty);
    public static void SetFloat(DependencyObject d, bool value) => d.SetValue(FloatProperty, value);

    private static void OnFloatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.Loaded += (_, _) =>
        {
            var (_, translate) = Transforms(element);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = 0,
                To = -8,
                Duration = Ms(1800),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        };
    }

    // ───────────── Pression des boutons (appliquée globalement par App) ─────────────

    /// <summary>Tous les boutons s'enfoncent légèrement au clic, comme dans WinUI.</summary>
    public static void RegisterButtonPressFeedback()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent,
            new RoutedEventHandler((s, _) => Press((UIElement)s, 0.95)), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent,
            new RoutedEventHandler((s, _) => Press((UIElement)s, 1)), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent,
            new RoutedEventHandler((s, _) => Press((UIElement)s, 1)), handledEventsToo: true);
    }

    private static void Press(UIElement element, double scale)
    {
        // Les cases à cocher des grilles et les RadioButton de navigation ont leur propre animation.
        if (element is ToggleButton) return;
        var (s, _) = Transforms(element);
        if (scale == 1 && s.ScaleX == 1) return;
        Animate(s, ScaleTransform.ScaleXProperty, scale, scale < 1 ? 90 : 300, scale < 1 ? Standard : Spring);
        Animate(s, ScaleTransform.ScaleYProperty, scale, scale < 1 ? 90 : 300, scale < 1 ? Standard : Spring);
    }
}
