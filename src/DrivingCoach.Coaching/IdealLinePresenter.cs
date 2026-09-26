using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>
/// Setzt Ideallinie, Fahrzeuglage und Kalibrierung zu dem Band zusammen, das
/// über das Bild des Spiels gelegt wird.
/// </summary>
/// <remarks>
/// <para>
/// Steht bewusst außerhalb des Fensters: Das Zeichnen selbst sind ein paar
/// Zeilen, die Entscheidung, ob und wo überhaupt gezeichnet wird, ist der
/// Teil, der stimmen muss – und der lässt sich nur hier prüfen.
/// </para>
/// <para>
/// Wird aus dem Zeichentakt der Oberfläche aufgerufen, also bis zu sechzigmal
/// je Sekunde. Deshalb wird die Kalibrierung nur bei einem Fahrzeugwechsel von
/// der Platte gelesen und sonst festgehalten.
/// </para>
/// </remarks>
public sealed class IdealLinePresenter
{
    private readonly CameraCalibrationStore _store;

    /// <summary><c>null</c>, solange noch kein Fahrzeug gesehen wurde.</summary>
    private string? _carName;

    public IdealLinePresenter(CameraCalibrationStore? store = null)
    {
        _store = store ?? new CameraCalibrationStore();
    }

    /// <summary>Die Werte des zuletzt gesehenen Fahrzeugs.</summary>
    public CameraCalibration Calibration { get; private set; } = CameraCalibration.Default;

    /// <summary>
    /// Warum gerade nichts zu sehen ist. Leer, wenn die Linie liegt oder wenn
    /// der Fahrer ohnehin nicht auf der Strecke ist.
    /// </summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Übernimmt geänderte Kamerawerte und merkt sie sich für dieses Fahrzeug.
    /// </summary>
    public void Adjust(CameraCalibration calibration)
    {
        Calibration = calibration with { CarName = _carName ?? calibration.CarName };
        _carName = Calibration.CarName;

        try
        {
            _store.Save(Calibration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Gemerkt wird für das nächste Mal; für diese Sitzung steht der Wert
            // bereits. Ein Schreibfehler darf das Kalibrieren nicht abbrechen.
        }
    }

    /// <summary>
    /// Rechnet den aktuellen Zustand in ein Band auf dem Bildschirm um.
    /// </summary>
    /// <param name="state">Der Zustand des Coaches.</param>
    /// <param name="viewportWidth">Breite der Zeichenfläche.</param>
    /// <param name="viewportHeight">Höhe der Zeichenfläche.</param>
    /// <remarks>
    /// Breite und Höhe dürfen in geräteunabhängigen Einheiten kommen: Die
    /// Brennweite wächst mit der Breite, die Ablagen im Bild wachsen mit der
    /// Brennweite – der Maßstab kürzt sich heraus.
    /// </remarks>
    public IReadOnlyList<RibbonPoint> Build(CoachState state, float viewportWidth, float viewportHeight)
    {
        // Vor dem ersten Layout-Durchlauf hat die Zeichenfläche noch keine
        // Größe. Das ist kein Zustand, über den jemand etwas lesen möchte –
        // beim nächsten Bild steht sie.
        if (viewportWidth < 1f || viewportHeight < 1f)
        {
            return [];
        }

        UseCar(state.Session.CarName);

        IdealLine? line = state.IdealLine;
        TelemetryFrame frame = state.Frame;

        if (line is null)
        {
            Status = state.ValidLapCount switch
            {
                0 => "Ideallinie: noch keine gültige Runde gefahren",
                var laps => $"Ideallinie: wird gelernt ({laps} Runden)",
            };

            return [];
        }

        if (!state.IsPoseLearned)
        {
            // Ohne die gelernte Umrechnung des Gierwinkels wäre die
            // Blickrichtung geraten. Eine Linie, die irgendwo liegt, ist
            // schlimmer als gar keine – ihr folgt man in die Wand.
            Status = "Ideallinie: Blickrichtung noch nicht eingeordnet";
            return [];
        }

        if (!frame.IsDriving)
        {
            Status = string.Empty;
            return [];
        }

        TrackCamera? camera = TrackCamera.Create(
            in frame, Calibration, state.Pose, viewportWidth, viewportHeight);

        if (camera is null)
        {
            Status = "Ideallinie: Kalibrierung unplausibel";
            return [];
        }

        Status = string.Empty;
        return LineRibbon.Build(line, camera, frame.LapDistance);
    }

    private void UseCar(string carName)
    {
        if (carName == _carName)
        {
            return;
        }

        _carName = carName;
        Calibration = _store.Load(carName);
    }
}
