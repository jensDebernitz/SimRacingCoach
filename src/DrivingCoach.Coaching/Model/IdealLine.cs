using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Was das Auto kann, aus gefahrenen Runden geschätzt. Alle Werte in m/s².
/// </summary>
/// <param name="Lateral">Querbeschleunigung, die in Kurven erreicht wurde.</param>
/// <param name="Forward">Längsbeschleunigung beim Herausbeschleunigen.</param>
/// <param name="Braking">Verzögerung beim Bremsen.</param>
/// <remarks>
/// Die Schnittstelle nennt weder Reifen noch Abtrieb noch Motorkennlinie. Was
/// das Auto hergibt, lässt sich deshalb nur daran ablesen, was der Fahrer ihm
/// bereits abverlangt hat. Die Schätzung ist damit zwangsläufig konservativ:
/// Sie kennt nur das bisher Erreichte, nicht das Mögliche.
/// </remarks>
public readonly record struct GripEstimate(float Lateral, float Forward, float Braking)
{
    /// <summary>Werte für einen Tourenwagen auf Slicks – nur als Rückfallebene.</summary>
    public static readonly GripEstimate Default = new(14f, 6f, 16f);

    /// <summary>
    /// Schätzt die Grenzen aus einer Runde.
    /// </summary>
    /// <remarks>
    /// Genommen wird nicht das Maximum, sondern das 95. Perzentil: Ein
    /// einzelner Randstein oder ein Frame mit Sprung in den Daten würde sonst
    /// die ganze Ideallinie auf eine Grenze stellen, die es nie gab.
    /// </remarks>
    public static GripEstimate FromLap(RecordedLap lap)
    {
        LapChannels channels = lap.Channels;
        int count = channels.Count;

        if (count < 10)
        {
            return Default;
        }

        var lateral = new List<float>(count);
        var forward = new List<float>(count);
        var braking = new List<float>(count);

        for (int bin = 0; bin < count; bin++)
        {
            float speed = channels.Speed[bin];
            if (speed < 5f)
            {
                continue;
            }

            lateral.Add(MathF.Abs(channels.YawRate[bin] * speed));

            // a = v · dv/ds, aus dem Tempoverlauf über der Strecke.
            int next = (bin + 1) % count;
            int previous = (bin - 1 + count) % count;
            float longitudinal =
                speed * (channels.Speed[next] - channels.Speed[previous]) / (2f * lap.BinSize);

            if (longitudinal > 0f)
            {
                forward.Add(longitudinal);
            }
            else
            {
                braking.Add(-longitudinal);
            }
        }

        return new GripEstimate(
            Math.Clamp(Percentile(lateral, 0.95f), 6f, 45f),
            Math.Clamp(Percentile(forward, 0.95f), 1.5f, 20f),
            Math.Clamp(Percentile(braking, 0.95f), 4f, 60f));
    }

    private static float Percentile(List<float> values, float fraction)
    {
        if (values.Count == 0)
        {
            return 0f;
        }

        values.Sort();
        return values[Math.Clamp((int)(values.Count * fraction), 0, values.Count - 1)];
    }
}

/// <summary>
/// Die berechnete Ideallinie einer Strecke, in denselben Abschnitten wie die
/// aufgezeichneten Runden.
/// </summary>
public sealed class IdealLine
{
    public required float BinSize { get; init; }

    /// <summary>Versatz zur Mittellinie der Karte, positiv nach rechts.</summary>
    public required float[] Offset { get; init; }

    /// <summary>Die Linie in Weltkoordinaten.</summary>
    public required WorldPoint[] Points { get; init; }

    /// <summary>Krümmung in 1/m, positiv für Rechtskurven.</summary>
    public required float[] Curvature { get; init; }

    /// <summary>Fahrbares Tempo in m/s.</summary>
    public required float[] TargetSpeed { get; init; }

    /// <summary>Weg von einem Abschnitt zum nächsten, in Metern.</summary>
    public required float[] StepLength { get; init; }

    /// <summary>Rundenzeit, die dieses Profil ergäbe, in Sekunden.</summary>
    public required float EstimatedLapTime { get; init; }

    public int Count => Offset.Length;

    /// <summary>Punkt der Linie an einer beliebigen Distanz, linear interpoliert.</summary>
    public WorldPoint PointAt(float distance)
    {
        float exact = distance / BinSize;
        int index = Index((int)MathF.Floor(exact));
        return WorldPoint.Lerp(Points[index], Points[Index(index + 1)], exact - MathF.Floor(exact));
    }

    /// <summary>Tempo der Linie an einer beliebigen Distanz, in m/s.</summary>
    public float SpeedAt(float distance)
    {
        float exact = distance / BinSize;
        int index = Index((int)MathF.Floor(exact));
        float fraction = exact - MathF.Floor(exact);
        return TargetSpeed[index] + ((TargetSpeed[Index(index + 1)] - TargetSpeed[index]) * fraction);
    }

    /// <summary>
    /// Einheitsvektor quer zur Linie, nach rechts in Fahrtrichtung.
    /// </summary>
    /// <remarks>
    /// Gebraucht, um aus der Linie ein Band zu machen: gezeichnet wird kein
    /// Strich, sondern ein Streifen, und der braucht zwei Ränder.
    /// </remarks>
    public (float X, float Z) RightNormalAt(int index)
    {
        WorldPoint previous = Points[Index(index - 1)];
        WorldPoint next = Points[Index(index + 1)];

        float tx = next.X - previous.X;
        float tz = next.Z - previous.Z;
        float length = MathF.Sqrt((tx * tx) + (tz * tz));

        return length < 1e-4f ? (1f, 0f) : (tz / length, -tx / length);
    }

    /// <summary>Abschnitt, in den eine Distanz fällt.</summary>
    public int BinOf(float distance) => Index((int)MathF.Floor(distance / BinSize));

    private int Index(int raw)
    {
        int wrapped = raw % Count;
        return wrapped < 0 ? wrapped + Count : wrapped;
    }
}
