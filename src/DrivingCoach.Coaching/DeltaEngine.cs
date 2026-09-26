using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>Momentaufnahme des Zeitvergleichs zur Referenzrunde.</summary>
/// <param name="HasReference">False, solange keine Referenzrunde vorliegt.</param>
/// <param name="IsMeaningful">False direkt nach Start/Ziel, wo das Delta noch zappelt.</param>
/// <param name="Delta">Sekunden gegenüber der Referenz; negativ heißt schneller.</param>
/// <param name="PredictedLapTime">Hochgerechnete Rundenzeit in Sekunden.</param>
/// <param name="ReferenceLapTime">Rundenzeit der Referenz in Sekunden.</param>
public readonly record struct DeltaState(
    bool HasReference,
    bool IsMeaningful,
    float Delta,
    float PredictedLapTime,
    float ReferenceLapTime)
{
    public static readonly DeltaState None = new(false, false, 0f, 0f, 0f);
}

/// <summary>
/// Berechnet fortlaufend den Zeitvorsprung bzw. -rückstand zur Referenzrunde.
/// </summary>
/// <remarks>
/// Der Vergleich läuft über die Distanz: an jedem Punkt der Strecke wird die
/// aktuelle Rundenzeit gegen die Zeit gehalten, zu der die Referenz denselben
/// Punkt passiert hat. Das ist dieselbe Größe, die Rennüberlagerungen als
/// Delta-Balken zeigen.
/// </remarks>
public sealed class DeltaEngine
{
    /// <summary>
    /// Vor dieser Distanz ist das Delta wertlos: kleine Unterschiede beim
    /// Überfahren der Linie erzeugen sonst große Ausschläge.
    /// </summary>
    private const float WarmupDistance = 40f;

    /// <summary>
    /// Glättung des angezeigten Delta. Ohne sie springt der Wert bei jedem
    /// Frame leicht, was am Bildschirmrand unruhig wirkt.
    /// </summary>
    private const float SmoothingFactor = 0.25f;

    private float _smoothed;
    private bool _hasSmoothed;
    private int _lastLapsCompleted = -1;

    /// <summary>Aktuelle Referenzrunde, gegen die verglichen wird.</summary>
    public RecordedLap? Reference { get; private set; }

    /// <summary>Letzter berechneter Zustand.</summary>
    public DeltaState Current { get; private set; } = DeltaState.None;

    /// <summary>Setzt die Referenzrunde (oder entfernt sie mit <c>null</c>).</summary>
    public void SetReference(RecordedLap? reference)
    {
        Reference = reference;
        _hasSmoothed = false;
        Current = DeltaState.None;
    }

    /// <summary>Berechnet den Zustand für den aktuellen Frame.</summary>
    public DeltaState Update(in TelemetryFrame frame)
    {
        RecordedLap? reference = Reference;

        if (frame.LapsCompleted != _lastLapsCompleted)
        {
            _lastLapsCompleted = frame.LapsCompleted;
            _hasSmoothed = false;
        }

        if (reference is null || !frame.IsDriving)
        {
            return Current = DeltaState.None;
        }

        float referenceTime = reference.TimeAt(frame.LapDistance);
        float raw = frame.CurrentLapTime - referenceTime;

        if (!_hasSmoothed)
        {
            _smoothed = raw;
            _hasSmoothed = true;
        }
        else
        {
            _smoothed += (raw - _smoothed) * SmoothingFactor;
        }

        bool meaningful = frame.LapDistance > WarmupDistance;

        return Current = new DeltaState(
            HasReference: true,
            IsMeaningful: meaningful,
            Delta: _smoothed,
            PredictedLapTime: reference.LapTime + _smoothed,
            ReferenceLapTime: reference.LapTime);
    }

    /// <summary>
    /// Ungeglättetes Delta an einer bestimmten Distanz einer fertigen Runde.
    /// Wird von der Kurvenanalyse benutzt, wo Glättung nur stören würde.
    /// </summary>
    public static float DeltaAt(RecordedLap lap, RecordedLap reference, float distance) =>
        lap.TimeAt(distance) - reference.TimeAt(distance);
}
