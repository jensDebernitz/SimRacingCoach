using System.Windows;
using System.Windows.Media;

namespace DrivingCoach.Overlay.Controls;

/// <summary>
/// Zeichnet den Verlauf von Gas, Bremse und Lenkung der letzten Sekunden.
/// </summary>
/// <remarks>
/// Selbst gezeichnet statt aus WPF-Formen zusammengesetzt: 180 Datenpunkte
/// mal drei Kanäle wären 540 <c>Line</c>-Elemente, die der Layout-Durchlauf
/// 30-mal pro Sekunde anfassen müsste. Ein <see cref="StreamGeometry"/> je
/// Kanal kostet dagegen fast nichts.
/// </remarks>
public sealed class InputTrace : FrameworkElement
{
    private static readonly Pen ThrottlePen = Frozen(Color.FromRgb(0x4C, 0xD1, 0x64), 1.6);
    private static readonly Pen BrakePen = Frozen(Color.FromRgb(0xFF, 0x5B, 0x4A), 1.6);
    private static readonly Pen SteeringPen = Frozen(Color.FromRgb(0x63, 0xB3, 0xFF), 1.3);
    private static readonly Pen GridPen = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 1.0);
    private static readonly Brush Backdrop = Frozen(Color.FromArgb(0x40, 0x00, 0x00, 0x00));

    public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(
        nameof(History),
        typeof(InputHistory),
        typeof(InputTrace),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnHistoryChanged));

    public InputHistory? History
    {
        get => (InputHistory?)GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    private static void OnHistoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var trace = (InputTrace)d;

        if (e.OldValue is InputHistory old)
        {
            old.Updated -= trace.OnHistoryUpdated;
        }

        if (e.NewValue is InputHistory added)
        {
            added.Updated += trace.OnHistoryUpdated;
        }
    }

    private void OnHistoryUpdated() => InvalidateVisual();

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        context.DrawRoundedRectangle(Backdrop, null, new Rect(0, 0, width, height), 3, 3);

        // Die Pedalspur nimmt die oberen zwei Drittel, die Lenkung das untere.
        double pedalHeight = height * 0.66;
        double steeringCentre = pedalHeight + (height - pedalHeight) / 2;

        context.DrawLine(GridPen, new Point(0, pedalHeight), new Point(width, pedalHeight));
        context.DrawLine(GridPen, new Point(0, steeringCentre), new Point(width, steeringCentre));

        InputHistory? history = History;
        if (history is null || history.Count < 2)
        {
            return;
        }

        // Über die volle Breite, auch wenn der Puffer noch nicht voll ist –
        // sonst wandert die Kurve beim Start von links nach rechts.
        double step = width / (history.Capacity - 1);
        int offset = history.Capacity - history.Count;

        context.DrawGeometry(null, ThrottlePen, BuildPedal(history, history.Throttle, offset, step, pedalHeight));
        context.DrawGeometry(null, BrakePen, BuildPedal(history, history.Brake, offset, step, pedalHeight));
        context.DrawGeometry(
            null,
            SteeringPen,
            BuildSteering(history, offset, step, steeringCentre, (height - pedalHeight) / 2));
    }

    /// <summary>0 liegt auf der Grundlinie, 1 ganz oben.</summary>
    private static StreamGeometry BuildPedal(
        InputHistory history,
        Func<int, float> channel,
        int offset,
        double step,
        double baseline)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point((offset) * step, baseline - Math.Clamp(channel(0), 0f, 1f) * baseline), false, false);

            for (int i = 1; i < history.Count; i++)
            {
                double value = Math.Clamp(channel(i), 0f, 1f);
                ctx.LineTo(new Point((offset + i) * step, baseline - value * baseline), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Die Lenkung schwingt um eine Mittellinie, -1 unten, +1 oben.</summary>
    private static StreamGeometry BuildSteering(
        InputHistory history,
        int offset,
        double step,
        double centre,
        double amplitude)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(offset * step, centre - Math.Clamp(history.Steering(0), -1f, 1f) * amplitude), false, false);

            for (int i = 1; i < history.Count; i++)
            {
                double value = Math.Clamp(history.Steering(i), -1f, 1f);
                ctx.LineTo(new Point((offset + i) * step, centre - value * amplitude), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Pen Frozen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
