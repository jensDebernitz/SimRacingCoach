namespace DrivingCoach.Coaching.Model;

/// <summary>Art des Hinweises zu einer Kurve.</summary>
public enum AdviceKind
{
    /// <summary>Kein nennenswerter Unterschied.</summary>
    Neutral,

    /// <summary>Hier war die Runde besser als die Referenz.</summary>
    Faster,

    /// <summary>Zu früh gebremst – Bremspunkt nach hinten verlegen.</summary>
    BrakeLater,

    /// <summary>Zu wenig Tempo am Scheitelpunkt.</summary>
    MoreApexSpeed,

    /// <summary>Zu spät wieder am Gas.</summary>
    EarlierThrottle,

    /// <summary>Zu spät gebremst und dadurch den Scheitelpunkt verpasst.</summary>
    Overdriving,
}

/// <summary>Auswertung einer einzelnen Kurve gegenüber der Referenzrunde.</summary>
/// <param name="Corner">Die betrachtete Kurve.</param>
/// <param name="Kind">Der dominierende Befund.</param>
/// <param name="TimeLost">Verlorene Sekunden; negativ heißt gewonnen.</param>
/// <param name="BrakePointDelta">Meter; positiv heißt später gebremst als die Referenz.</param>
/// <param name="ApexSpeedDelta">m/s am Scheitelpunkt; positiv heißt schneller.</param>
/// <param name="ThrottleDelta">Meter; positiv heißt später aufs Gas gegangen.</param>
/// <param name="Text">Ausformulierter Hinweis fürs Overlay.</param>
/// <param name="SpeechText">Kurzfassung für die Sprachausgabe.</param>
public sealed record CornerAdvice(
    Corner Corner,
    AdviceKind Kind,
    float TimeLost,
    float BrakePointDelta,
    float ApexSpeedDelta,
    float ThrottleDelta,
    string Text,
    string SpeechText)
{
    /// <summary>
    /// Wie die Kurve gefahren wurde. Bewusst keine Positionsangabe im
    /// Konstruktor: Die Fahrweise ist eine eigenständige zweite Antwort neben
    /// dem Zeitverlust und soll ihn nicht überschreiben.
    /// </summary>
    public TechniqueReport Technique { get; init; } = TechniqueReport.None;

    /// <summary>True, wenn der Hinweis dem Fahrer etwas bringt.</summary>
    public bool IsActionable => Kind is not (AdviceKind.Neutral or AdviceKind.Faster);

    /// <summary>Zeitverlust in km/h-freier Kurzform, z. B. "+0,23 s".</summary>
    public string TimeLostLabel => TimeLost >= 0f ? $"+{TimeLost:0.00} s" : $"{TimeLost:0.00} s";

    /// <summary>
    /// Was der Fahrer als Nächstes hören soll. Die Fahrweise hat Vorrang, wenn
    /// etwas auffällt: "du lenkst zu früh ein" ist umsetzbar, "du verlierst
    /// 0,2 s" ist nur ein Befund.
    /// </summary>
    public string BestSpeechText =>
        Technique.HasFinding && Technique.Speech.Length > 0 ? Technique.Speech : SpeechText;
}

/// <summary>Gesamtauswertung einer Runde.</summary>
/// <param name="LapTime">Zeit der gefahrenen Runde in Sekunden.</param>
/// <param name="ReferenceLapTime">Zeit der Referenzrunde in Sekunden.</param>
/// <param name="Corners">Auswertung je Kurve, in Streckenreihenfolge.</param>
public sealed record LapAnalysis(
    float LapTime,
    float ReferenceLapTime,
    IReadOnlyList<CornerAdvice> Corners)
{
    /// <summary>Auswertung der Kurvenkombinationen; leer, wenn es keine gibt.</summary>
    public IReadOnlyList<SequenceAdvice> Sequences { get; init; } = [];

    /// <summary>Differenz zur Referenzrunde in Sekunden.</summary>
    public float LapDelta => LapTime - ReferenceLapTime;

    /// <summary>Die Kurven mit dem größten Zeitverlust zuerst.</summary>
    public IEnumerable<CornerAdvice> WorstFirst =>
        Corners.Where(c => c.IsActionable).OrderByDescending(c => c.TimeLost);

    /// <summary>Die Kurvenkombinationen mit dem größten Zeitverlust zuerst.</summary>
    public IEnumerable<SequenceAdvice> WorstSequencesFirst =>
        Sequences.Where(s => s.IsActionable).OrderByDescending(s => s.TimeLost);

    /// <summary>Summe der in Kurven verlorenen Zeit.</summary>
    public float TotalTimeLost => Corners.Where(c => c.TimeLost > 0f).Sum(c => c.TimeLost);

    /// <summary>
    /// Kurven, an deren Fahrweise etwas auffällt – unabhängig davon, ob dort
    /// auch Zeit verloren ging. Eine Kurve kann zeitlich passen und trotzdem
    /// falsch gefahren sein; auf der nächsten Strecke rächt sich das.
    /// </summary>
    public IEnumerable<CornerAdvice> TechniqueFindings =>
        Corners.Where(c => c.Technique.HasFinding).OrderByDescending(c => c.TimeLost);
}
