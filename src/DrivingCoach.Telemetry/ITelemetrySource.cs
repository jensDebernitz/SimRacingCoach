namespace DrivingCoach.Telemetry;

/// <summary>Status einer Telemetriequelle, so wie ihn das Overlay anzeigt.</summary>
/// <param name="IsConnected">True, wenn Daten fließen.</param>
/// <param name="Headline">Kurztext für die Statuszeile.</param>
/// <param name="Detail">Optionaler Hinweis, was zu tun ist.</param>
public readonly record struct TelemetryStatus(bool IsConnected, string Headline, string? Detail = null);

/// <summary>
/// Quelle für Telemetrie-Frames. Abstrahiert AMS2, damit der Coach auch gegen
/// den Simulator laufen kann – ohne laufendes Spiel gäbe es sonst keine
/// Möglichkeit, die Auswertung zu testen.
/// </summary>
public interface ITelemetrySource : IDisposable
{
    /// <summary>Wird für jeden gelesenen Frame ausgelöst (Hintergrund-Thread).</summary>
    event Action<TelemetrySnapshot>? FrameReceived;

    /// <summary>Wird ausgelöst, wenn sich Strecke oder Auto ändern.</summary>
    event Action<SessionInfo>? SessionChanged;

    /// <summary>Wird bei Änderung des Verbindungszustands ausgelöst.</summary>
    event Action<TelemetryStatus>? StatusChanged;

    /// <summary>Aktueller Verbindungszustand.</summary>
    TelemetryStatus Status { get; }

    /// <summary>Startet die Erfassung.</summary>
    void Start();

    /// <summary>Stoppt die Erfassung.</summary>
    void Stop();
}
