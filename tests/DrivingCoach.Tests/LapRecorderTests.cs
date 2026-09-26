using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

public class LapRecorderTests
{
    [Fact]
    public void Zeichnet_vollstaendige_Runde_als_gueltig_auf()
    {
        LapCompleted completed = VirtualDriver.DriveLap(VirtualDriver.NeutralStyles());

        Assert.True(completed.IsValid, completed.InvalidReason);
        Assert.Null(completed.InvalidReason);

        RecordedLap lap = completed.Lap;
        Assert.Equal(LapRecorder.DefaultBinSize, lap.BinSize);
        Assert.Equal((int)MathF.Ceiling(SimTrack.Default.Length / lap.BinSize) + 1, lap.Channels.Count);

        // Die Teststrecke ist 4200 m lang; irgendwo zwischen einer und drei
        // Minuten muss die Rundenzeit liegen, sonst stimmt die Abtastung nicht.
        Assert.InRange(lap.LapTime, 60f, 180f);
    }

    [Fact]
    public void Zeitkanal_steigt_ueber_die_ganze_Runde_monoton()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        float[] time = lap.Channels.Time;

        for (int bin = 1; bin < time.Length; bin++)
        {
            Assert.True(
                time[bin] >= time[bin - 1],
                $"Abschnitt {bin}: {time[bin]:0.000} s liegt vor {time[bin - 1]:0.000} s.");
        }

        Assert.Equal(0f, time[0], 2);
        Assert.True(time[^1] > 60f);
    }

    [Fact]
    public void Interpoliert_Zeiten_auf_die_Abschnittskanten()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();

        // TimeAt muss zwischen zwei Stützstellen sauber interpolieren – davon
        // hängt ab, ob das Live-Delta ruhig steht oder zappelt.
        float binSize = lap.BinSize;
        float before = lap.Channels.Time[200];
        float after = lap.Channels.Time[201];
        float middle = lap.TimeAt(200.5f * binSize);

        Assert.InRange(middle, before, after);
        Assert.Equal((before + after) / 2f, middle, 3);
    }

    [Fact]
    public void Verwirft_Runde_die_nicht_an_Start_Ziel_beginnt()
    {
        var recorder = new LapRecorder();
        recorder.ResetSession(VirtualDriver.Session);

        LapCompleted? result = null;
        recorder.LapFinished += completed => result ??= completed;

        // Der Coach wird typischerweise mitten auf der Strecke gestartet.
        // Diese angefangene Runde darf niemals Referenz werden.
        List<TelemetryFrame> frames = VirtualDriver.Frames(VirtualDriver.NeutralStyles());
        foreach (TelemetryFrame frame in frames.Where(f => f.LapDistance > 1500f || f.LapsCompleted > 0))
        {
            recorder.Accept(in frame);
        }

        Assert.True(result.HasValue);
        Assert.False(result!.Value.IsValid);
        Assert.Equal("Aufzeichnung begann nicht an Start/Ziel", result.Value.InvalidReason);
    }

    [Fact]
    public void Verwirft_vom_Spiel_ungueltig_gewertete_Runde()
    {
        var recorder = new LapRecorder();
        recorder.ResetSession(VirtualDriver.Session);

        LapCompleted? result = null;
        recorder.LapFinished += completed => result ??= completed;

        foreach (TelemetryFrame frame in VirtualDriver.Frames(VirtualDriver.NeutralStyles()))
        {
            // Mitten in der Runde von der Strecke abgekommen.
            TelemetryFrame tainted = frame.LapDistance is > 2000f and < 2050f
                ? frame with { LapInvalidated = true }
                : frame;

            recorder.Accept(in tainted);
        }

        Assert.True(result.HasValue);
        Assert.False(result!.Value.IsValid);
        Assert.Equal("Runde vom Spiel ungültig gewertet", result.Value.InvalidReason);
    }

    [Fact]
    public void Verwirft_Runde_in_der_Boxengasse()
    {
        var recorder = new LapRecorder();
        recorder.ResetSession(VirtualDriver.Session);

        LapCompleted? result = null;
        recorder.LapFinished += completed => result ??= completed;

        foreach (TelemetryFrame frame in VirtualDriver.Frames(VirtualDriver.NeutralStyles()))
        {
            TelemetryFrame tainted = frame.LapDistance < 200f
                ? frame with { PitMode = Telemetry.Ams2.PitMode.DrivingIntoPits }
                : frame;

            recorder.Accept(in tainted);
        }

        Assert.True(result.HasValue);
        Assert.False(result!.Value.IsValid);
        Assert.Equal("Boxengasse", result.Value.InvalidReason);
    }

    /// <summary>
    /// Die Ideallinie steht und fällt damit, dass in jedem Abschnitt eine
    /// brauchbare Position liegt – auch in den letzten, die erst beim
    /// Überfahren der Ziellinie nachgetragen werden.
    /// </summary>
    [Fact]
    public void Zeichnet_zu_jedem_Abschnitt_eine_Position_auf()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        LapChannels channels = lap.Channels;

        Assert.True(channels.HasGeometry);

        // Von Abschnitt zu Abschnitt darf der Sprung nicht größer sein als die
        // Abschnittsbreite plus etwas Reserve für die Kurvensehne. Ein Loch in
        // der Aufzeichnung fällt so sofort auf.
        for (int bin = 1; bin < channels.Count; bin++)
        {
            float step = channels.PositionOf(bin).FlatDistanceTo(channels.PositionOf(bin - 1));
            Assert.InRange(step, 0.5f, lap.BinSize * 1.5f);
        }

        // Und die Runde muss sich schließen: der letzte Punkt liegt wieder an
        // der Ziellinie.
        Assert.True(channels.PositionOf(channels.Count - 1).FlatDistanceTo(channels.PositionOf(0)) < lap.BinSize * 2f);
    }

    [Fact]
    public void Legt_sich_in_Kurven_neben_die_Mittellinie()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        SimCorner corner = SimTrack.Default.Corners[0];

        int apexBin = (int)((corner.EntryDistance + (corner.Length / 2f)) / lap.BinSize);
        WorldPoint driven = lap.Channels.PositionOf(apexBin);
        WorldPoint centre = SimTrack.Default.PointAt(apexBin * lap.BinSize);

        // Am Scheitel liegt der Fahrer innen an, nicht auf der Mittellinie –
        // sonst hätte der gelernte Korridor später keine Breite.
        Assert.InRange(driven.FlatDistanceTo(centre), 1f, SimTrack.HalfWidth);
    }

    [Fact]
    public void Langsamere_Runde_braucht_laenger()
    {
        RecordedLap fast = VirtualDriver.DriveReferenceLap();

        var slow = VirtualDriver.NeutralStyles();
        Array.Fill(slow, new CornerStyle(SpeedScale: 0.85f));
        LapCompleted slowLap = VirtualDriver.DriveLap(slow);

        Assert.True(slowLap.IsValid, slowLap.InvalidReason);
        Assert.True(
            slowLap.Lap.LapTime > fast.LapTime,
            $"Langsame Runde {slowLap.Lap.LapTime:0.000} s war nicht langsamer als {fast.LapTime:0.000} s.");
    }
}
