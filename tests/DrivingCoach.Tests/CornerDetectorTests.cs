using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

public class CornerDetectorTests
{
    [Fact]
    public void Findet_alle_Kurven_der_Teststrecke_in_Fahrtrichtung()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(lap);

        Assert.Equal(SimTrack.Default.Corners.Count, corners.Count);

        for (int i = 0; i < corners.Count; i++)
        {
            Assert.Equal(i + 1, corners[i].Number);
        }

        // In Streckenreihenfolge, ohne Überlappung.
        for (int i = 1; i < corners.Count; i++)
        {
            Assert.True(
                corners[i].StartBin > corners[i - 1].EndBin,
                $"Kurve {i + 1} beginnt vor dem Ende von Kurve {i}.");
        }
    }

    [Fact]
    public void Erkennt_die_Drehrichtung_jeder_Kurve()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(lap);

        for (int i = 0; i < corners.Count; i++)
        {
            CornerDirection expected = SimTrack.Default.Corners[i].Direction > 0
                ? CornerDirection.Right
                : CornerDirection.Left;

            Assert.Equal(expected, corners[i].Direction);
        }
    }

    [Fact]
    public void Ordnet_jede_Kurve_der_richtigen_Stelle_der_Strecke_zu()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(lap);

        for (int i = 0; i < corners.Count; i++)
        {
            SimCorner expected = SimTrack.Default.Corners[i];
            float apexDistance = corners[i].ApexBin * lap.BinSize;

            Assert.InRange(apexDistance, expected.EntryDistance, expected.EntryDistance + expected.Length);
        }
    }

    [Fact]
    public void Setzt_den_Bremspunkt_vor_den_Kurveneingang()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(lap);

        // Jede Kurve der Teststrecke ist langsamer als die Gerade davor, also
        // muss vor jeder eine Bremszone liegen.
        foreach (Corner corner in corners)
        {
            Assert.True(corner.HasBrakingZone, $"{corner.Name} hat keine erkannte Bremszone.");
            Assert.True(
                corner.EntrySpeed > corner.ApexSpeed,
                $"{corner.Name}: Eingangstempo {corner.EntrySpeed:0.0} m/s ist nicht höher als am Scheitel.");
        }
    }

    [Fact]
    public void Findet_den_Scheitelpunkt_als_langsamsten_Punkt_der_Kurve()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        IReadOnlyList<Corner> corners = new CornerDetector().Detect(lap);

        foreach (Corner corner in corners)
        {
            for (int bin = corner.StartBin; bin <= corner.EndBin; bin++)
            {
                Assert.True(lap.Channels.Speed[bin] >= corner.ApexSpeed);
            }
        }
    }
}
