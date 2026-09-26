namespace DrivingCoach.Telemetry.Simulation;

/// <summary>Eine Kurve auf der synthetischen Teststrecke.</summary>
/// <param name="EntryDistance">Beginn der Kurve in Metern ab Start/Ziel.</param>
/// <param name="Length">Länge der Kurve in Metern.</param>
/// <param name="ApexSpeed">Mögliche Scheitelpunktgeschwindigkeit in m/s.</param>
/// <param name="Direction">+1 für rechts, -1 für links.</param>
public sealed record SimCorner(float EntryDistance, float Length, float ApexSpeed, int Direction);

/// <summary>
/// Synthetische Strecke für den Simulator. Die Werte sind frei erfunden, aber
/// so gewählt, dass ein realistisches Wechselspiel aus Bremszonen, Scheiteln
/// und Beschleunigungsphasen entsteht.
/// </summary>
public sealed class SimTrack
{
    /// <summary>Auflösung des vorberechneten Geschwindigkeitsprofils in Metern.</summary>
    public const float StepMetres = 2f;

    /// <summary>Längsbeschleunigung des simulierten Fahrzeugs in m/s².</summary>
    public const float MaxAcceleration = 6.5f;

    /// <summary>Verzögerung des simulierten Fahrzeugs in m/s².</summary>
    public const float MaxDeceleration = 16f;

    public SimTrack(string name, float length, IReadOnlyList<SimCorner> corners, float topSpeed)
    {
        Name = name;
        Length = length;
        Corners = corners;
        TopSpeed = topSpeed;
        StepCount = (int)MathF.Ceiling(length / StepMetres);
    }

    public string Name { get; }

    public float Length { get; }

    public IReadOnlyList<SimCorner> Corners { get; }

    /// <summary>Höchstgeschwindigkeit auf der Geraden in m/s.</summary>
    public float TopSpeed { get; }

    public int StepCount { get; }

    /// <summary>Ein Layout mit langsamen, mittleren und schnellen Kurven.</summary>
    public static SimTrack Default { get; } = new(
        "Testkurs",
        length: 4200f,
        corners:
        [
            new SimCorner(520f, 130f, 22f, +1),    // enge Rechtskurve nach der Start-Ziel-Geraden
            new SimCorner(900f, 90f, 30f, -1),     // mittlere Linkskurve
            new SimCorner(1180f, 160f, 45f, +1),   // schnelle Rechtskurve
            new SimCorner(1750f, 110f, 18f, -1),   // Haarnadel
            new SimCorner(2200f, 140f, 38f, +1),
            new SimCorner(2600f, 100f, 26f, -1),
            new SimCorner(3150f, 180f, 52f, +1),   // langgezogene schnelle Kurve
            new SimCorner(3700f, 120f, 24f, -1),   // letzte Kurve vor Start/Ziel
        ],
        topSpeed: 78f);

    /// <summary>
    /// Erzeugt das Tempolimit je Streckenpunkt: in den Kurven die
    /// Scheitelgeschwindigkeit, sonst die Höchstgeschwindigkeit.
    /// </summary>
    /// <param name="cornerSpeedScale">
    /// Faktor je Kurve, mit dem der simulierte Fahrer die mögliche
    /// Scheitelgeschwindigkeit trifft (1.0 = perfekt).
    /// </param>
    public float[] BuildSpeedLimits(IReadOnlyList<float> cornerSpeedScale)
    {
        var limits = new float[StepCount];
        Array.Fill(limits, TopSpeed);

        for (int c = 0; c < Corners.Count; c++)
        {
            SimCorner corner = Corners[c];
            float scale = c < cornerSpeedScale.Count ? cornerSpeedScale[c] : 1f;
            float speed = corner.ApexSpeed * scale;

            int from = (int)(corner.EntryDistance / StepMetres);
            int to = (int)((corner.EntryDistance + corner.Length) / StepMetres);
            for (int i = from; i <= to; i++)
            {
                int index = Wrap(i);
                limits[index] = MathF.Min(limits[index], speed);
            }
        }

        return limits;
    }

    /// <summary>Krümmung (1/Radius) an einem Streckenpunkt, mit Vorzeichen für die Richtung.</summary>
    public float CurvatureAt(float distance)
    {
        foreach (SimCorner corner in Corners)
        {
            float offset = distance - corner.EntryDistance;
            if (offset < 0f || offset > corner.Length)
            {
                continue;
            }

            // Krümmung über die Kurvenlänge weich ein- und ausblenden.
            float phase = offset / corner.Length;
            float shape = MathF.Sin(phase * MathF.PI);
            float radius = corner.ApexSpeed * corner.ApexSpeed / 12f; // a_quer ≈ 12 m/s²
            return corner.Direction * shape / MathF.Max(radius, 1f);
        }

        return 0f;
    }

    /// <summary>Index im Profil, zyklisch über die Rundenlänge.</summary>
    public int Wrap(int index)
    {
        int wrapped = index % StepCount;
        return wrapped < 0 ? wrapped + StepCount : wrapped;
    }

    /// <summary>
    /// Halbe Fahrbahnbreite der synthetischen Strecke in Metern.
    /// </summary>
    /// <remarks>
    /// AMS2 liefert keine Streckenränder; der Coach lernt sie aus gefahrenen
    /// Runden. Damit der Simulator denselben Weg durchspielt, hat auch die
    /// Teststrecke eine Breite – und der simulierte Fahrer darf sie nutzen.
    /// </remarks>
    public const float HalfWidth = 6f;

    private WorldPoint[]? _centreline;
    private float[]? _headings;

    /// <summary>Mittellinie in Streckenkoordinaten, ein Punkt je <see cref="StepMetres"/>.</summary>
    public IReadOnlyList<WorldPoint> Centreline
    {
        get
        {
            Build();
            return _centreline!;
        }
    }

    /// <summary>Blickrichtung der Mittellinie an einem Streckenpunkt, in Radiant.</summary>
    public float HeadingAt(float distance)
    {
        Build();
        return _headings![Wrap((int)MathF.Round(distance / StepMetres))];
    }

    /// <summary>
    /// Ein Punkt neben der Mittellinie. <paramref name="lateralOffset"/> zählt
    /// nach rechts in Fahrtrichtung, so wie <see cref="SimCorner.Direction"/>.
    /// </summary>
    public WorldPoint PointAt(float distance, float lateralOffset = 0f)
    {
        Build();

        float exact = distance / StepMetres;
        int index = Wrap((int)MathF.Floor(exact));
        WorldPoint point = WorldPoint.Lerp(
            _centreline![index], _centreline![Wrap(index + 1)], exact - MathF.Floor(exact));

        float heading = _headings![index];
        return point with
        {
            X = point.X + (MathF.Cos(heading) * lateralOffset),
            Z = point.Z - (MathF.Sin(heading) * lateralOffset),
        };
    }

    /// <summary>
    /// Seitlicher Versatz zur Mittellinie, wie ihn ein Fahrer in Kurven wählt:
    /// innen anlegen, auf der Geraden mittig. Positiv heißt nach rechts.
    /// </summary>
    /// <param name="variation">
    /// Faktor auf die Breite. Fährt jede Runde denselben Versatz, lernt der
    /// Coach eine Strecke ohne Breite – der Korridor entsteht erst daraus, dass
    /// die Runden sich unterscheiden.
    /// </param>
    /// <remarks>
    /// Die Krümmung wird über die letzten Meter geglättet. Die Spitze des
    /// Versatzes liegt dadurch kurz hinter dem geometrischen Scheitel – so wie
    /// bei einem echten Fahrer auch.
    /// </remarks>
    public float RacingOffsetAt(float distance, float variation = 1f)
    {
        float curvature =
            (CurvatureAt(distance) * 0.5f) +
            (CurvatureAt(distance - 20f) * 0.3f) +
            (CurvatureAt(distance - 45f) * 0.2f);

        float inside = Math.Clamp(curvature * 220f, -1f, 1f);
        return inside * HalfWidth * 0.65f * variation;
    }

    /// <summary>
    /// Integriert die Krümmung zu einer Mittellinie.
    /// </summary>
    /// <remarks>
    /// Die Krümmungen der erfundenen Kurven summieren sich nicht auf volle
    /// 360°, die Runde würde sich also nicht schließen. Beide Abweichungen –
    /// im Winkel und in der Lage – werden deshalb gleichmäßig über die Runde
    /// verteilt herausgerechnet. Das verbiegt die Form minimal und macht aus
    /// dem Streckenzug einen geschlossenen Kurs, wie ihn der Coach erwartet.
    /// </remarks>
    private void Build()
    {
        if (_centreline is not null)
        {
            return;
        }

        var headings = new float[StepCount];
        float heading = 0f;

        for (int i = 0; i < StepCount; i++)
        {
            headings[i] = heading;
            heading += CurvatureAt(i * StepMetres) * StepMetres;
        }

        // Winkelfehler gleichmäßig verteilen, damit die Runde rund wird.
        float headingDrift = (heading - (2f * MathF.PI)) / StepCount;
        for (int i = 0; i < StepCount; i++)
        {
            headings[i] -= headingDrift * i;
        }

        var points = new WorldPoint[StepCount];
        float x = 0f;
        float z = 0f;

        for (int i = 0; i < StepCount; i++)
        {
            points[i] = new WorldPoint(x, 0f, z);
            x += MathF.Sin(headings[i]) * StepMetres;
            z += MathF.Cos(headings[i]) * StepMetres;
        }

        // Restlichen Versatz ebenso verteilen – danach trifft der letzte Punkt
        // den ersten.
        for (int i = 0; i < StepCount; i++)
        {
            float share = (float)i / StepCount;
            points[i] = points[i] with { X = points[i].X - (x * share), Z = points[i].Z - (z * share) };
        }

        // Die Blickrichtung zum Schluss aus den fertigen Punkten ablesen, nicht
        // aus der integrierten Krümmung. Das Verteilen des Versatzes verschiebt
        // jeden Punkt ein Stück weit zur Seite, und zwar mit jedem Schritt mehr.
        // Der Streckenzug läuft dadurch messbar anders, als die Winkel behaupten
        // – stellenweise um ein Viertel Radiant. Ein Auto, das in eine Richtung
        // schaut und in eine andere fährt, wäre für alles, was aus Lage und
        // Position rechnet, ein falscher Prüfstein.
        for (int i = 0; i < StepCount; i++)
        {
            WorldPoint from = points[i];
            WorldPoint to = points[(i + 1) % StepCount];
            headings[i] = MathF.Atan2(to.X - from.X, to.Z - from.Z);
        }

        _headings = headings;
        _centreline = points;
    }
}
