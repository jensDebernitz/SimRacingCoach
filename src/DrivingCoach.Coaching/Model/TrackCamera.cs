using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching.Model;

/// <summary>Ein Punkt auf dem Bildschirm, in Pixeln.</summary>
/// <param name="X">Abstand vom linken Rand.</param>
/// <param name="Y">Abstand vom oberen Rand.</param>
/// <param name="Distance">Wie weit der Punkt vor der Kamera liegt, in Metern.</param>
public readonly record struct ScreenPoint(float X, float Y, float Distance);

/// <summary>
/// Rechnet Punkte der Strecke in Punkte auf dem Bildschirm um.
/// </summary>
/// <remarks>
/// <para>
/// Eine gewöhnliche Lochkamera: Blickrichtung aus der Fahrzeuglage, Augpunkt
/// und Blickwinkel aus der <see cref="CameraCalibration"/>. Mehr ist nicht
/// möglich, weil das Spiel seine eigene Kamera nicht preisgibt.
/// </para>
/// <para>
/// Daraus folgt die Grenze des Verfahrens: Was hier herauskommt, stimmt genau
/// so gut, wie die Kalibrierung zur Kameraeinstellung im Spiel passt. Ändert
/// der Fahrer dort den Blickwinkel oder wechselt die Kameraperspektive, liegt
/// die Linie daneben, bis neu kalibriert wurde. In VR funktioniert das gar
/// nicht – dort sitzt die Kamera im Headset und nicht auf dem Bildschirm.
/// </para>
/// </remarks>
public sealed class TrackCamera
{
    /// <summary>
    /// Näher als das wird nichts gezeichnet.
    /// </summary>
    /// <remarks>
    /// Direkt vor dem Augpunkt wird der Nenner der Perspektive winzig und ein
    /// Punkt landet weit außerhalb des Bildschirms. Zwei Meter sind ohnehin
    /// unter der Motorhaube.
    /// </remarks>
    public const float NearPlane = 2f;

    private readonly WorldPoint _eye;
    private readonly (float X, float Y, float Z) _right;
    private readonly (float X, float Y, float Z) _up;
    private readonly (float X, float Y, float Z) _forward;
    private readonly float _focalLength;
    private readonly float _centreX;
    private readonly float _centreY;

    private TrackCamera(
        WorldPoint eye,
        (float X, float Y, float Z) right,
        (float X, float Y, float Z) up,
        (float X, float Y, float Z) forward,
        float focalLength,
        float centreX,
        float centreY)
    {
        _eye = eye;
        _right = right;
        _up = up;
        _forward = forward;
        _focalLength = focalLength;
        _centreX = centreX;
        _centreY = centreY;
    }

    /// <summary>Augpunkt in Weltkoordinaten.</summary>
    public WorldPoint Eye => _eye;

    /// <summary>Brennweite in Pixeln – ergibt sich aus Blickwinkel und Bildbreite.</summary>
    public float FocalLength => _focalLength;

    /// <summary>
    /// Stellt die Kamera für einen Telemetrie-Frame auf.
    /// </summary>
    /// <param name="frame">Der aktuelle Frame; liefert Position und Lage.</param>
    /// <param name="calibration">Die Werte des Fahrzeugs.</param>
    /// <param name="pose">Wie der Gierwinkel des Spiels zu lesen ist.</param>
    /// <param name="viewportWidth">Breite der Zeichenfläche in Pixeln.</param>
    /// <param name="viewportHeight">Höhe der Zeichenfläche in Pixeln.</param>
    /// <returns><c>null</c>, wenn die Angaben keine Kamera ergeben.</returns>
    public static TrackCamera? Create(
        in TelemetryFrame frame,
        CameraCalibration calibration,
        PoseConvention pose,
        float viewportWidth,
        float viewportHeight)
    {
        if (viewportWidth < 1f || viewportHeight < 1f || !calibration.IsPlausible)
        {
            return null;
        }

        float heading = pose.HeadingFrom(frame.Pose.Yaw);

        // Waagerechte Grundrichtungen. Die Höhe liegt bei AMS2 auf Y, die
        // Fahrbahn spannt sich über X und Z auf.
        var flatForward = (X: MathF.Sin(heading), Y: 0f, Z: MathF.Cos(heading));
        var flatRight = (X: MathF.Cos(heading), Y: 0f, Z: -MathF.Sin(heading));
        var worldUp = (X: 0f, Y: 1f, Z: 0f);

        float pitch = (calibration.PitchSign * frame.Pose.Pitch) +
                      (calibration.PitchOffsetDegrees * MathF.PI / 180f);
        float roll = calibration.RollSign * frame.Pose.Roll;

        // Nicken kippt Blickrichtung und Oben um die Querachse.
        var forward = Add(Scale(flatForward, MathF.Cos(pitch)), Scale(worldUp, MathF.Sin(pitch)));
        var up = Add(Scale(flatForward, -MathF.Sin(pitch)), Scale(worldUp, MathF.Cos(pitch)));

        // Wanken dreht Quer und Oben um die Blickrichtung.
        var right = Add(Scale(flatRight, MathF.Cos(roll)), Scale(up, MathF.Sin(roll)));
        up = Add(Scale(flatRight, -MathF.Sin(roll)), Scale(up, MathF.Cos(roll)));

        // Der Augpunkt sitzt neben dem Bezugspunkt des Autos – vor allem über
        // ihm. Verschoben wird entlang der waagerechten Achsen, damit ein
        // gekipptes Auto den Sitz nicht durch den Boden schiebt.
        WorldPoint eye = new(
            frame.WorldPosition.X + (flatForward.X * calibration.EyeForward) + (flatRight.X * calibration.EyeRight),
            frame.WorldPosition.Y + calibration.EyeHeight,
            frame.WorldPosition.Z + (flatForward.Z * calibration.EyeForward) + (flatRight.Z * calibration.EyeRight));

        // Brennweite in Pixeln. Über die halbe Bildbreite gerechnet, damit der
        // senkrechte Blickwinkel aus dem Seitenverhältnis folgt.
        float focal = (viewportWidth / 2f) / MathF.Tan(calibration.FieldOfViewDegrees * MathF.PI / 360f);

        return new TrackCamera(eye, right, up, forward, focal, viewportWidth / 2f, viewportHeight / 2f);
    }

    /// <summary>
    /// Rechnet einen Punkt der Strecke auf den Bildschirm um.
    /// </summary>
    /// <returns>False, wenn der Punkt hinter oder zu dicht vor der Kamera liegt.</returns>
    public bool TryProject(WorldPoint point, out ScreenPoint screen)
    {
        float dx = point.X - _eye.X;
        float dy = point.Y - _eye.Y;
        float dz = point.Z - _eye.Z;

        float depth = (dx * _forward.X) + (dy * _forward.Y) + (dz * _forward.Z);
        if (depth < NearPlane)
        {
            screen = default;
            return false;
        }

        float lateral = (dx * _right.X) + (dy * _right.Y) + (dz * _right.Z);
        float vertical = (dx * _up.X) + (dy * _up.Y) + (dz * _up.Z);

        screen = new ScreenPoint(
            _centreX + (_focalLength * lateral / depth),
            _centreY - (_focalLength * vertical / depth),
            depth);

        return true;
    }

    private static (float X, float Y, float Z) Scale((float X, float Y, float Z) v, float factor) =>
        (v.X * factor, v.Y * factor, v.Z * factor);

    private static (float X, float Y, float Z) Add((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}
