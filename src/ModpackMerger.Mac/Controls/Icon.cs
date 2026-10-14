using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ModpackMerger.Mac.Controls;

/// <summary>
/// Icône au trait (style SF Symbols) dessinée à partir d'une géométrie dans une grille 24×24.
/// Remplace la police Segoe Fluent Icons, absente de macOS ; la couleur suit le texte environnant.
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.8);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeThicknessProperty);
        WidthProperty.OverrideDefaultValue<Icon>(16);
        HeightProperty.OverrideDefaultValue<Icon>(16);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        if (scale <= 0) return;
        var offset = new Vector((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2);
        var pen = new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
            context.DrawGeometry(null, pen, Data);
    }
}
