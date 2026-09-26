namespace DrivingCoach.Coaching;

/// <summary>Art einer Coach-Meldung – bestimmt Farbe und Dringlichkeit im Overlay.</summary>
public enum CoachMessageKind
{
    /// <summary>Allgemeine Information, z. B. geladene Referenzrunde.</summary>
    Info,

    /// <summary>Erkannter Fahrfehler.</summary>
    Fault,

    /// <summary>Hinweis zur gleich kommenden Kurve.</summary>
    CornerTip,

    /// <summary>Zusammenfassung nach einer Runde.</summary>
    LapSummary,

    /// <summary>Neue Bestzeit.</summary>
    Achievement,
}

/// <summary>Eine Meldung des Coaches.</summary>
/// <param name="Kind">Art der Meldung.</param>
/// <param name="Text">Text fürs Overlay.</param>
/// <param name="SpeechText">Text für die Sprachausgabe; leer heißt "nicht sprechen".</param>
/// <param name="Priority">Höher gewinnt, wenn mehrere Meldungen anstehen.</param>
/// <param name="At">Zeitstempel in Sekunden seit Anwendungsstart.</param>
public sealed record CoachMessage(
    CoachMessageKind Kind,
    string Text,
    string SpeechText,
    int Priority,
    double At)
{
    /// <summary>True, wenn die Meldung vorgelesen werden soll.</summary>
    public bool HasSpeech => !string.IsNullOrWhiteSpace(SpeechText);
}

/// <summary>Einstellungen für das Verhalten des Coaches.</summary>
public sealed record CoachOptions
{
    /// <summary>Sprachausgabe aktiv.</summary>
    public bool SpeechEnabled { get; init; } = true;

    /// <summary>Mindestabstand zwischen zwei Ansagen in Sekunden.</summary>
    public double MinimumSpeechGapSeconds { get; init; } = 4.0;

    /// <summary>Gleicher Text wird innerhalb dieser Zeit nicht wiederholt.</summary>
    public double SpeechRepeatBlockSeconds { get; init; } = 20.0;

    /// <summary>
    /// Wie viele Sekunden vor dem Bremspunkt der Kurventipp kommt. Zu früh und
    /// er ist vergessen, zu spät und er kommt mitten im Anbremsen.
    /// </summary>
    public double CornerCueLeadSeconds { get; init; } = 2.5;

    /// <summary>Ab diesem Zeitverlust bekommt eine Kurve einen Live-Tipp.</summary>
    public float CornerCueMinTimeLoss { get; init; } = 0.10f;

    /// <summary>Wie viele Kurven die Rundenzusammenfassung nennt.</summary>
    public int LapSummaryCornerCount { get; init; } = 3;

    /// <summary>Fahrfehler auch dann melden, wenn Kurventipps aktiv sind.</summary>
    public bool FaultWarningsEnabled { get; init; } = true;
}
