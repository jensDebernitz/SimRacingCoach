using System.Diagnostics;
using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

public class IdealLineTests
{
    [Fact]
    public void Ohne_genug_Runden_gibt_es_keine_Linie()
    {
        TrackMap map = Learn(1);

        Assert.Null(IdealLineSolver.Solve(map, GripEstimate.Default, topSpeed: 78f));
    }

    [Fact]
    public void Die_Linie_bleibt_im_Korridor()
    {
        TrackMap map = Learn(4);
        IdealLine line = Solve(map);

        for (int bin = 0; bin < line.Count; bin++)
        {
            Corridor corridor = map.CorridorAt(bin);

            // Der Sicherheitsabstand darf nur dort unterschritten werden, wo
            // der Korridor selbst schmaler ist als das Auto.
            float slack = MathF.Max(0f, (corridor.Width / 2f) - IdealLineSolver.SafetyMargin);
            float allowed = MathF.Min(IdealLineSolver.SafetyMargin, slack + 0.01f);

            Assert.InRange(line.Offset[bin], corridor.Left + allowed - 0.01f, corridor.Right - allowed + 0.01f);
        }
    }

    /// <summary>
    /// Der Sinn der ganzen Rechnerei: Die Linie muss glatter sein als das, was
    /// gefahren wurde. Sonst ist sie als Vorlage wertlos.
    /// </summary>
    [Fact]
    public void Die_Linie_ist_glatter_als_die_Mittellinie()
    {
        TrackMap map = Learn(4);
        IdealLine line = Solve(map);

        float lineBend = SquaredCurvature(line.Curvature);
        float centreBend = SquaredCurvature(CentreCurvature(map));

        Assert.True(
            lineBend < centreBend,
            $"Die Linie krümmt sich mit {lineBend:0.0000} stärker als die Mittellinie mit {centreBend:0.0000}.");
    }

    [Fact]
    public void Die_Linie_geht_in_Kurven_nach_innen()
    {
        TrackMap map = Learn(4);
        IdealLine line = Solve(map);

        // Erste Kurve der Teststrecke ist eine enge Rechtskurve. Am Scheitel
        // muss die Linie rechts der Mittellinie liegen, an der Einfahrt links.
        SimCorner corner = SimTrack.Default.Corners[0];
        int apex = (int)((corner.EntryDistance + (corner.Length / 2f)) / line.BinSize);
        int entry = (int)((corner.EntryDistance - 40f) / line.BinSize);

        Assert.True(line.Offset[apex] > line.Offset[entry],
            $"Scheitel {line.Offset[apex]:0.00} m liegt nicht weiter innen als die Einfahrt {line.Offset[entry]:0.00} m.");
        Assert.True(line.Curvature[apex] > 0f, "Die Rechtskurve kommt mit falschem Vorzeichen heraus.");
    }

    [Fact]
    public void Das_Tempoprofil_bremst_vor_der_Kurve_und_beschleunigt_danach()
    {
        TrackMap map = Learn(4);
        IdealLine line = Solve(map);

        SimCorner hairpin = SimTrack.Default.Corners[3];   // die Haarnadel, 18 m/s
        int apex = (int)((hairpin.EntryDistance + (hairpin.Length / 2f)) / line.BinSize);
        int before = (int)((hairpin.EntryDistance - 150f) / line.BinSize);
        int after = (int)((hairpin.EntryDistance + hairpin.Length + 150f) / line.BinSize);

        Assert.True(line.TargetSpeed[before] > line.TargetSpeed[apex] + 5f, "Vor der Haarnadel wird nicht gebremst.");
        Assert.True(line.TargetSpeed[after] > line.TargetSpeed[apex] + 5f, "Nach der Haarnadel wird nicht beschleunigt.");

        // Plausible Rundenzeit: die Teststrecke ist 4200 m lang.
        Assert.InRange(line.EstimatedLapTime, 55f, 180f);
    }

    /// <summary>
    /// Die Linie darf nicht schneller sein, als das Auto je war – der Coach
    /// kennt die Motorleistung nicht und darf sie sich nicht ausdenken.
    /// </summary>
    [Fact]
    public void Das_Tempo_bleibt_unter_dem_bisher_Gefahrenen()
    {
        TrackMap map = Learn(4);
        const float topSpeed = 78f;
        IdealLine line = Solve(map, topSpeed);

        Assert.All(line.TargetSpeed, speed => Assert.InRange(speed, 5f, topSpeed + 0.01f));
    }

    [Fact]
    public void Die_Berechnung_bleibt_im_Rahmen_einer_Rundenpause()
    {
        TrackMap map = Learn(4);

        var clock = Stopwatch.StartNew();
        Solve(map);
        clock.Stop();

        // Gerechnet wird zwischen zwei Runden, nebenher. Eine Sekunde ist
        // dafür großzügig; alles darüber wäre ein Zeichen, dass der Löser
        // nicht konvergiert.
        Assert.True(clock.ElapsedMilliseconds < 1000, $"Der Löser brauchte {clock.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Der_Grip_wird_aus_der_Runde_geschaetzt()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        GripEstimate grip = GripEstimate.FromLap(lap);

        // Der Simulator rechnet mit 12 m/s² quer und 6,5 / 16 m/s² längs.
        Assert.InRange(grip.Lateral, 8f, 16f);
        Assert.InRange(grip.Forward, 3f, 9f);
        Assert.InRange(grip.Braking, 8f, 22f);
    }

    private static TrackMap Learn(int laps)
    {
        List<RecordedLap> driven = VirtualDriver.DriveLaps(laps);
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

        return map;
    }

    private static IdealLine Solve(TrackMap map, float topSpeed = 78f)
    {
        IdealLine? line = IdealLineSolver.Solve(map, GripEstimate.Default, topSpeed);
        Assert.NotNull(line);
        return line!;
    }

    /// <summary>Krümmung der Mittellinie – als Vergleichsmaßstab.</summary>
    private static float[] CentreCurvature(TrackMap map)
    {
        var curvature = new float[map.Count];
        for (int bin = 0; bin < map.Count; bin++)
        {
            var previous = map.CentreAt((bin - 1 + map.Count) % map.Count);
            var current = map.CentreAt(bin);
            var next = map.CentreAt((bin + 1) % map.Count);

            float x = previous.X - (2f * current.X) + next.X;
            float z = previous.Z - (2f * current.Z) + next.Z;
            curvature[bin] = MathF.Sqrt((x * x) + (z * z)) / (map.BinSize * map.BinSize);
        }

        return curvature;
    }

    private static float SquaredCurvature(float[] curvature)
    {
        float sum = 0f;
        foreach (float value in curvature)
        {
            sum += value * value;
        }

        return sum;
    }
}
