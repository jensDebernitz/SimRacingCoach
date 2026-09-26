using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;

namespace DrivingCoach.Tests;

public class PoseConventionTests
{
    [Fact]
    public void Leitet_die_Blickrichtung_aus_der_Fahrtrichtung_ab()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();

        PoseConvention? convention = PoseConvention.Learn(lap);

        Assert.NotNull(convention);
        Assert.Equal(1f, convention!.Value.Sign);
        Assert.InRange(convention.Value.OffsetRadians, -0.05f, 0.05f);
    }

    /// <summary>
    /// Der eigentliche Zweck: Zählte AMS2 andersherum, müsste der Coach das von
    /// selbst merken – sonst läge die Linie spiegelverkehrt auf der Strecke.
    /// </summary>
    [Fact]
    public void Erkennt_den_umgekehrten_Drehsinn()
    {
        RecordedLap lap = Flipped(VirtualDriver.DriveReferenceLap());

        PoseConvention? convention = PoseConvention.Learn(lap);

        Assert.NotNull(convention);
        Assert.Equal(-1f, convention!.Value.Sign);
    }

    [Fact]
    public void Die_gelernte_Umrechnung_trifft_die_Fahrtrichtung()
    {
        RecordedLap lap = Flipped(VirtualDriver.DriveReferenceLap());
        PoseConvention convention = PoseConvention.Learn(lap)!.Value;
        LapChannels channels = lap.Channels;

        var errors = new List<float>();

        for (int bin = 0; bin < channels.Count; bin++)
        {
            if (channels.Speed[bin] < 40f)
            {
                continue;
            }

            WorldPoint from = channels.PositionOf(bin);
            WorldPoint to = channels.PositionOf((bin + 1) % channels.Count);
            float travel = MathF.Atan2(to.X - from.X, to.Z - from.Z);

            (float dx, float dz) = convention.DirectionFrom(channels.Yaw[bin]);
            errors.Add(MathF.Abs(MathF.Atan2(dx, dz) - travel));
        }

        // Gemittelt, nicht Punkt für Punkt: Der simulierte Fahrer zieht auch auf
        // der Geraden noch quer zur Fahrbahn, wenn er die Linie wechselt, und
        // dort weichen Blick- und Fahrtrichtung um einige Grad voneinander ab.
        // Bei falschem Drehsinn wären es im Mittel weit über 1 Radiant.
        Assert.InRange(errors.Average(), 0f, 0.05f);
    }

    [Fact]
    public void Ohne_Geometrie_gibt_es_nichts_zu_lernen()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        var blind = new RecordedLap
        {
            ReferenceKey = lap.ReferenceKey,
            TrackName = lap.TrackName,
            CarName = lap.CarName,
            TrackLength = lap.TrackLength,
            BinSize = lap.BinSize,
            LapTime = lap.LapTime,
            RecordedAt = lap.RecordedAt,
            Channels = new LapChannels
            {
                Time = lap.Channels.Time,
                Speed = lap.Channels.Speed,
                Throttle = lap.Channels.Throttle,
                Brake = lap.Channels.Brake,
                Steering = lap.Channels.Steering,
                YawRate = lap.Channels.YawRate,
                Gear = lap.Channels.Gear,
            },
        };

        Assert.Null(PoseConvention.Learn(blind));
    }

    /// <summary>Dieselbe Runde, aber mit umgekehrt gezähltem Gierwinkel.</summary>
    private static RecordedLap Flipped(RecordedLap lap)
    {
        float[] yaw = lap.Channels.Yaw.Select(value => -value).ToArray();

        return new RecordedLap
        {
            ReferenceKey = lap.ReferenceKey,
            TrackName = lap.TrackName,
            CarName = lap.CarName,
            TrackLength = lap.TrackLength,
            BinSize = lap.BinSize,
            LapTime = lap.LapTime,
            RecordedAt = lap.RecordedAt,
            Channels = new LapChannels
            {
                Time = lap.Channels.Time,
                Speed = lap.Channels.Speed,
                Throttle = lap.Channels.Throttle,
                Brake = lap.Channels.Brake,
                Steering = lap.Channels.Steering,
                YawRate = lap.Channels.YawRate,
                Gear = lap.Channels.Gear,
                PositionX = lap.Channels.PositionX,
                PositionY = lap.Channels.PositionY,
                PositionZ = lap.Channels.PositionZ,
                OffTrackWheels = lap.Channels.OffTrackWheels,
                Yaw = yaw,
            },
        };
    }
}

public class TrackCameraTests
{
    private const float Width = 1920f;
    private const float Height = 1080f;

    [Fact]
    public void Ein_Punkt_geradeaus_landet_in_der_Bildmitte()
    {
        TrackCamera camera = Create();

        // 50 m genau vor dem Auto, auf Höhe des Fahrzeugbezugspunktes.
        Assert.True(camera.TryProject(new WorldPoint(0f, 0f, 50f), out ScreenPoint screen));

        Assert.InRange(screen.X, (Width / 2f) - 1f, (Width / 2f) + 1f);

        // Waagerecht mittig, aber senkrecht darunter: Die Kamera sitzt über
        // dem Bezugspunkt und blickt deshalb auf ihn herab.
        Assert.True(screen.Y > Height / 2f, "Die Fahrbahn liegt nicht unterhalb der Bildmitte.");
        Assert.InRange(screen.Distance, 49f, 51f);
    }

    [Fact]
    public void Was_hinter_dem_Auto_liegt_wird_nicht_gezeichnet()
    {
        TrackCamera camera = Create();

        Assert.False(camera.TryProject(new WorldPoint(0f, 0f, -20f), out _));
    }

    [Fact]
    public void Rechts_vom_Auto_landet_rechts_im_Bild()
    {
        TrackCamera camera = Create();

        Assert.True(camera.TryProject(new WorldPoint(4f, 0f, 50f), out ScreenPoint right));
        Assert.True(camera.TryProject(new WorldPoint(-4f, 0f, 50f), out ScreenPoint left));

        Assert.True(right.X > Width / 2f, "Ein Punkt rechts der Strecke landet nicht rechts im Bild.");
        Assert.True(left.X < Width / 2f, "Ein Punkt links der Strecke landet nicht links im Bild.");
    }

    /// <summary>
    /// Die Perspektive muss stimmen, sonst sieht das Band aus wie ein Balken:
    /// Gleicher Abstand quer, doppelte Entfernung, halbe Breite im Bild.
    /// </summary>
    [Fact]
    public void Doppelte_Entfernung_ist_halbe_Breite()
    {
        TrackCamera camera = Create();

        camera.TryProject(new WorldPoint(2f, 0f, 30f), out ScreenPoint near);
        camera.TryProject(new WorldPoint(2f, 0f, 60f), out ScreenPoint far);

        float nearOffset = near.X - (Width / 2f);
        float farOffset = far.X - (Width / 2f);

        Assert.InRange(nearOffset / farOffset, 1.9f, 2.1f);
    }

    [Fact]
    public void Ein_gedrehtes_Auto_dreht_das_Bild_mit()
    {
        // 90° nach rechts gedreht: Die Blickrichtung zeigt nach +X.
        TrackCamera camera = Create(yaw: MathF.PI / 2f);

        Assert.True(camera.TryProject(new WorldPoint(50f, 0f, 0f), out ScreenPoint ahead));
        Assert.InRange(ahead.X, (Width / 2f) - 1f, (Width / 2f) + 1f);
        Assert.False(camera.TryProject(new WorldPoint(0f, 0f, 50f), out _));
    }

    [Fact]
    public void Ein_engerer_Blickwinkel_vergroessert()
    {
        TrackCamera wide = Create(fieldOfView: 90f);
        TrackCamera narrow = Create(fieldOfView: 40f);

        wide.TryProject(new WorldPoint(3f, 0f, 40f), out ScreenPoint wideScreen);
        narrow.TryProject(new WorldPoint(3f, 0f, 40f), out ScreenPoint narrowScreen);

        Assert.True(
            MathF.Abs(narrowScreen.X - (Width / 2f)) > MathF.Abs(wideScreen.X - (Width / 2f)),
            "Ein engerer Blickwinkel schiebt den Punkt nicht weiter nach außen.");
    }

    [Fact]
    public void Unsinnige_Kalibrierung_ergibt_keine_Kamera()
    {
        TelemetryFrame frame = FrameAt(0f);
        var broken = CameraCalibration.Default with { FieldOfViewDegrees = 0f };

        Assert.Null(TrackCamera.Create(in frame, broken, PoseConvention.Assumed, Width, Height));
        Assert.Null(TrackCamera.Create(in frame, CameraCalibration.Default, PoseConvention.Assumed, 0f, 0f));
    }

    private static TrackCamera Create(float yaw = 0f, float fieldOfView = CameraCalibration.DefaultFieldOfView)
    {
        TelemetryFrame frame = FrameAt(yaw);
        var calibration = CameraCalibration.Default with
        {
            FieldOfViewDegrees = fieldOfView,

            // Für die Rechnung genau mittig sitzen, damit sich die Prüfungen
            // auf die Perspektive beziehen und nicht auf den Sitzversatz.
            EyeForward = 0f,
            EyeRight = 0f,
        };

        TrackCamera? camera = TrackCamera.Create(in frame, calibration, PoseConvention.Assumed, Width, Height);
        Assert.NotNull(camera);
        return camera!;
    }

    private static TelemetryFrame FrameAt(float yaw) => new()
    {
        GameState = GameState.InGamePlaying,
        SessionState = SessionState.Practice,
        RaceState = RaceState.Racing,
        WorldPosition = new WorldPoint(0f, 0f, 0f),
        Pose = new CarPose(yaw, 0f, 0f),
    };
}
