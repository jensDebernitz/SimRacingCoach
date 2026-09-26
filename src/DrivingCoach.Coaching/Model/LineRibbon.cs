using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching.Model;

/// <summary>Ein Querschnitt des gezeichneten Bandes, schon auf dem Bildschirm.</summary>
/// <param name="Left">Linker Rand in Pixeln.</param>
/// <param name="Right">Rechter Rand in Pixeln.</param>
/// <param name="Distance">Entfernung vor dem Auto, in Metern.</param>
/// <param name="Opacity">Deckkraft 0..1; in der Ferne blasser.</param>
public readonly record struct RibbonPoint(ScreenPoint Left, ScreenPoint Right, float Distance, float Opacity);

/// <summary>
/// Macht aus der Ideallinie ein Band auf dem Bildschirm.
/// </summary>
/// <remarks>
/// Bewusst getrennt vom Zeichnen: Die Rechnung lässt sich so prüfen, ohne ein
/// Fenster zu öffnen.
/// </remarks>
public static class LineRibbon
{
    /// <summary>Wie weit die Linie nach vorn gezeigt wird, in Metern.</summary>
    /// <remarks>
    /// Weiter hilft nicht: Jenseits davon ist die Linie nur noch ein paar Pixel
    /// hoch, und die Abweichung der Kalibrierung wächst mit der Entfernung.
    /// </remarks>
    public const float DefaultLookAhead = 140f;

    /// <summary>Ab hier wird die Linie blasser, bis sie am Ende verschwindet.</summary>
    private const float FadeStart = 70f;

    /// <summary>Die ersten Meter liegen unter der Motorhaube und stören nur.</summary>
    private const float SkipAhead = 4f;

    /// <summary>
    /// Baut das Band ab der aktuellen Position nach vorn.
    /// </summary>
    /// <param name="line">Die berechnete Linie.</param>
    /// <param name="camera">Die aufgestellte Kamera.</param>
    /// <param name="lapDistance">Wo das Auto gerade steht, in Metern.</param>
    /// <param name="halfWidth">Halbe Breite des Bandes in Metern.</param>
    /// <param name="lookAhead">Sichtweite in Metern.</param>
    public static List<RibbonPoint> Build(
        IdealLine line,
        TrackCamera camera,
        float lapDistance,
        float halfWidth = 0.35f,
        float lookAhead = DefaultLookAhead)
    {
        var ribbon = new List<RibbonPoint>();
        if (line.Count == 0 || line.BinSize <= 0f)
        {
            return ribbon;
        }

        int steps = (int)(lookAhead / line.BinSize);
        int start = line.BinOf(lapDistance + SkipAhead);

        for (int step = 0; step <= steps; step++)
        {
            int bin = (start + step) % line.Count;
            WorldPoint centre = line.Points[bin];
            (float nx, float nz) = line.RightNormalAt(bin);

            var left = new WorldPoint(centre.X - (nx * halfWidth), centre.Y, centre.Z - (nz * halfWidth));
            var right = new WorldPoint(centre.X + (nx * halfWidth), centre.Y, centre.Z + (nz * halfWidth));

            if (!camera.TryProject(left, out ScreenPoint leftScreen) ||
                !camera.TryProject(right, out ScreenPoint rightScreen))
            {
                // Der erste Punkt kann noch hinter der Kamera liegen; sobald das
                // Band einmal angefangen hat, endet es hier. Ein Loch in der
                // Mitte und dann eine Fortsetzung sähe aus wie zwei Linien.
                if (ribbon.Count > 0)
                {
                    break;
                }

                continue;
            }

            float distance = leftScreen.Distance;
            ribbon.Add(new RibbonPoint(leftScreen, rightScreen, distance, OpacityAt(distance, lookAhead)));
        }

        return ribbon;
    }

    /// <summary>
    /// Deckkraft über der Entfernung.
    /// </summary>
    /// <remarks>
    /// Das Ausblenden ist nicht nur Kosmetik: In der Ferne laufen die Ränder des
    /// Bandes auf wenige Pixel zusammen und flimmern. Blass fällt das nicht auf.
    /// </remarks>
    private static float OpacityAt(float distance, float lookAhead)
    {
        if (distance <= FadeStart)
        {
            return 1f;
        }

        float span = MathF.Max(lookAhead - FadeStart, 1f);
        return Math.Clamp(1f - ((distance - FadeStart) / span), 0f, 1f);
    }
}
