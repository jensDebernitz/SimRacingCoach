using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>Ergebnis einer abgeschlossenen Runde.</summary>
/// <param name="Lap">Die aufgezeichnete Runde.</param>
/// <param name="IsValid">True, wenn sie als Referenz taugt.</param>
/// <param name="InvalidReason">Grund der Ablehnung, sonst <c>null</c>.</param>
public readonly record struct LapCompleted(RecordedLap Lap, bool IsValid, string? InvalidReason);

/// <summary>
/// Zeichnet die laufende Runde distanzbasiert auf und meldet fertige Runden.
/// </summary>
public sealed class LapRecorder
{
    /// <summary>
    /// Abschnittsbreite in Metern. 5 m ist fein genug, um Bremspunkte auf
    /// wenige Meter genau zu vergleichen, und grob genug, dass eine Runde
    /// wenige Tausend Werte bleibt.
    /// </summary>
    public const float DefaultBinSize = 5f;

    /// <summary>Anteil der Abschnitte, die belegt sein müssen, damit die Runde zählt.</summary>
    private const float RequiredCoverage = 0.97f;

    /// <summary>Ein größerer Distanzsprung ohne Rundenwechsel heißt: zurückgesetzt oder teleportiert.</summary>
    private const float MaxPlausibleJump = 60f;

    private readonly float _binSize;

    private SessionInfo _session = SessionInfo.Unknown;
    private LapChannels? _channels;
    private int _binCount;

    private bool _hasPrevious;
    private float _previousDistance;
    private float _previousTime;
    private WorldPoint _previousPosition;
    private int _lastFilledBin = -1;
    private int _firstFilledBin = -1;
    private int _previousLapsCompleted;
    private string? _invalidReason;

    public LapRecorder(float binSize = DefaultBinSize) => _binSize = binSize;

    /// <summary>Wird ausgelöst, sobald eine Runde über Start/Ziel abgeschlossen wurde.</summary>
    public event Action<LapCompleted>? LapFinished;

    /// <summary>Anteil der aktuellen Runde, der bereits aufgezeichnet ist (0..1).</summary>
    public float CurrentCoverage => _binCount == 0 || _lastFilledBin < 0
        ? 0f
        : (float)(_lastFilledBin + 1) / _binCount;

    /// <summary>Setzt den Recorder auf eine neue Strecke/Fahrzeug-Kombination.</summary>
    public void ResetSession(SessionInfo session)
    {
        _session = session;
        _binCount = session.IsUsable ? (int)MathF.Ceiling(session.TrackLength / _binSize) + 1 : 0;
        _channels = _binCount > 0 ? LapChannels.Allocate(_binCount) : null;
        StartNewLap();
        _previousLapsCompleted = -1;
    }

    /// <summary>Verarbeitet einen Telemetrie-Frame.</summary>
    public void Accept(in TelemetryFrame frame)
    {
        if (_channels is null || _binCount == 0)
        {
            return;
        }

        if (!frame.IsDriving)
        {
            // Pause, Menü oder Replay: laufende Runde verwerfen und neu ansetzen.
            Invalidate("Nicht auf der Strecke");
            _hasPrevious = false;
            return;
        }

        if (_previousLapsCompleted < 0)
        {
            _previousLapsCompleted = frame.LapsCompleted;
        }

        bool crossedLine =
            frame.LapsCompleted > _previousLapsCompleted ||
            (_hasPrevious && frame.LapDistance < _previousDistance - _session.TrackLength * 0.5f);

        if (crossedLine)
        {
            FinishLap(in frame);
            _previousLapsCompleted = frame.LapsCompleted;

            // Nach der Linie beginnt die neue Runde bei Distanz 0 und Zeit 0.
            _previousDistance = 0f;
            _previousTime = 0f;
            _previousPosition = frame.WorldPosition;
            _hasPrevious = true;
        }

        if (frame.LapInvalidated)
        {
            Invalidate("Runde vom Spiel ungültig gewertet");
        }

        if (frame.IsInPitLane)
        {
            Invalidate("Boxengasse");
        }

        if (!_hasPrevious)
        {
            _previousDistance = frame.LapDistance;
            _previousTime = frame.CurrentLapTime;
            _previousPosition = frame.WorldPosition;
            _lastFilledBin = (int)(frame.LapDistance / _binSize) - 1;
            _hasPrevious = true;
            return;
        }

        float travelled = frame.LapDistance - _previousDistance;
        if (travelled < 0f || travelled > MaxPlausibleJump)
        {
            // Rückwärts fahren oder Sprung: Aufzeichnung dieser Runde ist wertlos.
            Invalidate("Unplausibler Distanzsprung");
            _previousDistance = frame.LapDistance;
            _previousTime = frame.CurrentLapTime;
            _previousPosition = frame.WorldPosition;
            _lastFilledBin = (int)(frame.LapDistance / _binSize);
            return;
        }

        FillBins(in frame, travelled);

        _previousDistance = frame.LapDistance;
        _previousTime = frame.CurrentLapTime;
        _previousPosition = frame.WorldPosition;
    }

    /// <summary>
    /// Trägt alle Abschnittskanten ein, die zwischen dem letzten und diesem
    /// Frame überfahren wurden. Die Werte werden auf die Kante interpoliert –
    /// bei 250 km/h liegen zwei Frames sonst schon 1 m auseinander, was das
    /// Delta sichtbar verrauschen würde.
    /// </summary>
    private void FillBins(in TelemetryFrame frame, float travelled)
    {
        int targetBin = Math.Min((int)(frame.LapDistance / _binSize), _binCount - 1);
        if (targetBin <= _lastFilledBin || travelled <= 0f)
        {
            return;
        }

        LapChannels channels = _channels!;

        for (int bin = Math.Max(_lastFilledBin + 1, 0); bin <= targetBin; bin++)
        {
            float edge = bin * _binSize;
            float t = Math.Clamp((edge - _previousDistance) / travelled, 0f, 1f);

            channels.Time[bin] = Lerp(_previousTime, frame.CurrentLapTime, t);
            channels.Speed[bin] = frame.Speed;
            channels.Throttle[bin] = frame.Throttle;
            channels.Brake[bin] = frame.Brake;
            channels.Steering[bin] = frame.Steering;
            channels.YawRate[bin] = frame.YawRate;
            channels.Gear[bin] = frame.Gear;

            // Die Position wird interpoliert wie die Zeit, nicht wie Gas oder
            // Bremse: ein Knick von einem Meter fällt in einer gezeichneten
            // Linie sofort auf, in einem Pedalverlauf niemand.
            WorldPoint position = WorldPoint.Lerp(_previousPosition, frame.WorldPosition, t);
            channels.PositionX[bin] = position.X;
            channels.PositionY[bin] = position.Y;
            channels.PositionZ[bin] = position.Z;
            channels.OffTrackWheels[bin] = frame.Terrain.OffTrackWheels;
            channels.Yaw[bin] = frame.Pose.Yaw;

            if (_firstFilledBin < 0)
            {
                _firstFilledBin = bin;
            }
        }

        _lastFilledBin = targetBin;
    }

    private void FinishLap(in TelemetryFrame frame)
    {
        LapChannels? channels = _channels;
        if (channels is null)
        {
            StartNewLap();
            return;
        }

        // Die letzten Abschnitte bis Start/Ziel mit dem letzten Zustand auffüllen.
        int last = Math.Max(_lastFilledBin, 0);
        int missing = _binCount - (_lastFilledBin + 1);
        WorldPoint lastPosition = channels.PositionOf(last);

        for (int bin = _lastFilledBin + 1; bin < _binCount; bin++)
        {
            channels.Time[bin] = _previousTime;
            channels.Speed[bin] = channels.Speed[last];
            channels.Throttle[bin] = channels.Throttle[last];
            channels.Brake[bin] = channels.Brake[last];
            channels.Steering[bin] = channels.Steering[last];
            channels.YawRate[bin] = channels.YawRate[last];
            channels.Gear[bin] = channels.Gear[last];
            channels.OffTrackWheels[bin] = channels.OffTrackWheels[last];
            channels.Yaw[bin] = channels.Yaw[last];

            // Beim Zustand genügt Wiederholen, bei der Position nicht: ein
            // Stapel identischer Punkte auf der Ziellinie sähe in der Karte wie
            // ein Stillstand aus. Der Frame steht bereits knapp hinter der
            // Linie, also da, wo die Runde begonnen hat – dorthin wird
            // gleichmäßig weiterinterpoliert.
            WorldPoint position = WorldPoint.Lerp(
                lastPosition, frame.WorldPosition, (float)(bin - _lastFilledBin) / missing);
            channels.PositionX[bin] = position.X;
            channels.PositionY[bin] = position.Y;
            channels.PositionZ[bin] = position.Z;
        }

        string? reason = _invalidReason;

        bool startedAtLine = _firstFilledBin is >= 0 and <= 2;
        float coverage = _binCount == 0 ? 0f : (float)(_lastFilledBin + 1) / _binCount;

        if (reason is null && !startedAtLine)
        {
            reason = "Aufzeichnung begann nicht an Start/Ziel";
        }

        if (reason is null && coverage < RequiredCoverage)
        {
            reason = $"Nur {coverage:P0} der Runde aufgezeichnet";
        }

        float lapTime = DetermineLapTime(in frame);
        if (reason is null && lapTime <= 1f)
        {
            reason = "Keine plausible Rundenzeit";
        }

        var lap = new RecordedLap
        {
            ReferenceKey = _session.ReferenceKey,
            TrackName = _session.TrackDisplayName,
            CarName = _session.CarName,
            TrackLength = _session.TrackLength,
            BinSize = _binSize,
            LapTime = lapTime,
            RecordedAt = DateTimeOffset.Now,
            Channels = channels,
        };

        LapFinished?.Invoke(new LapCompleted(lap, reason is null, reason));

        StartNewLap();
    }

    /// <summary>
    /// Bevorzugt die Rundenzeit des Spiels. Sie steht erst kurz nach der Linie
    /// bereit, deshalb dient die selbst gemessene Zeit als Rückfallebene und
    /// als Plausibilitätsprüfung.
    /// </summary>
    private float DetermineLapTime(in TelemetryFrame frame)
    {
        float measured = _previousTime;
        float reported = frame.LastLapTime;

        if (reported > 1f && MathF.Abs(reported - measured) < 1.0f)
        {
            return reported;
        }

        return measured;
    }

    private void StartNewLap()
    {
        _channels = _binCount > 0 ? LapChannels.Allocate(_binCount) : null;
        _lastFilledBin = -1;
        _firstFilledBin = -1;
        _invalidReason = null;
    }

    private void Invalidate(string reason) => _invalidReason ??= reason;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
