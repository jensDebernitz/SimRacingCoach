namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Was an der Fahrweise durch eine Kurve auffällt.
/// </summary>
/// <remarks>
/// Bewusst Flags und nicht ein einzelner Befund: Die typische Anfängerkurve ist
/// eine Kette – zu früh eingelenkt, dadurch früher Scheitelpunkt, dadurch am
/// Ausgang nachlenken und zu spät aufs Gas. Nur einen davon zu melden würde
/// genau den Zusammenhang verschweigen, der den Fehler erklärt.
/// </remarks>
[Flags]
public enum TechniqueFlag
{
    None = 0,

    /// <summary>Deutlich vor der Referenz eingelenkt.</summary>
    TurnInTooEarly = 1 << 0,

    /// <summary>Deutlich nach der Referenz eingelenkt.</summary>
    TurnInTooLate = 1 << 1,

    /// <summary>Beim Einlenken steht noch viel Bremsdruck an.</summary>
    BrakingIntoTurnIn = 1 << 2,

    /// <summary>Die Bremse wird vor dem Einlenken ganz gelöst, statt sie mitzunehmen.</summary>
    NoTrailBraking = 1 << 3,

    /// <summary>Der langsamste Punkt liegt zu weit vorn in der Kurve.</summary>
    EarlyApex = 1 << 4,

    /// <summary>Der langsamste Punkt liegt zu weit hinten.</summary>
    LateApex = 1 << 5,

    /// <summary>Vor dem Scheitelpunkt schon am Gas.</summary>
    ThrottleTooEarly = 1 << 6,

    /// <summary>Nach dem Scheitelpunkt zu lange gewartet.</summary>
    ThrottleTooLate = 1 << 7,

    /// <summary>Gas und Bremse gleichzeitig.</summary>
    PedalOverlap = 1 << 8,

    /// <summary>Viele Lenkkorrekturen innerhalb der Kurve.</summary>
    RestlessSteering = 1 << 9,

    /// <summary>Am Kurvenausgang muss nachgelenkt werden – das Kennzeichen eines frühen Scheitelpunkts.</summary>
    AddingLockOnExit = 1 << 10,
}

/// <summary>
/// Gemessene Fahrweise durch eine Kurve – rein beschreibend, ohne Wertung.
/// </summary>
/// <param name="TurnInDistance">Wo eingelenkt wurde, in Metern ab Start/Ziel.</param>
/// <param name="LineApexDistance">
/// Punkt des größten Lenkeinschlags in Metern ab Start/Ziel – der Scheitelpunkt
/// der <em>Linie</em>.
/// </param>
/// <param name="BrakeAtTurnIn">Bremsdruck 0..1 im Moment des Einlenkens.</param>
/// <param name="TrailBrakeMetres">Strecke, auf der gleichzeitig gebremst und gelenkt wurde.</param>
/// <param name="ApexPosition">Lage des langsamsten Punkts im Kurvenbogen, 0 = Eingang, 1 = Ausgang.</param>
/// <param name="ThrottleVsApexMetres">
/// Erster Gasdruck relativ zu <paramref name="LineApexDistance"/>; negativ heißt davor.
/// </param>
/// <param name="PedalOverlapMetres">Strecke mit Gas und Bremse gleichzeitig.</param>
/// <param name="SteeringReversals">Richtungswechsel der Lenkbewegung innerhalb der Kurve.</param>
/// <param name="SteeringAtApex">Lenkeinschlag am langsamsten Punkt, 0..1.</param>
/// <param name="PeakSteeringAfterApex">Größter Lenkeinschlag hinter dem langsamsten Punkt, 0..1.</param>
/// <remarks>
/// Der Bezugspunkt für die Gasannahme ist bewusst der größte Lenkeinschlag und
/// nicht der langsamste Punkt. Wer zu früh Gas gibt, hört genau dort auf
/// langsamer zu werden – der langsamste Punkt wandert also mit dem Fehler mit
/// und könnte ihn nie zeigen. Der Lenkeinschlag folgt dagegen der Linie und
/// bleibt, wo er ist.
/// </remarks>
public sealed record CornerTechnique(
    float TurnInDistance,
    float LineApexDistance,
    float BrakeAtTurnIn,
    float TrailBrakeMetres,
    float ApexPosition,
    float ThrottleVsApexMetres,
    float PedalOverlapMetres,
    int SteeringReversals,
    float SteeringAtApex,
    float PeakSteeringAfterApex)
{
    /// <summary>Eine Messung, in der nichts Sinnvolles steht.</summary>
    public static readonly CornerTechnique Empty = new(0f, 0f, 0f, 0f, 0.5f, 0f, 0f, 0, 0f, 0f);

    /// <summary>
    /// True, wenn am Ausgang mehr Lenkeinschlag nötig war als am langsamsten
    /// Punkt. Das ist die Handschrift eines zu früh angelegten Scheitelpunkts:
    /// Wer zu früh an die Innenseite fährt, hat am Ausgang keine Strecke mehr
    /// und muss nachziehen, statt das Lenkrad aufmachen zu können.
    /// </summary>
    public bool AddsLockOnExit =>
        SteeringAtApex > 0.05f &&
        PeakSteeringAfterApex > SteeringAtApex * 1.15f &&
        PeakSteeringAfterApex - SteeringAtApex > 0.04f;
}

/// <summary>
/// Die Fahrweise einer Kurve im Vergleich zur Referenzrunde.
/// </summary>
/// <param name="Driven">Messung der gefahrenen Runde.</param>
/// <param name="Reference">Messung derselben Kurve in der Referenzrunde.</param>
/// <param name="Flags">Was daran auffällt.</param>
/// <param name="TurnInDelta">Meter; positiv heißt später eingelenkt als die Referenz.</param>
/// <param name="Text">
/// Ausformulierter Befund fürs Overlay, <em>ohne</em> Kurvennamen – der Befund
/// wird mal allein und mal hinter dem Zeitverlust derselben Kurve angezeigt,
/// und zweimal "Kurve 5" in einer Zeile liest sich schlecht. Leer, wenn nichts
/// auffällt.
/// </param>
/// <param name="Speech">Kurzfassung für die Sprachausgabe; leer, wenn nichts zu sagen ist.</param>
public sealed record TechniqueReport(
    CornerTechnique Driven,
    CornerTechnique Reference,
    TechniqueFlag Flags,
    float TurnInDelta,
    string Text,
    string Speech)
{
    /// <summary>Ein Bericht ohne Befund.</summary>
    public static readonly TechniqueReport None = new(
        CornerTechnique.Empty, CornerTechnique.Empty, TechniqueFlag.None, 0f, string.Empty, string.Empty);

    /// <summary>True, wenn überhaupt etwas aufgefallen ist.</summary>
    public bool HasFinding => Flags != TechniqueFlag.None && Text.Length > 0;

    /// <summary>True, wenn das genannte Merkmal zutrifft.</summary>
    public bool Has(TechniqueFlag flag) => (Flags & flag) != 0;
}
