using System.Text.Json;
using System.Text.Json.Serialization;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Speichert die schnellste gültige Runde je Strecken-/Fahrzeug-Kombination,
/// damit die Referenz auch nach einem Neustart erhalten bleibt.
/// </summary>
public sealed class LapStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Die Dateien werden bei Bedarf von Hand inspiziert – Einrückung hilft,
        // kostet bei wenigen hundert Kilobyte aber kaum etwas.
        WriteIndented = false,
    };

    private readonly string _directory;

    public LapStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrivingCoach",
            "laps");

        Directory.CreateDirectory(_directory);
    }

    /// <summary>Verzeichnis, in dem die Referenzrunden liegen.</summary>
    public string RootDirectory => _directory;

    /// <summary>Lädt die gespeicherte Referenzrunde, falls vorhanden.</summary>
    public RecordedLap? Load(string referenceKey)
    {
        string path = PathFor(referenceKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            RecordedLap? lap = JsonSerializer.Deserialize<RecordedLap>(stream, SerializerOptions);
            return IsStructurallyValid(lap) ? lap : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Beschädigte Datei darf den Start nicht verhindern.
            return null;
        }
    }

    /// <summary>Schreibt die Runde als neue Referenz.</summary>
    public void Save(RecordedLap lap)
    {
        string path = PathFor(lap.ReferenceKey);
        string temporary = path + ".tmp";

        // Erst vollständig schreiben, dann ersetzen – sonst kann ein Absturz
        // mitten im Schreiben die bisherige Referenz zerstören.
        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, lap, SerializerOptions);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Übernimmt die Runde als Referenz, wenn sie schneller ist als die bisherige.
    /// </summary>
    /// <returns>True, wenn eine neue Bestzeit gespeichert wurde.</returns>
    public bool TrySaveIfFaster(RecordedLap lap, RecordedLap? current, out RecordedLap best)
    {
        if (current is not null && current.LapTime <= lap.LapTime)
        {
            best = current;
            return false;
        }

        Save(lap);
        best = lap;
        return true;
    }

    /// <summary>Löscht die Referenz einer Kombination.</summary>
    public void Delete(string referenceKey)
    {
        string path = PathFor(referenceKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string referenceKey) => Path.Combine(_directory, referenceKey + ".json");

    /// <summary>Schützt vor Dateien, deren Kanäle unterschiedlich lang sind.</summary>
    private static bool IsStructurallyValid(RecordedLap? lap)
    {
        if (lap is null || lap.BinSize <= 0f || lap.LapTime <= 0f)
        {
            return false;
        }

        LapChannels c = lap.Channels;
        int n = c.Time.Length;
        return n > 0
            && c.Speed.Length == n
            && c.Throttle.Length == n
            && c.Brake.Length == n
            && c.Steering.Length == n
            && c.YawRate.Length == n
            && c.Gear.Length == n;
    }
}
