using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DrivingCoach.Ai;

/// <summary>Ergebnis einer Anfrage. Fehler sind ein normaler Rückgabewert, keine Ausnahme.</summary>
/// <param name="Ok">True, wenn Text zurückkam.</param>
/// <param name="Text">Die Antwort des Modells.</param>
/// <param name="Error">Klartext-Grund, falls etwas schiefging.</param>
public readonly record struct GeminiResult(bool Ok, string Text, string? Error)
{
    public static GeminiResult Success(string text) => new(true, text, null);

    public static GeminiResult Failure(string error) => new(false, string.Empty, error);
}

/// <summary>
/// Schlanker Zugang zur Gemini-Schnittstelle über deren REST-Endpunkt.
/// </summary>
/// <remarks>
/// <para>
/// Bewusst von Hand statt über ein SDK: gebraucht wird ein einziger Aufruf
/// (<c>generateContent</c>), und dafür ein Paket zu ziehen würde die
/// Abhängigkeitsfreiheit der Projektmappe aufgeben – die hier kein Selbstzweck
/// ist, sondern daran hängt, dass der Paket-Server nicht erreichbar ist.
/// </para>
/// <para>
/// Der Schlüssel geht als Kopfzeile mit, nicht als Adressparameter. Sonst
/// stünde er in jedem Proxy-Protokoll und in jeder Fehlermeldung.
/// </para>
/// </remarks>
public sealed class GeminiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly AiSettings _settings;

    /// <summary>
    /// Wird auf true gesetzt, sobald der Dienst <c>thinkingConfig</c> ablehnt.
    /// Nicht jedes Modell kennt das Feld, und welches Modell hier läuft, steht
    /// in <c>ai.json</c> – also wird es nach einer Absage für den Rest der
    /// Sitzung weggelassen, statt jede Anfrage daran zu verlieren.
    /// </summary>
    private bool _thinkingUnsupported;

    public GeminiClient(AiSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _ownsClient = http is null;
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120));
    }

    /// <summary>Fragt das Modell und gibt den reinen Antworttext zurück.</summary>
    /// <param name="systemInstruction">Rolle und Regeln – gilt für die ganze Anfrage.</param>
    /// <param name="prompt">Die eigentliche Aufgabe samt Daten.</param>
    /// <param name="responseSchema">Optionales JSON-Schema; erzwingt eine maschinenlesbare Antwort.</param>
    /// <param name="maxOutputTokens">Obergrenze für die Antwortlänge.</param>
    /// <param name="temperature">Streuung. Für Fahrtipps bewusst niedrig.</param>
    /// <param name="cancellationToken">Abbruch, etwa beim Beenden des Coaches.</param>
    public async Task<GeminiResult> GenerateAsync(
        string systemInstruction,
        string prompt,
        JsonNode? responseSchema = null,
        int maxOutputTokens = 800,
        double temperature = 0.4,
        CancellationToken cancellationToken = default)
    {
        string key = _settings.EffectiveApiKey;
        if (key.Length == 0)
        {
            return GeminiResult.Failure($"Kein API-Schlüssel. Trag ihn in {AiSettings.FilePath} ein.");
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                // Kurz warten und erneut versuchen – das Kontingent der freien
                // Stufe ist pro Minute gedeckelt, nicht pro Tag erschöpft.
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }

            JsonNode body = BuildBody(systemInstruction, prompt, responseSchema, maxOutputTokens, temperature);

            (GeminiResult result, bool retry) = await SendAsync(key, body, cancellationToken).ConfigureAwait(false);

            if (!retry)
            {
                return result;
            }
        }

        return GeminiResult.Failure("Gemini antwortet gerade nicht (mehrfach versucht).");
    }

    private async Task<(GeminiResult Result, bool Retry)> SendAsync(
        string key,
        JsonNode body,
        CancellationToken cancellationToken)
    {
        string url = $"{_settings.Endpoint.TrimEnd('/')}/v1beta/models/{_settings.Model}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        string payload;

        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (GeminiResult.Failure("Zeitlimit überschritten."), true);
        }
        catch (HttpRequestException ex)
        {
            return (GeminiResult.Failure($"Keine Verbindung zu Gemini: {ex.Message}"), true);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return (ReadAnswer(payload), false);
            }

            // Das Modell kennt thinkingConfig nicht: einmal ohne das Feld erneut
            // versuchen, statt die Funktion komplett zu verlieren.
            if (response.StatusCode == HttpStatusCode.BadRequest &&
                !_thinkingUnsupported &&
                payload.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            {
                _thinkingUnsupported = true;
                return (GeminiResult.Failure("thinkingConfig nicht unterstützt"), true);
            }

            bool transient =
                response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout;

            return (GeminiResult.Failure(DescribeError(response.StatusCode, payload)), transient);
        }
    }

    private JsonNode BuildBody(
        string systemInstruction,
        string prompt,
        JsonNode? responseSchema,
        int maxOutputTokens,
        double temperature)
    {
        var generationConfig = new JsonObject
        {
            ["temperature"] = temperature,
            ["maxOutputTokens"] = maxOutputTokens,
        };

        if (responseSchema is not null)
        {
            generationConfig["responseMimeType"] = "application/json";
            generationConfig["responseSchema"] = responseSchema.DeepClone();
        }

        if (_settings.ThinkingBudget is { } budget && !_thinkingUnsupported)
        {
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = budget };
        }

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = systemInstruction }),
            },
            ["contents"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }),
                }),
            ["generationConfig"] = generationConfig,
        };
    }

    /// <summary>
    /// Klaubt den Text aus der Antwort. Öffentlich, weil genau hier die Fehler
    /// sitzen, die man ohne Netz testen können muss.
    /// </summary>
    public static GeminiResult ReadAnswer(string payload)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException ex)
        {
            return GeminiResult.Failure($"Antwort unlesbar: {ex.Message}");
        }

        if (root?["promptFeedback"]?["blockReason"]?.GetValue<string>() is { } blocked)
        {
            return GeminiResult.Failure($"Anfrage abgelehnt ({blocked}).");
        }

        if (root?["candidates"] is not JsonArray { Count: > 0 } candidates)
        {
            return GeminiResult.Failure("Gemini hat nichts geliefert.");
        }

        JsonNode? candidate = candidates[0];

        // Abbruchgründe, bei denen der Text fehlt oder mittendrin endet. Ein
        // halber Satz als Fahrtipp wäre schlimmer als gar keiner.
        string? finish = candidate?["finishReason"]?.GetValue<string>();

        var text = new StringBuilder();

        if (candidate?["content"]?["parts"] is JsonArray parts)
        {
            foreach (JsonNode? part in parts)
            {
                if (part?["text"]?.GetValue<string>() is { Length: > 0 } piece)
                {
                    text.Append(piece);
                }
            }
        }

        if (text.Length == 0)
        {
            return GeminiResult.Failure(finish switch
            {
                "MAX_TOKENS" => "Antwort war zu lang und wurde abgeschnitten.",
                "SAFETY" or "PROHIBITED_CONTENT" => "Antwort wurde von Gemini zurückgehalten.",
                null => "Gemini hat nichts geliefert.",
                var reason => $"Gemini hat abgebrochen ({reason}).",
            });
        }

        if (finish == "MAX_TOKENS")
        {
            return GeminiResult.Failure("Antwort war zu lang und wurde abgeschnitten.");
        }

        return GeminiResult.Success(text.ToString().Trim());
    }

    /// <summary>Übersetzt eine Fehlerantwort in einen Satz, mit dem man etwas anfangen kann.</summary>
    private static string DescribeError(HttpStatusCode status, string payload)
    {
        string? detail = null;

        try
        {
            detail = JsonNode.Parse(payload)?["error"]?["message"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            // Dann eben ohne Detail.
        }

        string hint = status switch
        {
            HttpStatusCode.BadRequest => "Anfrage abgelehnt – meist ein falscher Modellname.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"Schlüssel wird nicht akzeptiert. Neu erzeugen unter aistudio.google.com/apikey und in {AiSettings.FilePath} eintragen.",
            // Google räumt alte Modelle ab. Der Dienst nennt im Detailtext
            // meist gleich den Nachfolger, deshalb steht der hier mit dabei.
            HttpStatusCode.NotFound =>
                $"Modell nicht gefunden oder abgekündigt. Trag in {AiSettings.FilePath} \"gemini-flash-latest\" ein, das zeigt immer auf das aktuelle Modell.",
            HttpStatusCode.TooManyRequests => "Kontingent erschöpft – kurz warten oder ein anderes Modell wählen.",
            HttpStatusCode.ServiceUnavailable => "Das Modell ist gerade überlastet. Neue Modelle trifft das oft in den ersten Tagen.",
            _ => $"Gemini meldet {(int)status}.",
        };

        return detail is null ? hint : $"{hint} ({detail})";
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
