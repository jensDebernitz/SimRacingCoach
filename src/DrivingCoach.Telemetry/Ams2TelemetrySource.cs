using System.Diagnostics;
using System.Runtime.Versioning;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Telemetry;

/// <summary>
/// Pollt den AMS2-Shared-Memory auf einem eigenen Thread und liefert
/// aufbereitete <see cref="TelemetrySnapshot"/>-Werte.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Ams2TelemetrySource : ITelemetrySource
{
    /// <summary>
    /// AMS2 aktualisiert den Block einmal pro Grafik-Frame. 120 Hz Abtastung
    /// liegt darüber, sodass kein Frame verpasst wird; doppelte Frames filtert
    /// die Sequenznummer heraus.
    /// </summary>
    private const double PollHz = 120.0;

    /// <summary>Wie oft ein Verbindungsversuch unternommen wird, solange AMS2 nicht läuft.</summary>
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);

    /// <summary>Strecke und Auto ändern sich selten – seltener prüfen spart das Dekodieren der Strings.</summary>
    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromSeconds(0.5);

    private readonly SharedMemoryReader _reader = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private SessionInfo _session = SessionInfo.Unknown;
    private TimeSpan _nextSessionCheck;
    private TimeSpan _nextConnectAttempt;
    private uint _lastSequence = uint.MaxValue;

    public event Action<TelemetrySnapshot>? FrameReceived;

    public event Action<SessionInfo>? SessionChanged;

    public event Action<TelemetryStatus>? StatusChanged;

    public TelemetryStatus Status { get; private set; } =
        new(false, "Nicht verbunden", "Warte auf Automobilista 2 …");

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
            Name = "AMS2 Telemetry",
            // Etwas über Normal, damit das Polling nicht vom UI-Thread verdrängt wird.
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
        _reader.Disconnect();
    }

    private void Run(CancellationToken token)
    {
        TimeSpan period = TimeSpan.FromSeconds(1.0 / PollHz);
        TimeSpan next = _clock.Elapsed;

        while (!token.IsCancellationRequested)
        {
            next += period;
            Poll();
            SleepUntil(next, token);

            // Nach einem Aussetzer (z. B. Alt-Tab) nicht Hunderte Frames nachholen.
            if (_clock.Elapsed - next > TimeSpan.FromMilliseconds(250))
            {
                next = _clock.Elapsed;
            }
        }
    }

    private void Poll()
    {
        if (!_reader.IsConnected)
        {
            if (_clock.Elapsed < _nextConnectAttempt)
            {
                return;
            }

            _nextConnectAttempt = _clock.Elapsed + ReconnectInterval;

            if (!_reader.TryConnect())
            {
                SetStatus(new TelemetryStatus(false, "AMS2 nicht gefunden", _reader.LastError));
                return;
            }

            SetStatus(_reader.State == ConnectionState.VersionMismatch
                ? new TelemetryStatus(true, "Verbunden (Versionswarnung)", _reader.LastError)
                : new TelemetryStatus(true, "Verbunden mit AMS2"));
        }

        if (!_reader.TryRead(out Ams2SharedMemory raw))
        {
            return;
        }

        // Das Spiel wurde beendet: der Block bleibt gemappt, meldet aber GAME_EXITED.
        if ((GameState)raw.mGameState == GameState.Exited)
        {
            _reader.Disconnect();
            _lastSequence = uint.MaxValue;
            SetStatus(new TelemetryStatus(false, "AMS2 beendet", "Warte auf Automobilista 2 …"));
            return;
        }

        // Gleiche Sequenznummer heißt: das Spiel hat seither nicht geschrieben.
        if (raw.mSequenceNumber == _lastSequence)
        {
            return;
        }

        _lastSequence = raw.mSequenceNumber;

        UpdateSessionIfChanged(in raw);
        FrameReceived?.Invoke(new TelemetrySnapshot(_session, Convert(in raw, _clock.Elapsed.TotalSeconds)));
    }

    private void UpdateSessionIfChanged(in Ams2SharedMemory raw)
    {
        if (_clock.Elapsed < _nextSessionCheck)
        {
            return;
        }

        _nextSessionCheck = _clock.Elapsed + SessionPollInterval;

        var current = new SessionInfo(
            raw.mTrackLocation.ToText(),
            raw.mTrackVariation.ToText(),
            raw.mTrackLength,
            raw.mCarName.ToText(),
            raw.mCarClassName.ToText(),
            raw.mNumSectors);

        if (current == _session)
        {
            return;
        }

        _session = current;
        SessionChanged?.Invoke(current);
    }

    /// <summary>
    /// Übersetzt den Rohblock in einen <see cref="TelemetryFrame"/>.
    /// Rundendistanz und Rundenzähler stehen nicht im Hauptblock, sondern im
    /// Eintrag des betrachteten Fahrers.
    /// </summary>
    internal static TelemetryFrame Convert(in Ams2SharedMemory raw, double timestamp)
    {
        int index = raw.mViewedParticipantIndex;
        bool hasParticipant = index >= 0 && index < Ams2Layout.StoredParticipantsMax && index < raw.mNumParticipants;

        float lapDistance = 0f;
        int lapsCompleted = 0;
        int currentLap = 0;
        int currentSector = -1;
        var position = default(WorldPoint);

        if (hasParticipant)
        {
            ParticipantInfo player = raw.mParticipantInfo[index];
            lapDistance = player.mCurrentLapDistance;
            lapsCompleted = (int)player.mLapsCompleted;
            currentLap = (int)player.mCurrentLap;
            currentSector = player.mCurrentSector;

            // Die Weltposition steht nur im Fahrereintrag, nicht im Hauptblock.
            position = new WorldPoint(
                player.mWorldPosition[Ams2Layout.VecX],
                player.mWorldPosition[Ams2Layout.VecY],
                player.mWorldPosition[Ams2Layout.VecZ]);
        }

        return new TelemetryFrame
        {
            Timestamp = timestamp,
            GameState = (GameState)raw.mGameState,
            SessionState = (SessionState)raw.mSessionState,
            RaceState = (RaceState)raw.mRaceState,
            PitMode = (PitMode)raw.mPitMode,
            LapDistance = lapDistance,
            WorldPosition = position,

            // Dieselbe Achsenzuordnung wie bei der Gierrate unten: Y ist die
            // Hochachse, also steckt die Blickrichtung in mOrientation[VecY].
            Pose = new CarPose(
                raw.mOrientation[Ams2Layout.VecY],
                raw.mOrientation[Ams2Layout.VecX],
                raw.mOrientation[Ams2Layout.VecZ]),
            CurrentLapTime = raw.mCurrentTime,
            LastLapTime = raw.mLastLapTime,
            BestLapTime = raw.mBestLapTime,
            LapsCompleted = lapsCompleted,
            CurrentLap = currentLap,
            CurrentSector = currentSector,
            LapInvalidated = raw.mLapInvalidated != 0,
            Speed = raw.mSpeed,
            Rpm = raw.mRpm,
            MaxRpm = raw.mMaxRPM,
            Gear = raw.mGear,
            NumGears = raw.mNumGears,
            Throttle = raw.mUnfilteredThrottle,
            Brake = raw.mUnfilteredBrake,
            Clutch = raw.mUnfilteredClutch,
            Steering = raw.mUnfilteredSteering,
            YawRate = raw.mAngularVelocity[Ams2Layout.VecY],
            LateralAcceleration = raw.mLocalAcceleration[Ams2Layout.VecX],
            LongitudinalAcceleration = raw.mLocalAcceleration[Ams2Layout.VecZ],
            TyreRps = new Wheels4(
                raw.mTyreRPS[Ams2Layout.TyreFrontLeft],
                raw.mTyreRPS[Ams2Layout.TyreFrontRight],
                raw.mTyreRPS[Ams2Layout.TyreRearLeft],
                raw.mTyreRPS[Ams2Layout.TyreRearRight]),
            Terrain = new TerrainSet(
                (Terrain)raw.mTerrain[Ams2Layout.TyreFrontLeft],
                (Terrain)raw.mTerrain[Ams2Layout.TyreFrontRight],
                (Terrain)raw.mTerrain[Ams2Layout.TyreRearLeft],
                (Terrain)raw.mTerrain[Ams2Layout.TyreRearRight]),
            AbsActive = raw.mAntiLockActive != 0,
            CarFlags = (CarFlags)raw.mCarFlags,
            BrakeBias = raw.mBrakeBias,
        };
    }

    private void SetStatus(TelemetryStatus status)
    {
        if (status == Status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
    }

    /// <summary>
    /// Wartet bis zum Zielzeitpunkt. <see cref="Thread.Sleep(int)"/> ist zu grob
    /// für ~8 ms Takt, deshalb grob schlafen und die letzte Millisekunde spinnen.
    /// </summary>
    private void SleepUntil(TimeSpan target, CancellationToken token)
    {
        TimeSpan remaining = target - _clock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        if (remaining > TimeSpan.FromMilliseconds(2))
        {
            token.WaitHandle.WaitOne(remaining - TimeSpan.FromMilliseconds(1));
        }

        while (_clock.Elapsed < target && !token.IsCancellationRequested)
        {
            Thread.SpinWait(40);
        }
    }

    public void Dispose()
    {
        Stop();
        _reader.Dispose();
    }
}
