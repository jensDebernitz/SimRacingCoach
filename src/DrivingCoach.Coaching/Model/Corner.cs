namespace DrivingCoach.Coaching.Model;

/// <summary>Drehrichtung einer Kurve.</summary>
public enum CornerDirection
{
    Left,
    Right,
}

/// <summary>
/// Eine auf der Referenzrunde erkannte Kurve, beschrieben über Abschnitts-Indizes.
/// </summary>
/// <param name="Number">Fortlaufende Nummer ab Start/Ziel, beginnend bei 1.</param>
/// <param name="StartBin">Beginn des gekrümmten Bereichs.</param>
/// <param name="EndBin">Ende des gekrümmten Bereichs.</param>
/// <param name="ApexBin">Langsamster Punkt – der Scheitelpunkt.</param>
/// <param name="BrakingStartBin">Wo in der Referenz zuerst gebremst wurde.</param>
/// <param name="ThrottleBin">Wo in der Referenz wieder voll beschleunigt wurde.</param>
/// <param name="Direction">Links- oder Rechtskurve.</param>
/// <param name="ApexSpeed">Geschwindigkeit am Scheitelpunkt in m/s.</param>
/// <param name="EntrySpeed">Geschwindigkeit beim Anbremsen in m/s.</param>
public sealed record Corner(
    int Number,
    int StartBin,
    int EndBin,
    int ApexBin,
    int BrakingStartBin,
    int ThrottleBin,
    CornerDirection Direction,
    float ApexSpeed,
    float EntrySpeed)
{
    /// <summary>Anzeigename, z. B. "Kurve 4".</summary>
    public string Name => $"Kurve {Number}";

    /// <summary>True, wenn vor der Kurve nennenswert gebremst wird.</summary>
    public bool HasBrakingZone => BrakingStartBin < StartBin;

    /// <summary>Scheitelgeschwindigkeit in km/h.</summary>
    public float ApexSpeedKmh => ApexSpeed * 3.6f;

    /// <summary>Grobe Einordnung für die Sprachausgabe.</summary>
    public string SpeedCategory => ApexSpeedKmh switch
    {
        < 70f => "langsame",
        < 140f => "mittelschnelle",
        _ => "schnelle",
    };
}
