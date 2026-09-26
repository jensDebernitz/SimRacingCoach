using DrivingCoach.Coaching;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Tests;

public class FaultDetectorTests
{
    private const float UpdateHz = 90f;
    private const float WheelRadius = 0.33f;

    /// <summary>Lässt den Detektor eine Folge gleichartiger Frames sehen.</summary>
    private static List<DrivingFault> Run(
        FaultDetector detector,
        Func<int, double, TelemetryFrame> build,
        int frames,
        double startTime = 0.0)
    {
        var faults = new List<DrivingFault>();
        detector.FaultDetected += fault => faults.Add(fault);

        for (int i = 0; i < frames; i++)
        {
            TelemetryFrame frame = build(i, startTime + i / (double)UpdateHz);
            detector.Accept(in frame);
        }

        return faults;
    }

    private static TelemetryFrame Cruising(double timestamp, float speed = 40f) => new()
    {
        Timestamp = timestamp,
        GameState = GameState.InGamePlaying,
        SessionState = SessionState.Practice,
        RaceState = RaceState.Racing,
        PitMode = PitMode.None,
        Speed = speed,
        Rpm = 4500f,
        MaxRpm = 7500f,
        Gear = 4,
        NumGears = 6,
        Throttle = 0.6f,
        Brake = 0f,
        TyreRps = Rps(speed),
        Terrain = new TerrainSet(Terrain.Road, Terrain.Road, Terrain.Road, Terrain.Road),
        CarFlags = CarFlags.EngineActive,
        BrakeBias = 0.56f,
    };

    private static Wheels4 Rps(float speed)
    {
        float rps = speed / (2f * MathF.PI * WheelRadius);
        return new Wheels4(rps, rps, rps, rps);
    }

    [Fact]
    public void Meldet_ein_blockierendes_Vorderrad()
    {
        var detector = new FaultDetector();

        List<DrivingFault> faults = Run(detector, (_, t) =>
        {
            TelemetryFrame frame = Cruising(t, speed: 45f);
            float rps = 45f / (2f * MathF.PI * WheelRadius);

            return frame with
            {
                Throttle = 0f,
                Brake = 0.9f,
                // Vorne links steht das Rad praktisch still.
                TyreRps = new Wheels4(rps * 0.05f, rps, rps, rps),
            };
        }, frames: 40);

        DrivingFault fault = Assert.Single(faults, f => f.Kind == FaultKind.Lockup);
        Assert.Contains("Vorderrad blockiert", fault.Text);
    }

    [Fact]
    public void Nennt_bei_aktivem_ABS_den_Bremsdruck_statt_des_Rades()
    {
        var detector = new FaultDetector();

        List<DrivingFault> faults = Run(detector, (_, t) =>
        {
            TelemetryFrame frame = Cruising(t, speed: 45f);
            float rps = 45f / (2f * MathF.PI * WheelRadius);

            return frame with
            {
                Throttle = 0f,
                Brake = 1f,
                AbsActive = true,
                TyreRps = new Wheels4(rps * 0.3f, rps, rps, rps),
            };
        }, frames: 40);

        DrivingFault fault = Assert.Single(faults, f => f.Kind == FaultKind.Lockup);
        Assert.Contains("ABS", fault.Text);
    }

    [Fact]
    public void Meldet_durchdrehende_Hinterraeder()
    {
        var detector = new FaultDetector();

        List<DrivingFault> faults = Run(detector, (_, t) =>
        {
            TelemetryFrame frame = Cruising(t, speed: 25f);
            float rps = 25f / (2f * MathF.PI * WheelRadius);

            return frame with
            {
                Throttle = 1f,
                Brake = 0f,
                TyreRps = new Wheels4(rps, rps, rps * 1.4f, rps * 1.4f),
            };
        }, frames: 40);

        DrivingFault fault = Assert.Single(faults, f => f.Kind == FaultKind.Wheelspin);
        Assert.Contains("Gas", fault.Text);
    }

    [Fact]
    public void Meldet_das_Verlassen_der_Strecke()
    {
        var detector = new FaultDetector();

        List<DrivingFault> faults = Run(detector, (_, t) => Cruising(t) with
        {
            Terrain = new TerrainSet(Terrain.Grass, Terrain.Grass, Terrain.Grass, Terrain.Grass),
        }, frames: 60);

        Assert.Single(faults, f => f.Kind == FaultKind.OffTrack);
    }

    [Fact]
    public void Meldet_zu_fruehes_Hochschalten()
    {
        var detector = new FaultDetector();

        // 20 Frames im 3. Gang bei 60 % Drehzahl, dann Schaltvorgang in den 4.
        List<DrivingFault> faults = Run(detector, (i, t) => Cruising(t) with
        {
            Gear = i < 20 ? 3 : 4,
            Rpm = 7500f * 0.60f,
        }, frames: 30);

        DrivingFault fault = Assert.Single(faults, f => f.Kind == FaultKind.ShortShift);
        Assert.Contains("zu früh hochgeschaltet", fault.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Meldet_langes_Rollen_ohne_Gas_und_Bremse()
    {
        var detector = new FaultDetector();

        List<DrivingFault> faults = Run(detector, (_, t) => Cruising(t) with
        {
            Throttle = 0f,
            Brake = 0f,
        }, frames: (int)(UpdateHz * 3));

        Assert.Single(faults, f => f.Kind == FaultKind.Coasting);
    }

    [Fact]
    public void Kurzes_Zucken_loest_keine_Meldung_aus()
    {
        var detector = new FaultDetector();

        // Ein einzelner Frame mit blockiertem Rad – typisches Messrauschen.
        List<DrivingFault> faults = Run(detector, (i, t) =>
        {
            TelemetryFrame frame = Cruising(t, speed: 45f) with { Throttle = 0f, Brake = 0.9f };
            float rps = 45f / (2f * MathF.PI * WheelRadius);

            return i == 10
                ? frame with { TyreRps = new Wheels4(rps * 0.05f, rps, rps, rps) }
                : frame;
        }, frames: 40);

        Assert.DoesNotContain(faults, f => f.Kind == FaultKind.Lockup);
    }

    [Fact]
    public void Wiederholt_dieselbe_Meldung_nicht_sofort()
    {
        var detector = new FaultDetector();

        // Durchgehend blockiertes Rad über fünf Sekunden: eine Meldung, nicht 450.
        List<DrivingFault> faults = Run(detector, (_, t) =>
        {
            TelemetryFrame frame = Cruising(t, speed: 45f);
            float rps = 45f / (2f * MathF.PI * WheelRadius);

            return frame with
            {
                Throttle = 0f,
                Brake = 0.9f,
                TyreRps = new Wheels4(rps * 0.05f, rps, rps, rps),
            };
        }, frames: (int)(UpdateHz * 5));

        Assert.Single(faults, f => f.Kind == FaultKind.Lockup);
    }

    [Fact]
    public void Saubere_Referenzrunde_loest_keine_Fehlermeldung_aus()
    {
        var detector = new FaultDetector();
        var faults = new List<DrivingFault>();
        detector.FaultDetected += fault => faults.Add(fault);

        foreach (TelemetryFrame frame in VirtualDriver.Frames(VirtualDriver.NeutralStyles()))
        {
            detector.Accept(in frame);
        }

        // Rollen ohne Gas und Bremse gibt es im synthetischen Profil auf den
        // Kurvenplateaus tatsächlich – nur die Fahrfehler dürfen ausbleiben.
        Assert.DoesNotContain(faults, f => f.Kind is FaultKind.Lockup or FaultKind.Wheelspin or FaultKind.OffTrack);
    }
}
