using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Ams2;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft die Entscheidung, ob und wo gezeichnet wird – nicht das Zeichnen
/// selbst. Dafür müsste ein Fenster aufgehen, und was dabei herauskommt,
/// prüft schon <see cref="LineRibbonTests"/>.
/// </summary>
public class IdealLinePresenterTests : IDisposable
{
    private const float Width = 1920f;
    private const float Height = 1080f;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "DrivingCoachTest_" + Guid.NewGuid().ToString("N"));

    private readonly IdealLinePresenter _presenter;

    public IdealLinePresenterTests()
    {
        Directory.CreateDirectory(_directory);
        _presenter = new IdealLinePresenter(new CameraCalibrationStore(_directory));
    }

    [Fact]
    public void Auf_der_Strecke_liegt_ein_Band()
    {
        IReadOnlyList<RibbonPoint> ribbon = _presenter.Build(Ready(), Width, Height);

        Assert.NotEmpty(ribbon);
        Assert.Equal(string.Empty, _presenter.Status);
    }

    [Fact]
    public void Ohne_Ideallinie_wird_nichts_gezeichnet()
    {
        CoachState state = Ready();
        state.IdealLine = null;
        state.ValidLapCount = 2;

        Assert.Empty(_presenter.Build(state, Width, Height));
        Assert.Contains("wird gelernt", _presenter.Status);
    }

    [Fact]
    public void Vor_der_ersten_Runde_sagt_der_Hinweis_warum()
    {
        CoachState state = Ready();
        state.IdealLine = null;
        state.ValidLapCount = 0;

        Assert.Empty(_presenter.Build(state, Width, Height));
        Assert.Contains("keine gültige Runde", _presenter.Status);
    }

    /// <summary>
    /// Der eigentliche Grund für die Prüfung: Eine Linie aus einer geratenen
    /// Blickrichtung läge irgendwo auf dem Bild und führte in die Wand.
    /// </summary>
    [Fact]
    public void Ohne_gelernte_Blickrichtung_bleibt_die_Linie_weg()
    {
        CoachState state = Ready();
        state.IsPoseLearned = false;

        Assert.Empty(_presenter.Build(state, Width, Height));
        Assert.Contains("Blickrichtung", _presenter.Status);
    }

    [Fact]
    public void In_der_Box_wird_nicht_gezeichnet()
    {
        CoachState state = Ready();
        state.Frame = state.Frame with { GameState = GameState.InGameInMenuTimeTicking };

        Assert.Empty(_presenter.Build(state, Width, Height));

        // Kein Hinweis: Dass im Menü keine Linie liegt, muss niemandem erklärt werden.
        Assert.Equal(string.Empty, _presenter.Status);
    }

    [Fact]
    public void Ein_Fenster_ohne_Groesse_meldet_keinen_Fehler()
    {
        Assert.Empty(_presenter.Build(Ready(), 0f, 0f));
        Assert.Equal(string.Empty, _presenter.Status);
    }

    [Fact]
    public void Eine_unsinnige_Kalibrierung_zeichnet_lieber_nichts()
    {
        CoachState state = Ready();

        _presenter.Build(state, Width, Height);
        _presenter.Adjust(_presenter.Calibration with { FieldOfViewDegrees = 0f });

        Assert.Empty(_presenter.Build(state, Width, Height));
        Assert.Contains("Kalibrierung", _presenter.Status);
    }

    [Fact]
    public void Das_Fahrzeug_bringt_seine_eigenen_Kamerawerte_mit()
    {
        new CameraCalibrationStore(_directory).Save(
            CameraCalibration.Default with { CarName = VirtualDriver.Session.CarName, FieldOfViewDegrees = 42f });

        _presenter.Build(Ready(), Width, Height);

        Assert.Equal(42f, _presenter.Calibration.FieldOfViewDegrees);
    }

    [Fact]
    public void Geaenderte_Werte_ueberleben_die_Sitzung()
    {
        _presenter.Build(Ready(), Width, Height);
        _presenter.Adjust(_presenter.Calibration with { FieldOfViewDegrees = 70f });

        Assert.Equal(70f, new CameraCalibrationStore(_directory).Load(VirtualDriver.Session.CarName).FieldOfViewDegrees);
    }

    /// <summary>
    /// Ein engerer Blickwinkel zieht dieselbe Linie weiter auseinander. Damit
    /// hängt die Zeichnung nachweislich an der Kalibrierung und nicht an
    /// irgendeinem festen Maßstab.
    /// </summary>
    [Fact]
    public void Der_Blickwinkel_wirkt_sich_auf_das_Band_aus()
    {
        CoachState state = Ready();

        _presenter.Build(state, Width, Height);
        _presenter.Adjust(_presenter.Calibration with { FieldOfViewDegrees = 90f });
        float wide = BandWidth(_presenter.Build(state, Width, Height));

        _presenter.Adjust(_presenter.Calibration with { FieldOfViewDegrees = 40f });
        float narrow = BandWidth(_presenter.Build(state, Width, Height));

        Assert.True(narrow > wide, $"Eng {narrow:0.0} px, weit {wide:0.0} px – der Blickwinkel bleibt wirkungslos.");
    }

    private static float BandWidth(IReadOnlyList<RibbonPoint> ribbon)
    {
        Assert.NotEmpty(ribbon);
        return MathF.Abs(ribbon[0].Right.X - ribbon[0].Left.X);
    }

    /// <summary>
    /// Ein Zustand, wie ihn der Coach nach ein paar Runden hat: Karte gelernt,
    /// Linie gerechnet, Blickrichtung eingeordnet, Auto auf der Strecke.
    /// </summary>
    private static CoachState Ready()
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

        SimTrack track = SimTrack.Default;
        const float lapDistance = 500f;

        return new CoachState
        {
            Session = VirtualDriver.Session,
            ValidLapCount = driven.Count,
            TrackMap = map,
            IdealLine = IdealLineSolver.Solve(map, GripEstimate.Default, topSpeed: 78f),
            Pose = PoseConvention.Learn(first) ?? PoseConvention.Assumed,
            IsPoseLearned = true,
            Frame = new TelemetryFrame
            {
                GameState = GameState.InGamePlaying,
                LapDistance = lapDistance,
                WorldPosition = track.PointAt(lapDistance, track.RacingOffsetAt(lapDistance)),
                Pose = new CarPose(track.HeadingAt(lapDistance), 0f, 0f),
            },
        };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist Kür.
        }
    }
}
