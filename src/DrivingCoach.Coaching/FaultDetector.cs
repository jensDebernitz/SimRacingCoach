using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>Art eines erkannten Fahrfehlers.</summary>
public enum FaultKind
{
    /// <summary>Ein Rad steht beim Bremsen praktisch still.</summary>
    Lockup,

    /// <summary>Angetriebene Räder drehen schneller als der Rest.</summary>
    Wheelspin,

    /// <summary>Alle vier Räder abseits der Strecke.</summary>
    OffTrack,

    /// <summary>Am Begrenzer, ohne hochzuschalten.</summary>
    OverRev,

    /// <summary>Deutlich unter der Nenndrehzahl hochgeschaltet.</summary>
    ShortShift,

    /// <summary>Weder Gas noch Bremse über längere Zeit.</summary>
    Coasting,
}

/// <summary>Ein erkannter Fahrfehler.</summary>
/// <param name="Kind">Art des Fehlers.</param>
/// <param name="Text">Hinweis fürs Overlay.</param>
/// <param name="SpeechText">Kurzfassung für die Sprachausgabe.</param>
/// <param name="At">Zeitstempel des Frames in Sekunden.</param>
public readonly record struct DrivingFault(FaultKind Kind, string Text, string SpeechText, double At);

/// <summary>
/// Erkennt typische Anfängerfehler direkt im laufenden Frame-Strom.
/// </summary>
/// <remarks>
/// Alle Schwellen arbeiten mit einer Mindestdauer und einer Sperrzeit je
/// Fehlerart. Ohne beides würde ein kurzes Zucken der Werte eine Kaskade von
/// Meldungen auslösen – beim Fahren wäre das reine Ablenkung.
/// </remarks>
public sealed class FaultDetector
{
    private static readonly IReadOnlyDictionary<FaultKind, double> Cooldowns = new Dictionary<FaultKind, double>
    {
        [FaultKind.Lockup] = 8.0,
        [FaultKind.Wheelspin] = 8.0,
        [FaultKind.OffTrack] = 6.0,
        [FaultKind.OverRev] = 12.0,
        [FaultKind.ShortShift] = 12.0,
        [FaultKind.Coasting] = 15.0,
    };

    /// <summary>Ein Rad unterhalb dieses Anteils der schnellsten Radrehzahl gilt als blockiert.</summary>
    private const float LockupRatio = 0.55f;

    /// <summary>Überschreitet ein Radpaar das andere um diesen Faktor, dreht es durch.</summary>
    private const float WheelspinRatio = 1.12f;

    /// <summary>Anteil der Maximaldrehzahl, ab dem "im Begrenzer" gilt.</summary>
    private const float OverRevFraction = 0.985f;

    /// <summary>Unterhalb dieses Anteils ist ein Hochschalten verfrüht.</summary>
    private const float ShortShiftFraction = 0.80f;

    private readonly Sustain _lockup = new();
    private readonly Sustain _wheelspin = new();
    private readonly Sustain _offTrack = new();
    private readonly Sustain _overRev = new();
    private readonly Sustain _coasting = new();
    private readonly Dictionary<FaultKind, double> _lastReported = [];

    private int _previousGear;
    private float _peakRpmFraction;
    private bool _frontWheelLocked;

    /// <summary>Wird ausgelöst, sobald ein Fehler als gesichert gilt.</summary>
    public event Action<DrivingFault>? FaultDetected;

    /// <summary>Setzt alle Zustände zurück, etwa bei Sitzungswechsel.</summary>
    public void Reset()
    {
        _lockup.Reset();
        _wheelspin.Reset();
        _offTrack.Reset();
        _overRev.Reset();
        _coasting.Reset();
        _lastReported.Clear();
        _previousGear = 0;
        _peakRpmFraction = 0f;
    }

    /// <summary>Prüft einen Frame auf Fahrfehler.</summary>
    public void Accept(in TelemetryFrame frame)
    {
        if (!frame.IsDriving || frame.IsInPitLane)
        {
            Reset();
            return;
        }

        double now = frame.Timestamp;

        CheckLockup(in frame, now);
        CheckWheelspin(in frame, now);
        CheckOffTrack(in frame, now);
        CheckShiftBehaviour(in frame, now);
        CheckCoasting(in frame, now);
    }

    private void CheckLockup(in TelemetryFrame frame, double now)
    {
        Wheels4 rps = frame.TyreRps;
        float fastest = rps.Max;

        bool active =
            frame.Brake > 0.25f &&
            frame.Speed > 8f &&
            fastest > 1f &&
            rps.Min < fastest * LockupRatio;

        if (active)
        {
            _frontWheelLocked = MathF.Min(rps.FrontLeft, rps.FrontRight) < fastest * LockupRatio;
        }

        if (!_lockup.Update(active, now, requiredSeconds: 0.12))
        {
            return;
        }

        // Wenn ABS regelt, ist das Blockieren gewollt abgefangen – dann ist der
        // eigentliche Hinweis "zu viel Bremsdruck", nicht "Rad blockiert".
        (string text, string speech) = frame.AbsActive
            ? ("ABS regelt stark – Bremsdruck früher aufbauen und dann lösen",
               "Zu viel Bremsdruck.")
            : _frontWheelLocked
                ? ("Vorderrad blockiert – Bremsdruck lösen, sonst schiebt das Auto geradeaus",
                   "Vorderrad blockiert.")
                : ("Hinterrad blockiert – Gefahr des Ausbrechens beim Anbremsen",
                   "Hinterrad blockiert.");

        Report(FaultKind.Lockup, text, speech, now);
    }

    private void CheckWheelspin(in TelemetryFrame frame, double now)
    {
        Wheels4 rps = frame.TyreRps;

        // Beim Bremsen sind die Räder ohnehin ungleich schnell – hier nur Traktion prüfen.
        bool active =
            frame.Throttle > 0.4f &&
            frame.Brake < 0.05f &&
            frame.Speed > 3f &&
            rps.FrontMax > 1f &&
            rps.RearMax > 1f &&
            (rps.RearMax > rps.FrontMax * WheelspinRatio || rps.FrontMax > rps.RearMax * WheelspinRatio);

        if (_wheelspin.Update(active, now, requiredSeconds: 0.15))
        {
            Report(
                FaultKind.Wheelspin,
                "Räder drehen durch – Gas weicher öffnen, besonders beim Herausbeschleunigen",
                "Zu viel Gas.",
                now);
        }
    }

    private void CheckOffTrack(in TelemetryFrame frame, double now)
    {
        if (_offTrack.Update(frame.Terrain.AllWheelsOffTrack && frame.Speed > 5f, now, requiredSeconds: 0.3))
        {
            Report(
                FaultKind.OffTrack,
                "Alle vier Räder neben der Strecke – die Runde zählt nicht mehr",
                "Von der Strecke ab.",
                now);
        }
    }

    private void CheckShiftBehaviour(in TelemetryFrame frame, double now)
    {
        float fraction = frame.RpmFraction;

        if (_previousGear == 0)
        {
            _previousGear = frame.Gear;
        }

        // Hochschalten erkannt: die kurz zuvor erreichte Spitzendrehzahl bewerten.
        if (frame.Gear > _previousGear && _previousGear >= 1)
        {
            if (_peakRpmFraction is > 0.1f and < ShortShiftFraction)
            {
                Report(
                    FaultKind.ShortShift,
                    $"Zu früh hochgeschaltet ({_peakRpmFraction:P0} der Maximaldrehzahl) – dreh weiter aus",
                    "Du schaltest zu früh hoch.",
                    now);
            }

            _peakRpmFraction = 0f;
        }
        else if (frame.Gear < _previousGear)
        {
            _peakRpmFraction = 0f;
        }
        else
        {
            _peakRpmFraction = MathF.Max(_peakRpmFraction, fraction);
        }

        _previousGear = frame.Gear;

        bool atLimiter =
            fraction > OverRevFraction &&
            frame.Throttle > 0.9f &&
            frame.Gear > 0 &&
            frame.Gear < frame.NumGears;

        if (_overRev.Update(atLimiter, now, requiredSeconds: 0.5))
        {
            Report(
                FaultKind.OverRev,
                "Du hängst im Begrenzer – früher hochschalten",
                "Hochschalten.",
                now);
        }
    }

    private void CheckCoasting(in TelemetryFrame frame, double now)
    {
        bool active = frame.Throttle < 0.03f && frame.Brake < 0.03f && frame.Speed > 15f;

        if (_coasting.Update(active, now, requiredSeconds: 1.5))
        {
            Report(
                FaultKind.Coasting,
                "Langes Rollen ohne Gas und Bremse – entweder bremsen oder beschleunigen",
                "Nicht rollen lassen.",
                now);
        }
    }

    private void Report(FaultKind kind, string text, string speech, double now)
    {
        if (_lastReported.TryGetValue(kind, out double last) && now - last < Cooldowns[kind])
        {
            return;
        }

        _lastReported[kind] = now;
        FaultDetected?.Invoke(new DrivingFault(kind, text, speech, now));
    }

    /// <summary>
    /// Verfolgt, wie lange eine Bedingung ununterbrochen zutrifft, und meldet
    /// den Ablauf der geforderten Dauer genau einmal je Ereignis.
    /// </summary>
    private sealed class Sustain
    {
        private double _activeSince = double.NaN;
        private bool _fired;

        public bool Update(bool active, double now, double requiredSeconds)
        {
            if (!active)
            {
                _activeSince = double.NaN;
                _fired = false;
                return false;
            }

            if (double.IsNaN(_activeSince))
            {
                _activeSince = now;
                return false;
            }

            if (_fired || now - _activeSince < requiredSeconds)
            {
                return false;
            }

            _fired = true;
            return true;
        }

        public void Reset()
        {
            _activeSince = double.NaN;
            _fired = false;
        }
    }
}
