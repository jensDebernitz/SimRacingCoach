using System.Diagnostics;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Telemetry.Simulation;

/// <summary>
/// Erzeugt plausible Telemetrie ohne laufendes AMS2. Gedacht zum Einrichten des
/// Overlays und zum Testen der Auswertung.
/// </summary>
/// <remarks>
/// Das Geschwindigkeitsprofil entsteht aus einem Vorwärts-/Rückwärtslauf über
/// die Tempolimits der Strecke: vorwärts begrenzt die Motorleistung, rückwärts
/// die Bremsverzögerung. Daraus ergeben sich automatisch Bremspunkte an den
/// richtigen Stellen. Jede Runde bekommt leicht andere Kurvenfaktoren, damit
/// Delta und Kurvenanalyse etwas zu vergleichen haben.
/// </remarks>
public sealed class SimulatedTelemetrySource : ITelemetrySource
{
    private const double UpdateHz = 90.0;
    private const float MaxAcceleration = SimTrack.MaxAcceleration;
    private const float MaxDeceleration = SimTrack.MaxDeceleration;
    private const float WheelRadius = 0.33f;      // m, für die Umrechnung in Rad-Umdrehungen
    private const int GearCount = 6;

    private readonly SimTrack _track;
    private readonly Random _random;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SessionInfo _session;

    private Thread? _thread;
    private CancellationTokenSource? _cts;

    private float[] _speedProfile = [];
    private float _distance;
    private float _lapTime;
    private float _lastLapTime;
    private float _bestLapTime = -1f;
    private int _lapsCompleted;
    private float _lockupUntil;

    public SimulatedTelemetrySource(SimTrack? track = null, int seed = 1234)
    {
        _track = track ?? SimTrack.Default;
        _random = new Random(seed);
        _session = new SessionInfo(
            _track.Name,
            "Simulator",
            _track.Length,
            "Sim-Fahrzeug",
            "Training",
            NumSectors: 3);

        StartNewLap();
    }

    public event Action<TelemetrySnapshot>? FrameReceived;

    public event Action<SessionInfo>? SessionChanged;

    public event Action<TelemetryStatus>? StatusChanged;

    public TelemetryStatus Status { get; private set; } =
        new(true, "Simulator läuft", "Kein AMS2 nötig – synthetische Daten.");

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Run(_cts.Token))
        {
            IsBackground = true,
            Name = "Telemetry Simulator",
        };
        _thread.Start();

        SessionChanged?.Invoke(_session);
        StatusChanged?.Invoke(Status);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void Run(CancellationToken token)
    {
        var period = TimeSpan.FromSeconds(1.0 / UpdateHz);
        TimeSpan next = _clock.Elapsed;
        float dt = (float)period.TotalSeconds;

        while (!token.IsCancellationRequested)
        {
            next += period;
            Advance(dt);
            FrameReceived?.Invoke(new TelemetrySnapshot(_session, BuildFrame()));

            TimeSpan remaining = next - _clock.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                token.WaitHandle.WaitOne(remaining);
            }
            else
            {
                next = _clock.Elapsed;
            }
        }
    }

    /// <summary>Würfelt ein neues Fahrerprofil für die kommende Runde aus.</summary>
    private void StartNewLap()
    {
        var scales = new float[_track.Corners.Count];
        for (int i = 0; i < scales.Length; i++)
        {
            // Der simulierte Fahrer trifft jede Kurve unterschiedlich gut.
            scales[i] = 0.90f + (float)_random.NextDouble() * 0.13f;
        }

        _speedProfile = SpeedProfileSolver.Solve(
            _track.BuildSpeedLimits(scales),
            SimTrack.StepMetres,
            MaxAcceleration,
            MaxDeceleration);
    }

    private void Advance(float dt)
    {
        float speed = SpeedAt(_distance);
        _distance += speed * dt;
        _lapTime += dt;

        if (_distance >= _track.Length)
        {
            _distance -= _track.Length;
            _lastLapTime = _lapTime;
            _bestLapTime = _bestLapTime < 0f ? _lapTime : MathF.Min(_bestLapTime, _lapTime);
            _lapTime = 0f;
            _lapsCompleted++;
            StartNewLap();
        }

        // Gelegentlich blockiert ein Vorderrad – damit die Fehlererkennung etwas zu tun hat.
        if (_lockupUntil < _lapTime && _random.NextDouble() < 0.0015)
        {
            _lockupUntil = _lapTime + 0.4f;
        }
    }

    private TelemetryFrame BuildFrame()
    {
        float speed = SpeedAt(_distance);
        float ahead = SpeedAt(_distance + SimTrack.StepMetres);
        float behind = SpeedAt(_distance - SimTrack.StepMetres);

        // Längsbeschleunigung aus dem Profil: dv/dt = v * dv/ds
        float dvds = (ahead - behind) / (2f * SimTrack.StepMetres);
        float longitudinal = speed * dvds;

        float throttle = longitudinal > 0f ? MathF.Min(1f, longitudinal / MaxAcceleration) : 0f;
        float brake = longitudinal < 0f ? MathF.Min(1f, -longitudinal / MaxDeceleration) : 0f;

        float curvature = _track.CurvatureAt(_distance);
        float yawRate = curvature * speed;
        float lateral = curvature * speed * speed;
        float steering = Math.Clamp(curvature * 220f, -1f, 1f);

        int gear = Math.Clamp(1 + (int)(speed / (_track.TopSpeed / GearCount)), 1, GearCount);
        float gearBand = _track.TopSpeed / GearCount;
        float rpmFraction = Math.Clamp(0.35f + (speed - (gear - 1) * gearBand) / gearBand * 0.6f, 0.2f, 1f);

        float wheelRps = speed / (2f * MathF.PI * WheelRadius);
        bool locked = _lapTime < _lockupUntil && brake > 0.3f;
        var tyreRps = new Wheels4(
            locked ? wheelRps * 0.15f : wheelRps,
            locked ? wheelRps * 0.20f : wheelRps,
            wheelRps,
            wheelRps);

        return new TelemetryFrame
        {
            Timestamp = _clock.Elapsed.TotalSeconds,
            GameState = GameState.InGamePlaying,
            SessionState = SessionState.Practice,
            RaceState = RaceState.Racing,
            PitMode = PitMode.None,
            LapDistance = _distance,
            // Von Runde zu Runde eine etwas andere Linie – sonst lernt der
            // Coach eine Strecke ohne Breite und die Ideallinie hätte keinen
            // Spielraum.
            WorldPosition = _track.PointAt(
                _distance,
                _track.RacingOffsetAt(_distance, 1f + (MathF.Sin(_lapsCompleted * 2.4f) * 0.25f))),
            Pose = new CarPose(_track.HeadingAt(_distance), 0f, 0f),
            CurrentLapTime = _lapTime,
            LastLapTime = _lastLapTime,
            BestLapTime = _bestLapTime,
            LapsCompleted = _lapsCompleted,
            CurrentLap = _lapsCompleted + 1,
            CurrentSector = (int)(_distance / (_track.Length / 3f)),
            LapInvalidated = false,
            Speed = speed,
            Rpm = rpmFraction * 7500f,
            MaxRpm = 7500f,
            Gear = gear,
            NumGears = GearCount,
            Throttle = throttle,
            Brake = brake,
            Clutch = 0f,
            Steering = steering,
            YawRate = yawRate,
            LateralAcceleration = lateral,
            LongitudinalAcceleration = longitudinal,
            TyreRps = tyreRps,
            Terrain = new TerrainSet(Terrain.Road, Terrain.Road, Terrain.Road, Terrain.Road),
            AbsActive = locked,
            CarFlags = CarFlags.EngineActive,
            BrakeBias = 0.56f,
        };
    }

    /// <summary>Linear interpolierte Geschwindigkeit an einer beliebigen Distanz.</summary>
    private float SpeedAt(float distance)
    {
        float position = distance / SimTrack.StepMetres;
        int index = (int)MathF.Floor(position);
        float fraction = position - index;

        float a = _speedProfile[_track.Wrap(index)];
        float b = _speedProfile[_track.Wrap(index + 1)];
        return a + (b - a) * fraction;
    }

    public void Dispose() => Stop();
}
