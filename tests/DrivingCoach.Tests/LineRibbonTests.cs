using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

public class LineRibbonTests
{
    private const float Width = 1920f;
    private const float Height = 1080f;

    [Fact]
    public void Das_Band_laeuft_vom_Auto_weg_nach_vorn()
    {
        List<RibbonPoint> ribbon = BuildAt(0f);

        Assert.NotEmpty(ribbon);

        // Die Entfernung muss stetig wachsen – ein Sprung hieße, dass die Linie
        // an der falschen Stelle beginnt oder hinter dem Auto weitergeht.
        for (int i = 1; i < ribbon.Count; i++)
        {
            Assert.True(
                ribbon[i].Distance > ribbon[i - 1].Distance,
                $"Punkt {i} liegt mit {ribbon[i].Distance:0.0} m näher als sein Vorgänger.");
        }

        Assert.True(ribbon[0].Distance < 20f, $"Das Band beginnt erst {ribbon[0].Distance:0} m vor dem Auto.");
    }

    [Fact]
    public void Das_Band_verjuengt_sich_mit_der_Entfernung()
    {
        List<RibbonPoint> ribbon = BuildAt(0f);

        float near = MathF.Abs(ribbon[0].Right.X - ribbon[0].Left.X);
        float far = MathF.Abs(ribbon[^1].Right.X - ribbon[^1].Left.X);

        Assert.True(near > far * 2f, $"Nah {near:0.0} px, fern {far:0.0} px – die Perspektive fehlt.");
    }

    [Fact]
    public void Der_rechte_Rand_liegt_rechts_vom_linken()
    {
        foreach (RibbonPoint point in BuildAt(0f))
        {
            Assert.True(point.Right.X > point.Left.X, "Das Band ist in sich verdreht.");
        }
    }

    [Fact]
    public void In_der_Ferne_wird_die_Linie_blasser()
    {
        List<RibbonPoint> ribbon = BuildAt(0f);

        Assert.Equal(1f, ribbon[0].Opacity);
        Assert.True(ribbon[^1].Opacity < 0.5f, $"Am Ende sind es noch {ribbon[^1].Opacity:0.00} Deckkraft.");
        Assert.All(ribbon, point => Assert.InRange(point.Opacity, 0f, 1f));
    }

    /// <summary>
    /// In einer Haarnadel zieht die Linie aus dem Bild. Abbrechen ist richtig –
    /// wieder einsetzen wäre eine zweite, zusammenhanglose Linie.
    /// </summary>
    [Fact]
    public void In_der_Haarnadel_endet_das_Band_vorzeitig()
    {
        SimCorner hairpin = SimTrack.Default.Corners[3];
        List<RibbonPoint> ribbon = BuildAt(hairpin.EntryDistance);

        Assert.NotEmpty(ribbon);
        Assert.True(
            ribbon[^1].Distance < LineRibbon.DefaultLookAhead,
            "Das Band reicht durch die Haarnadel hindurch bis ans Sichtende.");
    }

    /// <summary>
    /// Der ganze Weg am Stück: gefahrene Runden, gelernte Karte, gerechnete
    /// Linie, aufgestellte Kamera, gezeichnetes Band.
    /// </summary>
    private static List<RibbonPoint> BuildAt(float lapDistance)
    {
        IdealLine line = SolveLine();
        SimTrack track = SimTrack.Default;

        var frame = new TelemetryFrame
        {
            GameState = GameState.InGamePlaying,
            LapDistance = lapDistance,
            WorldPosition = track.PointAt(lapDistance, track.RacingOffsetAt(lapDistance)),
            Pose = new CarPose(track.HeadingAt(lapDistance), 0f, 0f),
        };

        TrackCamera? camera = TrackCamera.Create(
            in frame, CameraCalibration.Default, PoseConvention.Assumed, Width, Height);

        Assert.NotNull(camera);
        return LineRibbon.Build(line, camera!, lapDistance);
    }

    private static IdealLine SolveLine()
    {
        List<RecordedLap> driven = VirtualDriver.DriveLaps(4);
        RecordedLap first = driven[0];

        TrackMap map = TrackMap.Empty(
            VirtualDriver.Session.TrackKey,
            VirtualDriver.Session.TrackDisplayName,
            first.TrackLength,
            first.BinSize,
            first.Channels.Count);

        foreach (RecordedLap lap in driven)
        {
            map.Learn(lap);
        }

        IdealLine? line = IdealLineSolver.Solve(map, GripEstimate.Default, topSpeed: 78f);
        Assert.NotNull(line);
        return line!;
    }
}

public class CameraCalibrationStoreTests
{
    [Fact]
    public void Merkt_sich_die_Werte_eines_Fahrzeugs()
    {
        using var directory = new TempDirectory();
        var store = new CameraCalibrationStore(directory.Path);

        store.Save(CameraCalibration.Default with { CarName = "Formel V10", FieldOfViewDegrees = 48f });

        Assert.Equal(48f, store.Load("Formel V10").FieldOfViewDegrees);
    }

    /// <summary>
    /// Der Blickwinkel hängt am Bildschirm, nicht am Auto. Wer ihn einmal
    /// eingestellt hat, soll ihn im nächsten Fahrzeug nicht neu suchen müssen.
    /// </summary>
    [Fact]
    public void Ein_unbekanntes_Fahrzeug_erbt_die_letzte_Einstellung()
    {
        using var directory = new TempDirectory();
        var store = new CameraCalibrationStore(directory.Path);

        store.Save(CameraCalibration.Default with { CarName = "Tourenwagen", FieldOfViewDegrees = 62f });

        CameraCalibration inherited = store.Load("Ein ganz anderes Auto");

        Assert.Equal(62f, inherited.FieldOfViewDegrees);
        Assert.Equal("Ein ganz anderes Auto", inherited.CarName);
    }

    [Fact]
    public void Ohne_Datei_kommt_die_Voreinstellung()
    {
        using var directory = new TempDirectory();
        var store = new CameraCalibrationStore(directory.Path);

        Assert.Equal(CameraCalibration.DefaultFieldOfView, store.Load("Testwagen").FieldOfViewDegrees);
    }

    [Fact]
    public void Eine_kaputte_Datei_verhindert_den_Start_nicht()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Testwagen.json"), "{ kein JSON");

        var store = new CameraCalibrationStore(directory.Path);

        Assert.Equal(CameraCalibration.DefaultFieldOfView, store.Load("Testwagen").FieldOfViewDegrees);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DrivingCoachTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Aufräumen ist Kür.
            }
        }
    }
}
