using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Eine Runde, abgetastet in festen Distanz-Abschnitten statt in Zeitschritten.
/// </summary>
/// <remarks>
/// Die Abtastung über die Distanz ist der Kern des ganzen Vergleichs: nur so
/// lassen sich zwei unterschiedlich schnelle Runden an derselben Stelle der
/// Strecke gegenüberstellen. Bei Zeitabtastung würden die Stützstellen
/// auseinanderlaufen.
/// </remarks>
public sealed class LapChannels
{
    /// <summary>Rundenzeit in Sekunden beim Passieren dieses Abschnitts.</summary>
    public required float[] Time { get; init; }

    /// <summary>Geschwindigkeit in m/s.</summary>
    public required float[] Speed { get; init; }

    /// <summary>Gasstellung 0..1.</summary>
    public required float[] Throttle { get; init; }

    /// <summary>Bremsstellung 0..1.</summary>
    public required float[] Brake { get; init; }

    /// <summary>Lenkwinkel -1..1.</summary>
    public required float[] Steering { get; init; }

    /// <summary>Gierrate in rad/s.</summary>
    public required float[] YawRate { get; init; }

    /// <summary>Eingelegter Gang.</summary>
    public required int[] Gear { get; init; }

    /// <summary>
    /// Wo das Auto stand, in Streckenkoordinaten.
    /// </summary>
    /// <remarks>
    /// Bewusst nicht <c>required</c>: Runden, die vor der Ideallinie
    /// aufgezeichnet wurden, haben diese Kanäle nicht. Mit <c>required</c>
    /// würde das Einlesen der alten Dateien mit einer Ausnahme abbrechen und
    /// der Fahrer stünde ohne seine Referenzrunde da. Stattdessen bleiben die
    /// Felder leer und <see cref="HasGeometry"/> meldet das.
    /// </remarks>
    public float[] PositionX { get; init; } = [];

    /// <inheritdoc cref="PositionX"/>
    public float[] PositionY { get; init; } = [];

    /// <inheritdoc cref="PositionX"/>
    public float[] PositionZ { get; init; } = [];

    /// <summary>
    /// Räder abseits der Strecke, 0 bis 4. Zieht die Grenze des Korridors:
    /// wo ein Rad im Gras war, hört die Fahrbahn auf.
    /// </summary>
    public int[] OffTrackWheels { get; init; } = [];

    /// <summary>
    /// Blickrichtung des Autos in Radiant, roh aus dem Spiel.
    /// </summary>
    /// <remarks>
    /// Wofür der Wert steht – Nullpunkt und Drehsinn – ist nirgends
    /// dokumentiert. Verglichen mit der Richtung, in die sich das Auto
    /// tatsächlich bewegt hat, lässt es sich aber ausrechnen; siehe
    /// <see cref="PoseConvention"/>.
    /// </remarks>
    public float[] Yaw { get; init; } = [];

    public int Count => Time.Length;

    /// <summary>True, wenn die Runde Weltkoordinaten mitbringt.</summary>
    public bool HasGeometry => PositionX.Length == Count && Count > 0;

    /// <summary>Position an einem Abschnitt. Ohne Geometrie der Nullpunkt.</summary>
    public WorldPoint PositionOf(int bin) => HasGeometry
        ? new WorldPoint(PositionX[bin], PositionY[bin], PositionZ[bin])
        : default;

    public static LapChannels Allocate(int bins) => new()
    {
        Time = new float[bins],
        Speed = new float[bins],
        Throttle = new float[bins],
        Brake = new float[bins],
        Steering = new float[bins],
        YawRate = new float[bins],
        Gear = new int[bins],
        PositionX = new float[bins],
        PositionY = new float[bins],
        PositionZ = new float[bins],
        OffTrackWheels = new int[bins],
        Yaw = new float[bins],
    };
}

/// <summary>Eine vollständig aufgezeichnete Runde inklusive Metadaten.</summary>
public sealed class RecordedLap
{
    /// <summary>Schlüssel aus Strecke, Layout und Auto – bestimmt die Vergleichbarkeit.</summary>
    public required string ReferenceKey { get; init; }

    public required string TrackName { get; init; }

    public required string CarName { get; init; }

    /// <summary>Streckenlänge in Metern.</summary>
    public required float TrackLength { get; init; }

    /// <summary>Abschnittsbreite der Abtastung in Metern.</summary>
    public required float BinSize { get; init; }

    /// <summary>Rundenzeit in Sekunden.</summary>
    public required float LapTime { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required LapChannels Channels { get; init; }

    /// <summary>
    /// Distanz in Metern, für die der Abschnitt gilt. Ein Abschnitt hält den
    /// Zustand an seiner <em>Eintrittskante</em> fest, damit
    /// <see cref="TimeAt"/> sauber zwischen zwei Abschnitten interpolieren kann.
    /// </summary>
    public float DistanceOf(int bin) => bin * BinSize;

    /// <summary>
    /// Rundenzeit an einer beliebigen Distanz, linear zwischen den Abschnitten
    /// interpoliert. Grundlage für das Live-Delta.
    /// </summary>
    public float TimeAt(float distance)
    {
        float[] time = Channels.Time;
        if (time.Length == 0)
        {
            return 0f;
        }

        float position = distance / BinSize;
        int index = (int)MathF.Floor(position);

        if (index < 0)
        {
            return 0f;
        }

        if (index >= time.Length - 1)
        {
            return time[^1];
        }

        float fraction = position - index;
        return time[index] + (time[index + 1] - time[index]) * fraction;
    }

    /// <summary>Wert eines Kanals an einer Distanz, linear interpoliert.</summary>
    public static float Sample(float[] channel, float binSize, float distance)
    {
        if (channel.Length == 0)
        {
            return 0f;
        }

        float position = distance / binSize;
        int index = Math.Clamp((int)MathF.Floor(position), 0, channel.Length - 1);

        if (index >= channel.Length - 1)
        {
            return channel[^1];
        }

        float fraction = position - index;
        return channel[index] + (channel[index + 1] - channel[index]) * fraction;
    }
}
