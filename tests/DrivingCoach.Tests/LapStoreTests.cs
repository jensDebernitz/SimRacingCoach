using DrivingCoach.Coaching;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Tests;

public class LapStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "DrivingCoachTests",
        Guid.NewGuid().ToString("N"));

    private readonly LapStore _store;

    public LapStoreTests() => _store = new LapStore(_directory);

    [Fact]
    public void Speichert_und_laedt_eine_Runde_verlustfrei()
    {
        RecordedLap original = VirtualDriver.DriveReferenceLap();
        _store.Save(original);

        RecordedLap? loaded = _store.Load(original.ReferenceKey);

        Assert.NotNull(loaded);
        Assert.Equal(original.ReferenceKey, loaded.ReferenceKey);
        Assert.Equal(original.TrackName, loaded.TrackName);
        Assert.Equal(original.CarName, loaded.CarName);
        Assert.Equal(original.LapTime, loaded.LapTime);
        Assert.Equal(original.BinSize, loaded.BinSize);
        Assert.Equal(original.Channels.Count, loaded.Channels.Count);
        Assert.Equal(original.Channels.Time, loaded.Channels.Time);
        Assert.Equal(original.Channels.Speed, loaded.Channels.Speed);
        Assert.Equal(original.Channels.Gear, loaded.Channels.Gear);
    }

    [Fact]
    public void Liefert_null_fuer_unbekannte_Kombination()
    {
        Assert.Null(_store.Load("Gibt_es_nicht"));
    }

    [Fact]
    public void Uebernimmt_nur_die_schnellere_Runde()
    {
        RecordedLap fast = VirtualDriver.DriveReferenceLap();

        var slowStyles = VirtualDriver.NeutralStyles();
        Array.Fill(slowStyles, new CornerStyle(SpeedScale: 0.88f));
        RecordedLap slow = VirtualDriver.DriveLap(slowStyles).Lap;

        Assert.True(_store.TrySaveIfFaster(slow, current: null, out RecordedLap first));
        Assert.Equal(slow.LapTime, first.LapTime);

        Assert.True(_store.TrySaveIfFaster(fast, first, out RecordedLap second));
        Assert.Equal(fast.LapTime, second.LapTime);

        // Die langsamere Runde darf die Bestzeit nicht wieder überschreiben.
        Assert.False(_store.TrySaveIfFaster(slow, second, out RecordedLap third));
        Assert.Equal(fast.LapTime, third.LapTime);
        Assert.Equal(fast.LapTime, _store.Load(fast.ReferenceKey)!.LapTime);
    }

    [Fact]
    public void Loescht_die_Referenz_auf_Wunsch()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        _store.Save(lap);
        Assert.NotNull(_store.Load(lap.ReferenceKey));

        _store.Delete(lap.ReferenceKey);

        Assert.Null(_store.Load(lap.ReferenceKey));
    }

    [Fact]
    public void Beschaedigte_Datei_verhindert_den_Start_nicht()
    {
        RecordedLap lap = VirtualDriver.DriveReferenceLap();
        _store.Save(lap);

        File.WriteAllText(Path.Combine(_store.RootDirectory, lap.ReferenceKey + ".json"), "{ kaputt");

        Assert.Null(_store.Load(lap.ReferenceKey));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
