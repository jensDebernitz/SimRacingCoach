namespace DrivingCoach.Coaching.Model;

/// <summary>
/// Mehrere Kurven, die so dicht aufeinander folgen, dass sie als eine Aufgabe
/// gefahren werden müssen.
/// </summary>
/// <param name="Number">Fortlaufende Nummer der Kombination ab Start/Ziel.</param>
/// <param name="Corners">Die enthaltenen Kurven in Fahrtrichtung.</param>
/// <param name="FollowingStraightMetres">
/// Meter bis zum Bremspunkt der nächsten Kurve. Entscheidet darüber, wie teuer
/// ein vermurkster Ausgang ist.
/// </param>
public sealed record CornerSequence(
    int Number,
    IReadOnlyList<Corner> Corners,
    float FollowingStraightMetres)
{
    /// <summary>Die erste Kurve der Kette.</summary>
    public Corner First => Corners[0];

    /// <summary>Die letzte Kurve der Kette – sie bestimmt das Tempo auf der folgenden Geraden.</summary>
    public Corner Last => Corners[^1];

    /// <summary>True, wenn tatsächlich mehrere Kurven zusammenhängen.</summary>
    public bool IsCombination => Corners.Count > 1;

    /// <summary>Anzeigename, z. B. "Kurven 5–6".</summary>
    public string Name => IsCombination ? $"Kurven {First.Number}–{Last.Number}" : First.Name;

    /// <summary>Vorlesbarer Name – ein Gedankenstrich wird sonst verschluckt.</summary>
    public string SpeechName => IsCombination ? $"Kurven {First.Number} bis {Last.Number}" : First.Name;

    /// <summary>Richtungsfolge, z. B. "links-rechts".</summary>
    public string Shape =>
        string.Join('-', Corners.Select(c => c.Direction == CornerDirection.Left ? "links" : "rechts"));

    /// <summary>True, wenn hinter der Kombination eine lange Gerade liegt.</summary>
    public bool LeadsOntoStraight => FollowingStraightMetres >= 250f;
}

/// <summary>Was an einer Kurvenkombination als Ganzes auffällt.</summary>
public enum SequenceIssue
{
    /// <summary>Nichts Nennenswertes.</summary>
    None,

    /// <summary>Der Verlust steckt in der letzten Kurve – der Ausgang wurde dem Eingang geopfert.</summary>
    ExitCompromised,

    /// <summary>Der Verlust steckt in der ersten Kurve – zu viel Tempo mitgenommen und den Rest verhauen.</summary>
    EntryOverdriven,

    /// <summary>Über die ganze Kombination verteilt.</summary>
    Throughout,
}

/// <summary>Auswertung einer Kurvenkombination als Ganzes.</summary>
/// <param name="Sequence">Die betrachtete Kombination.</param>
/// <param name="Issue">Der Befund.</param>
/// <param name="TimeLost">Summe der in der Kombination verlorenen Sekunden.</param>
/// <param name="Text">Ausformulierter Hinweis fürs Overlay.</param>
/// <param name="Speech">Kurzfassung für die Sprachausgabe.</param>
public sealed record SequenceAdvice(
    CornerSequence Sequence,
    SequenceIssue Issue,
    float TimeLost,
    string Text,
    string Speech)
{
    /// <summary>True, wenn der Hinweis dem Fahrer etwas bringt.</summary>
    public bool IsActionable => Issue != SequenceIssue.None;
}
