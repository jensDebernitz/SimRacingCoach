using System.Text.Json;
using DrivingCoach.Coaching.Model;

namespace DrivingCoach.Coaching;

/// <summary>
/// Merkt sich die Kamerawerte je Fahrzeug.
/// </summary>
/// <remarks>
/// Kalibrieren ist lästig, und die Sitzposition unterscheidet sich von Auto zu
/// Auto deutlich – ein Formelwagen liegt einen halben Meter tiefer als ein
/// Tourenwagen. Deshalb je Fahrzeug eine Datei; das zuletzt eingestellte
/// Fahrzeug dient zugleich als Vorlage für ein noch unbekanntes.
/// </remarks>
public sealed class CameraCalibrationStore
{
    private const string FallbackName = "standard";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public CameraCalibrationStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrivingCoach",
            "kameras");

        Directory.CreateDirectory(_directory);
    }

    /// <summary>Verzeichnis, in dem die Kalibrierungen liegen.</summary>
    public string RootDirectory => _directory;

    /// <summary>
    /// Lädt die Werte eines Fahrzeugs.
    /// </summary>
    /// <remarks>
    /// Ist das Fahrzeug unbekannt, kommt die zuletzt gespeicherte Kalibrierung
    /// zurück statt der Voreinstellung: Wer seinen Blickwinkel einmal
    /// eingestellt hat, fährt ihn in aller Regel in jedem Auto.
    /// </remarks>
    public CameraCalibration Load(string carName)
    {
        CameraCalibration? own = ReadFile(PathFor(carName));
        if (own is not null)
        {
            return own with { CarName = carName };
        }

        CameraCalibration? template = MostRecent();
        return template is null
            ? CameraCalibration.Default with { CarName = carName }
            : template with { CarName = carName };
    }

    /// <summary>Schreibt die Werte, erst vollständig, dann ersetzend.</summary>
    public void Save(CameraCalibration calibration)
    {
        string path = PathFor(calibration.CarName);
        string temporary = path + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(calibration, SerializerOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Die zuletzt gespeicherte Kalibrierung, egal für welches Auto.</summary>
    private CameraCalibration? MostRecent()
    {
        FileInfo? newest = new DirectoryInfo(_directory)
            .GetFiles("*.json")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return newest is null ? null : ReadFile(newest.FullName);
    }

    private static CameraCalibration? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            CameraCalibration? loaded =
                JsonSerializer.Deserialize<CameraCalibration>(File.ReadAllText(path), SerializerOptions);

            // Unplausible Werte zeichnen eine Linie irgendwohin. Dann lieber
            // von vorn kalibrieren als eine Linie, der niemand folgen kann.
            return loaded is { IsPlausible: true } ? loaded : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string PathFor(string carName) => Path.Combine(_directory, Sanitize(carName) + ".json");

    /// <summary>Macht aus einem Fahrzeugnamen einen Dateinamen.</summary>
    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FallbackName;
        }

        Span<char> buffer = stackalloc char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            buffer[i] = char.IsLetterOrDigit(name[i]) ? name[i] : '_';
        }

        return new string(buffer);
    }
}
