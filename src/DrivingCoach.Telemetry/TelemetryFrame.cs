using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Telemetry;

/// <summary>Ein Wert je Rad, in der Reihenfolge des Shared Memory (VL, VR, HL, HR).</summary>
public readonly record struct Wheels4(float FrontLeft, float FrontRight, float RearLeft, float RearRight)
{
    public float this[int index] => index switch
    {
        0 => FrontLeft,
        1 => FrontRight,
        2 => RearLeft,
        3 => RearRight,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public float Max => MathF.Max(MathF.Max(FrontLeft, FrontRight), MathF.Max(RearLeft, RearRight));

    public float Min => MathF.Min(MathF.Min(FrontLeft, FrontRight), MathF.Min(RearLeft, RearRight));

    public float FrontMax => MathF.Max(FrontLeft, FrontRight);

    public float RearMax => MathF.Max(RearLeft, RearRight);
}

/// <summary>
/// Punkt in Streckenkoordinaten, in Metern.
/// </summary>
/// <remarks>
/// AMS2 legt die Höhe auf <see cref="Y"/>; die Fahrbahn spannt sich also über
/// <see cref="X"/> und <see cref="Z"/> auf. Wer das verwechselt, bekommt eine
/// Strecke, die senkrecht in den Himmel steht – deshalb heißen die Helfer hier
/// ausdrücklich "Flat", wenn sie die Höhe weglassen.
/// </remarks>
public readonly record struct WorldPoint(float X, float Y, float Z)
{
    /// <summary>Abstand in der Ebene, ohne Höhenunterschied.</summary>
    public float FlatDistanceTo(WorldPoint other)
    {
        float dx = X - other.X;
        float dz = Z - other.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    public static WorldPoint operator -(WorldPoint a, WorldPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static WorldPoint operator +(WorldPoint a, WorldPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static WorldPoint operator *(WorldPoint a, float factor) => new(a.X * factor, a.Y * factor, a.Z * factor);

    /// <summary>Lineare Mischung zweier Punkte. <paramref name="t"/> 0 liefert <paramref name="a"/>.</summary>
    public static WorldPoint Lerp(WorldPoint a, WorldPoint b, float t) => a + ((b - a) * t);
}

/// <summary>
/// Lage des Autos im Raum, in Radiant.
/// </summary>
/// <remarks>
/// Die Blickrichtung steckt in <see cref="Yaw"/>. <see cref="Pitch"/> und
/// <see cref="Roll"/> werden erst für die perspektivische Linie gebraucht:
/// ohne sie kippt die gezeichnete Linie in Kuppen und Steilkurven weg.
/// </remarks>
public readonly record struct CarPose(float Yaw, float Pitch, float Roll);

/// <summary>Untergrund je Rad.</summary>
public readonly record struct TerrainSet(Terrain FrontLeft, Terrain FrontRight, Terrain RearLeft, Terrain RearRight)
{
    /// <summary>Anzahl Räder abseits der befestigten Strecke.</summary>
    public int OffTrackWheels =>
        (FrontLeft.IsOffTrack() ? 1 : 0) +
        (FrontRight.IsOffTrack() ? 1 : 0) +
        (RearLeft.IsOffTrack() ? 1 : 0) +
        (RearRight.IsOffTrack() ? 1 : 0);

    /// <summary>True, wenn das Auto komplett neben der Strecke ist.</summary>
    public bool AllWheelsOffTrack => OffTrackWheels == 4;
}

/// <summary>
/// Sitzungsdaten, die sich während der Fahrt nicht ändern. Bewusst von
/// <see cref="TelemetryFrame"/> getrennt, damit die Strings nicht pro Frame
/// neu dekodiert und allokiert werden müssen.
/// </summary>
public sealed record SessionInfo(
    string TrackName,
    string TrackVariation,
    float TrackLength,
    string CarName,
    string CarClass,
    int NumSectors)
{
    public static readonly SessionInfo Unknown = new(string.Empty, string.Empty, 0f, string.Empty, string.Empty, 0);

    /// <summary>Strecke inklusive Layout, z. B. "Interlagos – GP".</summary>
    public string TrackDisplayName =>
        string.IsNullOrEmpty(TrackVariation) ? TrackName : $"{TrackName} – {TrackVariation}";

    /// <summary>
    /// Stabiler Schlüssel für die Referenzrunde. Die Streckenlänge gehört dazu,
    /// weil dieselbe Strecke in verschiedenen Layouts denselben Namen tragen kann.
    /// </summary>
    public string ReferenceKey => Sanitize($"{TrackName}_{TrackVariation}_{TrackLength:F0}m_{CarName}");

    /// <summary>
    /// Schlüssel für die Streckenkarte. Ohne das Auto, denn der Asphalt liegt
    /// da, wo er liegt – gelernte Streckengeometrie gilt für alle Fahrzeuge.
    /// </summary>
    public string TrackKey => Sanitize($"{TrackName}_{TrackVariation}_{TrackLength:F0}m");

    /// <summary>True, wenn genug Daten für eine sinnvolle Aufzeichnung vorliegen.</summary>
    public bool IsUsable => TrackLength > 100f && !string.IsNullOrEmpty(TrackName);

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        foreach (char c in value)
        {
            buffer[length++] = char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_';
        }

        return new string(buffer[..length]);
    }
}

/// <summary>
/// Aufbereiteter Telemetrie-Frame: die Felder, die der Coach tatsächlich braucht,
/// in vernünftigen Einheiten und ohne Bezug zum Shared-Memory-Layout.
/// </summary>
public readonly record struct TelemetryFrame
{
    /// <summary>Monoton steigende Zeit in Sekunden seit Start der Anwendung.</summary>
    public double Timestamp { get; init; }

    public GameState GameState { get; init; }
    public SessionState SessionState { get; init; }
    public RaceState RaceState { get; init; }
    public PitMode PitMode { get; init; }

    /// <summary>Zurückgelegte Distanz auf der aktuellen Runde in Metern.</summary>
    public float LapDistance { get; init; }

    /// <summary>
    /// Position des Autos in Streckenkoordinaten. Grundlage der Ideallinie –
    /// die Schnittstelle liefert keine Streckengeometrie, also wird sie aus
    /// diesen Punkten gelernt.
    /// </summary>
    public WorldPoint WorldPosition { get; init; }

    /// <summary>Lage des Autos im Raum. Nur für die perspektivische Linie nötig.</summary>
    public CarPose Pose { get; init; }

    /// <summary>Laufende Rundenzeit in Sekunden.</summary>
    public float CurrentLapTime { get; init; }

    public float LastLapTime { get; init; }
    public float BestLapTime { get; init; }
    public int LapsCompleted { get; init; }
    public int CurrentLap { get; init; }
    public int CurrentSector { get; init; }
    public bool LapInvalidated { get; init; }

    /// <summary>Geschwindigkeit in Metern pro Sekunde.</summary>
    public float Speed { get; init; }

    public float Rpm { get; init; }
    public float MaxRpm { get; init; }
    public int Gear { get; init; }
    public int NumGears { get; init; }

    /// <summary>Rohe Gasstellung 0..1 (unfiltered, also das, was der Fahrer macht).</summary>
    public float Throttle { get; init; }

    /// <summary>Rohe Bremsstellung 0..1.</summary>
    public float Brake { get; init; }

    public float Clutch { get; init; }

    /// <summary>Lenkwinkel -1..1.</summary>
    public float Steering { get; init; }

    /// <summary>Gierrate in rad/s. Basis der Kurvenerkennung, da vorzeichen- und einheitensicher.</summary>
    public float YawRate { get; init; }

    /// <summary>
    /// Querbeschleunigung in m/s² (aus <c>mLocalAcceleration[VEC_X]</c>).
    /// Wird nur für Anzeige/Heuristik genutzt, nicht für die Kurvenerkennung –
    /// die Achsenzuordnung der Madness-Engine ist nicht offiziell dokumentiert.
    /// </summary>
    public float LateralAcceleration { get; init; }

    /// <summary>Längsbeschleunigung in m/s² (aus <c>mLocalAcceleration[VEC_Z]</c>).</summary>
    public float LongitudinalAcceleration { get; init; }

    /// <summary>Radumdrehungen pro Sekunde je Rad – Grundlage für Blockierer und Durchdrehen.</summary>
    public Wheels4 TyreRps { get; init; }

    public TerrainSet Terrain { get; init; }
    public bool AbsActive { get; init; }
    public CarFlags CarFlags { get; init; }
    public float BrakeBias { get; init; }

    /// <summary>Geschwindigkeit in km/h.</summary>
    public float SpeedKmh => Speed * 3.6f;

    /// <summary>Drehzahl als Anteil der Maximaldrehzahl (0..1).</summary>
    public float RpmFraction => MaxRpm > 1f ? Rpm / MaxRpm : 0f;

    /// <summary>True, wenn der Fahrer gerade aktiv auf der Strecke ist.</summary>
    public bool IsDriving => GameState == GameState.InGamePlaying;

    /// <summary>True, wenn die Runde für eine Referenzzeit nicht taugt.</summary>
    public bool IsInPitLane => PitMode != PitMode.None;
}

/// <summary>Sitzungs- und Frame-Daten zusammen, wie sie die Quelle ausliefert.</summary>
public readonly record struct TelemetrySnapshot(SessionInfo Session, TelemetryFrame Frame);
