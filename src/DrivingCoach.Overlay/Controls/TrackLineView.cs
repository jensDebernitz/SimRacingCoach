using System.Windows;
using System.Windows.Media;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Overlay.Controls;

/// <summary>
/// Zeichnet das Band der Ideallinie über das Bild des Spiels.
/// </summary>
/// <remarks>
/// <para>
/// Ein einziges Polygon statt eines Vierecks je Abschnitt: Aneinandergesetzte
/// Vierecke hinterlassen an jeder gemeinsamen Kante einen feinen hellen
/// Strich, weil die Kantenglättung beide Seiten halbdurchsichtig zeichnet. Bei
/// dreißig Abschnitten wäre das Band durchgehend gestreift.
/// </para>
/// <para>
/// Das Ausblenden in die Ferne übernimmt deshalb ein Farbverlauf entlang des
/// Bandes statt eine eigene Deckkraft je Abschnitt.
/// </para>
/// </remarks>
public sealed class TrackLineView : FrameworkElement
{
    /// <summary>
    /// Ein Türkis, das auf grauem Asphalt und auf grünen Randstreifen gleich
    /// gut steht und mit keiner Warnfarbe des Spiels verwechselt werden kann.
    /// </summary>
    private static readonly Color BandColour = Color.FromRgb(0x36, 0xD6, 0xFF);

    /// <summary>Dunkler Saum, damit das Band auch in der Sonne noch abhebt.</summary>
    private static readonly Color EdgeColour = Color.FromRgb(0x02, 0x1C, 0x26);

    private const double BandOpacity = 0.5;
    private const double EdgeOpacity = 0.75;
    private const double EdgeThickness = 1.5;

    private IReadOnlyList<RibbonPoint> _ribbon = [];

    /// <summary>Übernimmt das nächste Band und fordert ein neues Bild an.</summary>
    /// <remarks>
    /// Zweimal nacheinander nichts zu zeichnen kommt bei jeder Boxeneinfahrt
    /// und in jedem Menü vor. Ein zweites Neuzeichnen des ganzen Bildschirms
    /// dafür wäre verschenkte Bildrate – und die fehlt dann im Spiel.
    /// </remarks>
    public void Show(IReadOnlyList<RibbonPoint> ribbon)
    {
        if (_ribbon.Count == 0 && ribbon.Count == 0)
        {
            return;
        }

        _ribbon = ribbon;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        IReadOnlyList<RibbonPoint> ribbon = _ribbon;

        // Ein einzelner Querschnitt ergibt noch keine Fläche.
        if (ribbon.Count < 2)
        {
            return;
        }

        var pen = new Pen(Fade(ribbon, EdgeColour, EdgeOpacity), EdgeThickness);
        pen.Freeze();

        context.DrawGeometry(Fade(ribbon, BandColour, BandOpacity), pen, BuildBand(ribbon));
    }

    /// <summary>
    /// Der Umriss des Bandes: den linken Rand nach vorn, den rechten zurück.
    /// </summary>
    private static StreamGeometry BuildBand(IReadOnlyList<RibbonPoint> ribbon)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(At(ribbon[0].Left), isFilled: true, isClosed: true);

            for (int i = 1; i < ribbon.Count; i++)
            {
                ctx.LineTo(At(ribbon[i].Left), isStroked: true, isSmoothJoin: false);
            }

            for (int i = ribbon.Count - 1; i >= 0; i--)
            {
                ctx.LineTo(At(ribbon[i].Right), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Ein Verlauf, der die Deckkraft der einzelnen Querschnitte nachbildet.
    /// </summary>
    /// <remarks>
    /// Die Achse läuft vom vordersten zum hintersten Querschnitt. Wo ein
    /// Querschnitt auf dieser Achse liegt, entscheidet nicht seine Entfernung
    /// in Metern, sondern seine Lage im Bild – perspektivisch rücken die
    /// hinteren Querschnitte eng zusammen, und genauso eng müssen die
    /// Haltepunkte des Verlaufs stehen.
    /// </remarks>
    private static Brush Fade(IReadOnlyList<RibbonPoint> ribbon, Color colour, double opacity)
    {
        Point near = Middle(ribbon[0]);
        Point far = Middle(ribbon[^1]);

        double axisX = far.X - near.X;
        double axisY = far.Y - near.Y;
        double lengthSquared = (axisX * axisX) + (axisY * axisY);

        // Ein Band, das im Bild auf einen Punkt zusammenfällt – etwa direkt vor
        // einer Kuppe. Ein Verlauf ohne Länge ist nicht definiert.
        if (lengthSquared < 1.0)
        {
            return Frozen(colour, opacity * ribbon[0].Opacity);
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = near,
            EndPoint = far,
            MappingMode = BrushMappingMode.Absolute,
        };

        foreach (RibbonPoint point in ribbon)
        {
            Point middle = Middle(point);
            double along =
                (((middle.X - near.X) * axisX) + ((middle.Y - near.Y) * axisY)) / lengthSquared;

            brush.GradientStops.Add(new GradientStop(
                Shaded(colour, opacity * point.Opacity),
                Math.Clamp(along, 0.0, 1.0)));
        }

        brush.Freeze();
        return brush;
    }

    private static Point Middle(RibbonPoint point) =>
        new((point.Left.X + point.Right.X) / 2.0, (point.Left.Y + point.Right.Y) / 2.0);

    private static Point At(ScreenPoint point) => new(point.X, point.Y);

    private static Color Shaded(Color colour, double opacity) =>
        Color.FromArgb((byte)(Math.Clamp(opacity, 0.0, 1.0) * 255.0), colour.R, colour.G, colour.B);

    private static Brush Frozen(Color colour, double opacity)
    {
        var brush = new SolidColorBrush(Shaded(colour, opacity));
        brush.Freeze();
        return brush;
    }
}
