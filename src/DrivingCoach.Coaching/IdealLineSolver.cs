using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Coaching;

/// <summary>
/// Rechnet aus einer gelernten Streckenkarte die Linie mit der geringsten
/// Krümmung und das Tempo, das darauf möglich wäre.
/// </summary>
/// <remarks>
/// <para>
/// Die Idee dahinter ist die klassische: Von allen Linien, die in den Korridor
/// passen, ist die mit der kleinsten Krümmung die schnellste, denn die Krümmung
/// begrenzt über <c>v² = a_quer / κ</c> das mögliche Tempo. Das ist nicht ganz
/// die theoretisch schnellste Linie – die würde Bremsen und Beschleunigen
/// mitoptimieren und fiele etwas später in die Kurve –, aber sie liegt sehr nah
/// daran und ist für einen Anfänger ohnehin die bessere Vorlage: gleichmäßig
/// statt trickreich.
/// </para>
/// <para>
/// Gelöst wird abschnittsweise: Jeder Punkt wandert dorthin, wo er den Knick zu
/// seinen Nachbarn am kleinsten macht, und wird dann in den Korridor
/// zurückgeholt. Das über einige hundert Durchläufe, bis sich nichts mehr
/// bewegt. Kein Gleichungssystem, keine Fremdbibliothek.
/// </para>
/// </remarks>
public static class IdealLineSolver
{
    /// <summary>
    /// Abstand, den die Linie zum Streckenrand hält.
    /// </summary>
    /// <remarks>
    /// Die Linie beschreibt die Fahrzeugmitte. Ohne diesen Abstand hinge das
    /// halbe Auto im Gras, und der Rand der Karte ist ohnehin nur geschätzt.
    /// </remarks>
    public const float SafetyMargin = TrackMap.CarHalfWidth + 0.3f;

    /// <summary>Standardanzahl der Durchläufe. Darunter bleiben Haarnadeln eckig.</summary>
    public const int DefaultIterations = 600;

    /// <summary>
    /// Dämpfung je Durchlauf. Über 1 würde die Linie um die Lösung herum
    /// aufschwingen, deutlich darunter dauert es unnötig lange.
    /// </summary>
    private const float Relaxation = 0.6f;

    /// <summary>
    /// Berechnet die Ideallinie. Gibt <c>null</c> zurück, wenn die Karte dafür
    /// noch nicht genug hergibt.
    /// </summary>
    public static IdealLine? Solve(TrackMap map, GripEstimate grip, float topSpeed, int iterations = DefaultIterations)
    {
        int count = map.Count;
        if (!map.IsUsable || count < 16)
        {
            return null;
        }

        var normals = new (float X, float Z)[count];
        for (int bin = 0; bin < count; bin++)
        {
            normals[bin] = map.RightNormalAt(bin);
        }

        (float[] low, float[] high) = Bounds(map);

        var offsets = new float[count];
        for (int bin = 0; bin < count; bin++)
        {
            offsets[bin] = Math.Clamp(0f, low[bin], high[bin]);
        }

        WorldPoint[] points = BuildPoints(map, normals, offsets);
        Smooth(map, normals, low, high, offsets, points, iterations);

        float[] steps = StepLengths(points);
        float[] curvature = Curvatures(points);

        float[] limits = SpeedLimits(curvature, grip.Lateral, topSpeed);
        float[] speeds = SpeedProfileSolver.Solve(limits, steps, grip.Forward, grip.Braking);

        return new IdealLine
        {
            BinSize = map.BinSize,
            Offset = offsets,
            Points = points,
            Curvature = curvature,
            TargetSpeed = speeds,
            StepLength = steps,
            EstimatedLapTime = LapTime(steps, speeds),
        };
    }

    /// <summary>
    /// Der Bereich, in dem die Fahrzeugmitte liegen darf.
    /// </summary>
    /// <remarks>
    /// Wo der Korridor schmaler ist als das Auto plus Sicherheitsabstand, bleibt
    /// nur die Mitte übrig. Lieber ein fester Punkt als ein negativer Spielraum,
    /// in dem der Löser beliebig weit nach außen rutschen könnte.
    /// </remarks>
    private static (float[] Low, float[] High) Bounds(TrackMap map)
    {
        int count = map.Count;
        var low = new float[count];
        var high = new float[count];

        for (int bin = 0; bin < count; bin++)
        {
            float left = map.EdgeLeft[bin] + SafetyMargin;
            float right = map.EdgeRight[bin] - SafetyMargin;

            if (left > right)
            {
                float middle = (map.EdgeLeft[bin] + map.EdgeRight[bin]) / 2f;
                left = middle;
                right = middle;
            }

            low[bin] = left;
            high[bin] = right;
        }

        return (low, high);
    }

    /// <summary>
    /// Schiebt die Punkte so lange quer, bis die Linie so gerade wie möglich
    /// durch den Korridor läuft.
    /// </summary>
    /// <remarks>
    /// Für einen Punkt allein lässt sich das Optimum direkt hinschreiben: Die
    /// Ableitung der Summe der quadrierten Knicke nach seinem Versatz ist
    /// linear, und der Vorfaktor ist immer 6 – das ist <c>1 + 4 + 1</c> aus den
    /// drei zweiten Differenzen, in denen der Punkt vorkommt.
    /// </remarks>
    private static void Smooth(
        TrackMap map,
        (float X, float Z)[] normals,
        float[] low,
        float[] high,
        float[] offsets,
        WorldPoint[] points,
        int iterations)
    {
        int count = offsets.Length;

        for (int pass = 0; pass < iterations; pass++)
        {
            float movement = 0f;

            for (int bin = 0; bin < count; bin++)
            {
                (float X, float Z) normal = normals[bin];

                // Vierte Differenz der Linie, projiziert auf die Querrichtung:
                // der Gradient des Knickmaßes an dieser Stelle.
                float gradient =
                    Bend(points, bin - 1, normal) -
                    (2f * Bend(points, bin, normal)) +
                    Bend(points, bin + 1, normal);

                float updated = Math.Clamp(offsets[bin] - (Relaxation * gradient / 6f), low[bin], high[bin]);
                movement += MathF.Abs(updated - offsets[bin]);

                offsets[bin] = updated;
                points[bin] = Point(map, normals, offsets, bin);
            }

            // Bewegt sich über die ganze Runde weniger als ein Millimeter,
            // bringen weitere Durchläufe nichts mehr.
            if (movement < 0.001f)
            {
                return;
            }
        }
    }

    /// <summary>Zweite Differenz an einer Stelle, auf eine Richtung projiziert.</summary>
    private static float Bend(WorldPoint[] points, int bin, (float X, float Z) direction)
    {
        int count = points.Length;
        WorldPoint previous = points[Wrap(bin - 1, count)];
        WorldPoint current = points[Wrap(bin, count)];
        WorldPoint next = points[Wrap(bin + 1, count)];

        float x = previous.X - (2f * current.X) + next.X;
        float z = previous.Z - (2f * current.Z) + next.Z;

        return (x * direction.X) + (z * direction.Z);
    }

    private static WorldPoint Point(TrackMap map, (float X, float Z)[] normals, float[] offsets, int bin) =>
        new(
            map.CentreX[bin] + (normals[bin].X * offsets[bin]),
            map.CentreY[bin],
            map.CentreZ[bin] + (normals[bin].Z * offsets[bin]));

    private static WorldPoint[] BuildPoints(TrackMap map, (float X, float Z)[] normals, float[] offsets)
    {
        var points = new WorldPoint[offsets.Length];
        for (int bin = 0; bin < points.Length; bin++)
        {
            points[bin] = Point(map, normals, offsets, bin);
        }

        return points;
    }

    /// <summary>Weg von jedem Punkt zum nächsten.</summary>
    private static float[] StepLengths(WorldPoint[] points)
    {
        var steps = new float[points.Length];
        for (int bin = 0; bin < points.Length; bin++)
        {
            steps[bin] = MathF.Max(points[bin].FlatDistanceTo(points[(bin + 1) % points.Length]), 0.01f);
        }

        return steps;
    }

    /// <summary>
    /// Krümmung aus dem Umkreis dreier aufeinanderfolgender Punkte.
    /// </summary>
    /// <remarks>
    /// Nicht über die zweite Differenz, denn die Punkte liegen nicht gleich
    /// weit auseinander – wer innen abkürzt, legt zwischen zwei Abschnitten
    /// weniger Weg zurück. Der Umkreis kommt damit klar.
    /// </remarks>
    private static float[] Curvatures(WorldPoint[] points)
    {
        int count = points.Length;
        var curvature = new float[count];

        for (int bin = 0; bin < count; bin++)
        {
            WorldPoint a = points[Wrap(bin - 1, count)];
            WorldPoint b = points[bin];
            WorldPoint c = points[Wrap(bin + 1, count)];

            float abx = b.X - a.X;
            float abz = b.Z - a.Z;
            float bcx = c.X - b.X;
            float bcz = c.Z - b.Z;

            float ab = MathF.Sqrt((abx * abx) + (abz * abz));
            float bc = MathF.Sqrt((bcx * bcx) + (bcz * bcz));
            float ac = a.FlatDistanceTo(c);

            if (ab < 1e-3f || bc < 1e-3f || ac < 1e-3f)
            {
                curvature[bin] = 0f;
                continue;
            }

            // Kreuzprodukt in der Ebene. Negativ heißt Rechtskurve – dieselbe
            // Orientierung wie die Querrichtung nach rechts.
            float cross = (abx * bcz) - (abz * bcx);
            curvature[bin] = -2f * cross / (ab * bc * ac);
        }

        return curvature;
    }

    /// <summary>Tempo, das die Querbeschleunigung an jeder Stelle zulässt.</summary>
    private static float[] SpeedLimits(float[] curvature, float lateralGrip, float topSpeed)
    {
        var limits = new float[curvature.Length];

        for (int bin = 0; bin < limits.Length; bin++)
        {
            float bend = MathF.Abs(curvature[bin]);

            // Auf der Geraden begrenzt nicht die Kurve, sondern das Auto. Was
            // es hergibt, weiß der Coach nur aus dem, was schon gefahren wurde.
            limits[bin] = bend < 1e-5f
                ? topSpeed
                : MathF.Min(topSpeed, MathF.Sqrt(lateralGrip / bend));
        }

        return limits;
    }

    private static float LapTime(float[] steps, float[] speeds)
    {
        float time = 0f;
        for (int bin = 0; bin < steps.Length; bin++)
        {
            time += steps[bin] / MathF.Max(speeds[bin], 1f);
        }

        return time;
    }

    private static int Wrap(int index, int count)
    {
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }
}
