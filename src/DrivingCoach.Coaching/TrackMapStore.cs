using System.Text.Json;
using System.Text.Json.Serialization;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Hält die gelernte Streckengeometrie fest, damit sie nicht bei jedem Start
/// von vorn erarbeitet werden muss.
/// </summary>
/// <remarks>
/// Eine Karte wird über viele Runden hinweg besser. Ginge sie beim Beenden
/// verloren, wäre die Ideallinie in jeder Sitzung erst nach einigen Runden da –
/// also praktisch nie.
/// </remarks>
public sealed class TrackMapStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _directory;

    public TrackMapStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrivingCoach",
            "strecken");

        Directory.CreateDirectory(_directory);
    }

    /// <summary>Verzeichnis, in dem die Streckenkarten liegen.</summary>
    public string RootDirectory => _directory;

    /// <summary>Lädt die Karte einer Strecke, falls vorhanden und brauchbar.</summary>
    public TrackMap? Load(string trackKey)
    {
        string path = PathFor(trackKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            TrackMap? map = JsonSerializer.Deserialize<TrackMap>(stream, SerializerOptions);
            return IsStructurallyValid(map) ? map : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Eine kaputte Karte ist ärgerlich, aber kein Grund, den Coach
            // nicht zu starten – sie wird einfach neu gelernt.
            return null;
        }
    }

    /// <summary>Schreibt die Karte, erst vollständig, dann ersetzend.</summary>
    public void Save(TrackMap map)
    {
        string path = PathFor(map.TrackKey);
        string temporary = path + ".tmp";

        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, map, SerializerOptions);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Löscht die Karte einer Strecke.</summary>
    public void Delete(string trackKey)
    {
        string path = PathFor(trackKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string trackKey) => Path.Combine(_directory, trackKey + ".json");

    private static bool IsStructurallyValid(TrackMap? map)
    {
        if (map is null || map.BinSize <= 0f || map.LapCount <= 0)
        {
            return false;
        }

        int n = map.CentreX.Length;
        return n > 0
            && map.CentreY.Length == n
            && map.CentreZ.Length == n
            && map.EdgeLeft.Length == n
            && map.EdgeRight.Length == n;
    }
}
