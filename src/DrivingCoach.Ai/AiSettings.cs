using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrivingCoach.Ai;

/// <summary>
/// Einstellungen der KI-Anbindung. Liegt getrennt von
/// <c>overlay.json</c>, weil hier ein Geheimnis drinsteht.
/// </summary>
/// <remarks>
/// <para>
/// Der Schlüssel kommt wahlweise aus dieser Datei oder aus der
/// Umgebungsvariablen <c>GEMINI_API_KEY</c>. Die Umgebungsvariable gewinnt –
/// wer sie setzt, will bewusst keinen Schlüssel auf der Platte liegen haben.
/// </para>
/// <para>
/// Wichtig: Ein Gemini-Abo in der App (Google One) lässt sich nicht von außen
/// ansprechen. Gebraucht wird ein API-Schlüssel aus dem Google AI Studio.
/// </para>
/// </remarks>
public sealed record AiSettings
{
    /// <summary>
    /// Voreinstellung, falls in der Datei nichts steht.
    /// </summary>
    /// <remarks>
    /// Google nimmt alte Modelle vom Netz – <c>gemini-2.5-flash</c> antwortet
    /// neuen Schlüsseln bereits mit 404. Wird dieser Name eines Tages auch
    /// abgeräumt, hilft <c>gemini-flash-latest</c> in <c>ai.json</c>: der zeigt
    /// immer auf das aktuelle Flash-Modell, ohne dass hier etwas nachgezogen
    /// werden muss.
    /// </remarks>
    public const string DefaultModel = "gemini-3.8-flash";

    /// <summary>Basisadresse der Gemini-Schnittstelle.</summary>
    public const string DefaultEndpoint = "https://generativelanguage.googleapis.com";

    /// <summary>Name der Umgebungsvariablen, die den Schlüssel ohne Datei liefert.</summary>
    public const string KeyEnvironmentVariable = "GEMINI_API_KEY";

    /// <summary>API-Schlüssel aus dem Google AI Studio. Leer heißt: KI aus.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Modellname. Änderbar, ohne neu zu bauen – Google benennt Modelle um.</summary>
    public string Model { get; init; } = DefaultModel;

    public string Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>Rundenbesprechung nach jeder Runde.</summary>
    public bool LapDebriefEnabled { get; init; } = true;

    /// <summary>Session-Fazit mit Trainingsplan.</summary>
    public bool SessionSummaryEnabled { get; init; } = true;

    /// <summary>Freie Fragen über das Frage-Fenster.</summary>
    public bool QuestionsEnabled { get; init; } = true;

    /// <summary>
    /// Live-Ansagen von Gemini formulieren lassen. Die Sätze werden am
    /// Rundenende im Voraus geholt und beim Auslösen nur noch abgespielt –
    /// währenddessen wird nichts angefragt.
    /// </summary>
    public bool LivePhrasingEnabled { get; init; } = true;

    /// <summary>Rundenbesprechung zusätzlich vorlesen.</summary>
    public bool SpeakDebrief { get; init; } = true;

    /// <summary>Zeitlimit je Anfrage in Sekunden.</summary>
    public int TimeoutSeconds { get; init; } = 25;

    /// <summary>
    /// Denkbudget. 0 schaltet es ab und macht die Antwort deutlich schneller
    /// und billiger; für "fasse diese Zahlen zusammen" bringt Nachdenken
    /// nichts. <c>null</c> überlässt es dem Modell. Lehnt ein Modell das Feld
    /// ab, wird es automatisch weggelassen – siehe <see cref="GeminiClient"/>.
    /// </summary>
    public int? ThinkingBudget { get; init; } = 0;

    /// <summary>Verzeichnis für die abgelegten Session-Fazits.</summary>
    [JsonIgnore]
    public static string ReportDirectory => Path.Combine(BaseDirectory, "berichte");

    /// <summary>Der tatsächlich zu verwendende Schlüssel.</summary>
    [JsonIgnore]
    public string EffectiveApiKey
    {
        get
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
            return string.IsNullOrWhiteSpace(fromEnvironment) ? ApiKey.Trim() : fromEnvironment.Trim();
        }
    }

    /// <summary>True, wenn überhaupt etwas angefragt werden kann.</summary>
    [JsonIgnore]
    public bool IsConfigured => EffectiveApiKey.Length > 0;

    [JsonIgnore]
    public static string FilePath => Path.Combine(BaseDirectory, "ai.json");

    private static string BaseDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DrivingCoach");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AiSettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return new AiSettings();
            }

            return JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), JsonOptions) ?? new AiSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Eine kaputte Datei darf den Coach nicht am Starten hindern –
            // dann fährt er eben ohne KI.
            return new AiSettings();
        }
    }

    /// <summary>
    /// Legt eine kommentierte Vorlage an, falls noch keine Datei existiert.
    /// Ohne sie müsste der Fahrer die Struktur erraten.
    /// </summary>
    public static void EnsureTemplate()
    {
        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Template);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Vorlage ist Komfort, kein Muss.
        }
    }

    private static string Template =>
        $$"""
        {
          // API-Schluessel aus dem Google AI Studio: https://aistudio.google.com/apikey
          // Alternativ die Umgebungsvariable {{KeyEnvironmentVariable}} setzen, dann
          // bleibt hier nichts stehen. Leer heisst: der Coach faehrt ohne KI.
          "ApiKey": "",

          // Modellname. Laesst sich jederzeit aendern, ohne neu zu bauen.
          // Meldet der Coach "Modell nicht gefunden", hat Google es abgeraeumt:
          // dann "gemini-flash-latest" eintragen, das zeigt immer auf das
          // aktuelle Flash-Modell.
          "Model": "{{DefaultModel}}",

          "LapDebriefEnabled": true,
          "SessionSummaryEnabled": true,
          "QuestionsEnabled": true,

          // Live-Ansagen von Gemini formulieren lassen. Die Saetze werden am
          // Rundenende im Voraus geholt, waehrend der Fahrt wird nichts angefragt.
          "LivePhrasingEnabled": true,

          "SpeakDebrief": true,
          "TimeoutSeconds": 25,

          // 0 schaltet das Nachdenken ab: schneller und billiger. Kennt ein
          // Modell das Feld nicht, laesst der Coach es von selbst weg.
          "ThinkingBudget": 0
        }
        """;
}
