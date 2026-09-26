using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;
using DrivingCoach.Telemetry.Simulation;

namespace DrivingCoach.Tests;

public class TrackMapTests
{
    [Fact]
    public void Lernt_die_Mittellinie_aus_mehreren_Runden()
    {
        TrackMap map = LearnLaps(4);

        Assert.Equal(4, map.LapCount);
        Assert.True(map.IsUsable);

        // Die gelernte Linie muss auf der Strecke liegen. Geprüft gegen die
        // Mittellinie der Teststrecke – der Simulator legt sich in Kurven
        // innen an, weiter als eine halbe Fahrbahnbreite darf es nicht sein.
        for (int bin = 0; bin < map.Count; bin++)
        {
            WorldPoint truth = SimTrack.Default.PointAt(bin * map.BinSize);
            Assert.True(
                map.CentreAt(bin).FlatDistanceTo(truth) <= SimTrack.HalfWidth,
                $"Abschnitt {bin} liegt {map.CentreAt(bin).FlatDistanceTo(truth):0.0} m neben der Strecke.");
        }
    }

    [Fact]
    public void Der_Korridor_wird_breiter_als_das_Auto()
    {
        TrackMap map = LearnLaps(4);

        for (int bin = 0; bin < map.Count; bin++)
        {
            Corridor corridor = map.CorridorAt(bin);

            Assert.True(corridor.Left <= -TrackMap.CarHalfWidth);
            Assert.True(corridor.Right >= TrackMap.CarHalfWidth);
            Assert.True(corridor.Width >= 2f * TrackMap.CarHalfWidth, $"Abschnitt {bin} ist nur {corridor.Width:0.0} m breit.");
        }
    }

    /// <summary>
    /// Die einzige harte Information über die Streckenbreite, die AMS2
    /// hergibt, sind Räder im Gras. Sie muss den Korridor auf dieser Seite
    /// zusammenziehen, sonst führt die Ideallinie später neben die Strecke.
    /// </summary>
    [Fact]
    public void Raeder_im_Gras_ziehen_den_Rand_herein()
    {
        TrackMap map = LearnLaps(1);
        float before = map.EdgeRight[100];

        // Dieselbe Runde, drei Meter weiter rechts – und dort war ein Rad im Gras.
        map.Learn(ShiftedLap(map, LearnedLap(), offset: 3f, wheelsOff: true));

        Assert.True(map.EdgeRight[100] < before, "Der rechte Rand hat sich nicht bewegt.");

        // Nach dem Verschieben der Mittellinie liegt der Beweispunkt 1,5 m
        // rechts von ihr – weiter darf der Korridor dort nicht reichen.
        Assert.InRange(map.EdgeRight[100], TrackMap.CarHalfWidth, 1.6f);
        Assert.True(map.EdgeLeft[100] < 0f, "Der Korridor enthält die Mittellinie nicht mehr.");
    }

    [Fact]
    public void Ohne_Geometrie_wird_nichts_gelernt()
    {
        RecordedLap lap = LearnedLap();
        var blind = new RecordedLap
        {
            ReferenceKey = lap.ReferenceKey,
            TrackName = lap.TrackName,
            CarName = lap.CarName,
            TrackLength = lap.TrackLength,
            BinSize = lap.BinSize,
            LapTime = lap.LapTime,
            RecordedAt = lap.RecordedAt,

            // Eine Runde aus der Zeit vor der Ideallinie: alle Kanäle da, nur
            // die Positionen fehlen.
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

        TrackMap map = TrackMap.Empty("test", "Test", lap.TrackLength, lap.BinSize, lap.Channels.Count);

        Assert.False(map.Learn(blind));
        Assert.Equal(0, map.LapCount);
        Assert.False(map.IsUsable);
    }

    [Fact]
    public void Eine_Runde_reicht_noch_nicht()
    {
        Assert.False(LearnLaps(1).IsUsable);
        Assert.True(LearnLaps(TrackMap.MinimumLaps).IsUsable);
    }

    [Fact]
    public void Die_Karte_uebersteht_Speichern_und_Laden()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DrivingCoachTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TrackMapStore(directory);
            TrackMap map = LearnLaps(3);
            store.Save(map);

            TrackMap? loaded = store.Load(map.TrackKey);

            Assert.NotNull(loaded);
            Assert.Equal(map.LapCount, loaded!.LapCount);
            Assert.Equal(map.Count, loaded.Count);
            Assert.Equal(map.CentreAt(500), loaded.CentreAt(500));
            Assert.Equal(map.CorridorAt(500), loaded.CorridorAt(500));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Eine_fehlende_Karte_ist_kein_Fehler()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DrivingCoachTests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(new TrackMapStore(directory).Load("gibtesnicht"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RecordedLap LearnedLap() => VirtualDriver.DriveReferenceLap();

    private static TrackMap LearnLaps(int count)
    {
        List<RecordedLap> laps = VirtualDriver.DriveLaps(count);
        RecordedLap first = laps[0];

        TrackMap map = TrackMap.Empty(
            VirtualDriver.Session.TrackKey,
            VirtualDriver.Session.TrackDisplayName,
            first.TrackLength,
            first.BinSize,
            first.Channels.Count);

        foreach (RecordedLap lap in laps)
        {
            Assert.True(map.Learn(lap));
        }

        return map;
    }

    /// <summary>
    /// Dieselbe Runde, um <paramref name="offset"/> Meter quer versetzt. Der
    /// Versatz kommt aus der Karte selbst, weil nur sie weiß, wo quer ist.
    /// </summary>
    private static RecordedLap ShiftedLap(TrackMap map, RecordedLap lap, float offset, bool wheelsOff)
    {
        int bins = lap.Channels.Count;
        var x = new float[bins];
        var y = new float[bins];
        var z = new float[bins];
        var off = new int[bins];

        for (int bin = 0; bin < bins; bin++)
        {
            WorldPoint point = map.PointAt(bin, map.OffsetOf(bin, lap.Channels.PositionOf(bin)) + offset);
            x[bin] = point.X;
            y[bin] = point.Y;
            z[bin] = point.Z;
            off[bin] = wheelsOff ? 2 : 0;
        }

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
                PositionX = x,
                PositionY = y,
                PositionZ = z,
                OffTrackWheels = off,
            },
        };
    }
}
