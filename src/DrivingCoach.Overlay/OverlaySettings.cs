using System.IO;
using System.Text.Json;

namespace DrivingCoach.Overlay;

/// <summary>
/// Was sich das Overlay zwischen zwei Starts merkt. Bewusst winzig gehalten –
/// die Position ist das Einzige, was der Fahrer nicht jedes Mal neu einstellen
/// will.
/// </summary>
public sealed record OverlaySettings
{
    public double Left { get; init; } = 40;

    public double Top { get; init; } = 40;

    public bool SpeechEnabled { get; init; } = true;

    /// <summary>Kurvenbericht der letzten Runde einblenden.</summary>
    public bool ShowCornerReport { get; init; } = true;

    /// <summary>Perspektivische Ideallinie auf der Strecke einblenden.</summary>
    public bool ShowIdealLine { get; init; } = true;

    /// <summary>
    /// Vergrößerungsfaktor des Overlays. <c>null</c> heißt "noch nie
    /// eingestellt" – dann sucht sich <see cref="OverlayScale"/> einen Wert, der
    /// zum Bildschirm passt. Ein fester Vorgabewert würde diesen Unterschied
    /// verschlucken und die einmal gewählte 100 % nie wieder hinterfragen.
    /// </summary>
    public double? Scale { get; init; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DrivingCoach",
        "overlay.json");

    public static OverlaySettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return new OverlaySettings();
            }

            return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path)) ?? new OverlaySettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Kaputte Datei darf den Start nicht verhindern – dann eben von vorn.
            return new OverlaySettings();
        }
    }

    public void Save()
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Speichern ist Komfort, kein Muss.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
