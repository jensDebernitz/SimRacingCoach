using System.Windows;
using System.Windows.Media;

namespace DrivingCoach.Overlay.Controls;

/// <summary>
/// Der Delta-Balken: wächst von der Mitte nach links, wenn die laufende Runde
/// schneller ist, und nach rechts, wenn sie langsamer ist.
/// </summary>
/// <remarks>
/// Die Richtung ist mit Bedacht gewählt: links/grün bedeutet Zeitgewinn, weil
/// der Blick beim Fahren nur Farbe und Richtung erfasst, keine Vorzeichen.
/// </remarks>
public sealed class DeltaBar : FrameworkElement
{
    private static readonly Brush Backdrop = Frozen(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
    private static readonly Brush FasterBrush = Frozen(Color.FromRgb(0x36, 0xC7, 0x5A));
    private static readonly Brush SlowerBrush = Frozen(Color.FromRgb(0xE8, 0x3A, 0x2F));
    private static readonly Brush IdleBrush = Frozen(Color.FromArgb(0x60, 0x9A, 0xA4, 0xB0));
    private static readonly Pen CentrePen = FrozenPen(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 1.2);
    private static readonly Pen TickPen = FrozenPen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 1.0);

    public static readonly DependencyProperty DeltaProperty = DependencyProperty.Register(
        nameof(Delta),
        typeof(double),
        typeof(DeltaBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ausschlag in Sekunden, bei dem der Balken den Rand erreicht.</summary>
    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(
        nameof(Range),
        typeof(double),
        typeof(DeltaBar),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>False, solange keine Referenzrunde vorliegt – der Balken bleibt dann grau.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(DeltaBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Delta
    {
        get => (double)GetValue(DeltaProperty);
        set => SetValue(DeltaProperty, value);
    }

    public double Range
    {
        get => (double)GetValue(RangeProperty);
        set => SetValue(RangeProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        double centre = width / 2;
        double radius = height / 4;

        context.DrawRoundedRectangle(Backdrop, null, new Rect(0, 0, width, height), radius, radius);

        // Marken bei einem Viertel und der Hälfte des Ausschlags.
        foreach (double fraction in (ReadOnlySpan<double>)[0.25, 0.5, 0.75])
        {
            double dx = fraction * centre;
            context.DrawLine(TickPen, new Point(centre - dx, height * 0.25), new Point(centre - dx, height * 0.75));
            context.DrawLine(TickPen, new Point(centre + dx, height * 0.25), new Point(centre + dx, height * 0.75));
        }

        if (IsActive)
        {
            double range = Range <= 0 ? 1.0 : Range;
            double clamped = Math.Clamp(Delta / range, -1.0, 1.0);
            double length = Math.Abs(clamped) * centre;

            if (length > 0.5)
            {
                // Negatives Delta heißt schneller: der Balken geht nach links.
                double left = clamped < 0 ? centre - length : centre;
                Brush brush = clamped < 0 ? FasterBrush : SlowerBrush;

                context.DrawRoundedRectangle(
                    brush,
                    null,
                    new Rect(left, height * 0.18, length, height * 0.64),
                    radius / 2,
                    radius / 2);
            }
        }
        else
        {
            context.DrawRoundedRectangle(
                IdleBrush,
                null,
                new Rect(centre - 1.5, height * 0.18, 3, height * 0.64),
                1.5,
                1.5);
        }

        context.DrawLine(CentrePen, new Point(centre, 0), new Point(centre, height));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
