namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Was der Coach über die Kamera im Cockpit wissen muss, um eine Linie auf den
/// Asphalt zu zeichnen.
/// </summary>
/// <remarks>
/// <para>
/// Diese Werte müssten eigentlich aus dem Spiel kommen. Der Shared Memory von
/// AMS2 nennt sie aber nicht: weder Blickwinkel noch Sitzposition noch
/// Kameraneigung stehen darin. Ohne sie lässt sich ein Punkt der Strecke nicht
/// in einen Punkt auf dem Bildschirm umrechnen – die Linie läge irgendwo.
/// </para>
/// <para>
/// Deshalb werden sie einmal je Fahrzeug eingestellt. Der Ausgangspunkt ist der
/// Blickwinkel aus den Kameraeinstellungen des Spiels; den Rest schiebt der
/// Fahrer zurecht, bis die Linie in der Geraden auf der Fahrbahn liegt.
/// </para>
/// <para>
/// Zwei Dinge muss man dafür im Spiel abschalten, sonst wandert die Linie
/// dauernd: Die Kamerabewegungen ("World Movement", "G-Force Effect" und die
/// Kopfbewegung) gehören auf 0. Sie verschieben das Bild, ohne dass die
/// Telemetrie davon erzählt.
/// </para>
/// </remarks>
public sealed record CameraCalibration
{
    /// <summary>Üblicher Wert für einen Einzelbildschirm – nur als Startpunkt.</summary>
    public const float DefaultFieldOfView = 56f;

    public static readonly CameraCalibration Default = new();

    /// <summary>Fahrzeug, für das die Werte gelten. Leer heißt: für alle.</summary>
    public string CarName { get; init; } = string.Empty;

    /// <summary>
    /// Waagerechter Blickwinkel in Grad, wie im Spiel eingestellt.
    /// </summary>
    /// <remarks>
    /// Der senkrechte Blickwinkel ergibt sich daraus und aus dem
    /// Seitenverhältnis des Bildschirms von selbst – Pixel sind quadratisch.
    /// </remarks>
    public float FieldOfViewDegrees { get; init; } = DefaultFieldOfView;

    /// <summary>Augpunkt vor dem Bezugspunkt des Autos, in Metern.</summary>
    /// <remarks>
    /// Wo genau AMS2 den Bezugspunkt des Fahrzeugs hat, ist nicht dokumentiert.
    /// Die drei Versätze fangen das mit auf; sie sind Stellschrauben, keine
    /// Messwerte.
    /// </remarks>
    public float EyeForward { get; init; } = 0.30f;

    /// <summary>Augpunkt rechts des Bezugspunktes. Negativ für Linkslenker.</summary>
    public float EyeRight { get; init; } = -0.35f;

    /// <summary>Augpunkt über dem Bezugspunkt, in Metern.</summary>
    public float EyeHeight { get; init; } = 0.55f;

    /// <summary>Neigung der Kamera in Grad, positiv nach oben.</summary>
    public float PitchOffsetDegrees { get; init; }

    /// <summary>
    /// Drehsinn von Nick- und Wankwinkel, +1 oder -1.
    /// </summary>
    /// <remarks>
    /// Für den Gierwinkel lässt sich der Drehsinn aus der Fahrtrichtung
    /// herleiten (siehe <see cref="PoseConvention"/>); für Nicken und Wanken
    /// gibt es keinen vergleichbaren Anhaltspunkt in den Daten. Beide Winkel
    /// sind klein, ein falsches Vorzeichen verdoppelt also nur einen kleinen
    /// Fehler – umstellbar bleibt es trotzdem.
    /// </remarks>
    public float PitchSign { get; init; } = 1f;

    /// <inheritdoc cref="PitchSign"/>
    public float RollSign { get; init; } = 1f;

    /// <summary>True, wenn die Werte eine brauchbare Kamera ergeben.</summary>
    public bool IsPlausible =>
        FieldOfViewDegrees is > 20f and < 150f &&
        MathF.Abs(EyeForward) < 5f &&
        MathF.Abs(EyeRight) < 3f &&
        EyeHeight is > -1f and < 3f &&
        MathF.Abs(PitchOffsetDegrees) < 45f;
}
