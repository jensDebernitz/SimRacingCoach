using DrivingCoach.Ai;
using DrivingCoach.Coaching;

namespace DrivingCoach.Tests;

/// <summary>
/// Prüft das Auswerten der Gemini-Antworten – ohne Netz.
/// </summary>
/// <remarks>
/// Die Anfrage selbst ist wenig interessant, das Auswerten dagegen sehr: Dort
/// sitzen die Fälle, die im Betrieb tatsächlich auftreten – abgeschnittene
/// Antworten, fehlende Felder, Zahlen als Text.
/// </remarks>
public class AiResponseTests
{
    [Fact]
    public void Eine_normale_Antwort_wird_gelesen()
    {
        GeminiResult result = GeminiClient.ReadAnswer("""
            {
              "candidates": [
                {
                  "finishReason": "STOP",
                  "content": { "parts": [ { "text": "Du bremst " }, { "text": "zu frueh." } ] }
                }
              ]
            }
            """);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Du bremst zu frueh.", result.Text);
    }

    /// <summary>
    /// Ein mitten im Satz abgeschnittener Fahrtipp ist schlimmer als gar keiner
    /// – er könnte das Gegenteil des Gemeinten bedeuten.
    /// </summary>
    [Fact]
    public void Eine_abgeschnittene_Antwort_gilt_als_Fehlschlag()
    {
        GeminiResult result = GeminiClient.ReadAnswer("""
            {
              "candidates": [
                {
                  "finishReason": "MAX_TOKENS",
                  "content": { "parts": [ { "text": "Du bremst zu" } ] }
                }
              ]
            }
            """);

        Assert.False(result.Ok);
        Assert.Contains("zu lang", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Eine_abgelehnte_Anfrage_nennt_den_Grund()
    {
        GeminiResult result = GeminiClient.ReadAnswer("""
            { "promptFeedback": { "blockReason": "SAFETY" } }
            """);

        Assert.False(result.Ok);
        Assert.Contains("SAFETY", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Unlesbares_wird_nicht_zur_Ausnahme()
    {
        GeminiResult result = GeminiClient.ReadAnswer("das ist kein JSON");

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Ohne_Kandidat_kommt_ein_Klartextgrund()
    {
        GeminiResult result = GeminiClient.ReadAnswer("""{ "candidates": [] }""");

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Kurvenansagen_aus_der_Besprechung_landen_im_Vorrat()
    {
        using var coach = new AiCoach(new AiSettings());

        coach.ApplyDebrief("""
            {
              "debrief": "Kurve 4 kostet dich am meisten.",
              "spoken": "Kurve vier: spaeter einlenken.",
              "cornerLines": [
                { "corner": 4, "speech": "Kurve vier: spaeter einlenken." },
                { "corner": 7, "speech": "Kurve sieben: frueher ans Gas." }
              ]
            }
            """);

        Assert.True(coach.Phrases.TryGet(PhraseKeys.Corner(4), out string four));
        Assert.Equal("Kurve vier: spaeter einlenken.", four);
        Assert.True(coach.Phrases.TryGet(PhraseKeys.Corner(7), out _));
    }

    /// <summary>
    /// Das Schema verlangt eine Zahl, aber Modelle liefern gelegentlich
    /// trotzdem einen String. Das darf die ganze Besprechung nicht kosten.
    /// </summary>
    [Fact]
    public void Eine_Kurvennummer_als_Text_wird_trotzdem_verstanden()
    {
        using var coach = new AiCoach(new AiSettings());

        coach.ApplyDebrief("""
            {
              "debrief": "Kurve 2.",
              "cornerLines": [ { "corner": "2", "speech": "Kurve zwei: weiter aussen anlegen." } ]
            }
            """);

        Assert.True(coach.Phrases.TryGet(PhraseKeys.Corner(2), out _));
    }

    /// <summary>
    /// Wer eine Kurve diese Runde sauber erwischt hat, darf ihren alten Tipp
    /// nicht noch einmal hören.
    /// </summary>
    [Fact]
    public void Eine_neue_Besprechung_raeumt_die_alten_Kurventipps_weg()
    {
        using var coach = new AiCoach(new AiSettings());

        coach.ApplyDebrief("""
            { "debrief": "x", "cornerLines": [ { "corner": 4, "speech": "Kurve vier: spaeter einlenken." } ] }
            """);

        coach.ApplyDebrief("""
            { "debrief": "y", "cornerLines": [ { "corner": 9, "speech": "Kurve neun: frueher ans Gas." } ] }
            """);

        Assert.False(coach.Phrases.TryGet(PhraseKeys.Corner(4), out _));
        Assert.True(coach.Phrases.TryGet(PhraseKeys.Corner(9), out _));
    }

    [Fact]
    public void Eine_kaputte_Besprechung_laesst_den_Vorrat_unberuehrt()
    {
        using var coach = new AiCoach(new AiSettings());

        coach.ApplyDebrief("""
            { "debrief": "x", "cornerLines": [ { "corner": 4, "speech": "Kurve vier: spaeter einlenken." } ] }
            """);

        coach.ApplyDebrief("{ das ist kaputt");

        Assert.True(coach.Phrases.TryGet(PhraseKeys.Corner(4), out _));
    }

    [Fact]
    public void Fahrfehler_bekommen_mehrere_Fassungen()
    {
        using var coach = new AiCoach(new AiSettings());

        coach.ApplyFaultVariants("""
            {
              "faults": [
                {
                  "kind": "Lockup",
                  "variants": [ "Bremsdruck raus.", "Das Rad blockiert.", "Weniger Bremse." ]
                },
                { "kind": "GibtEsNicht", "variants": [ "Egal." ] }
              ]
            }
            """);

        string key = PhraseKeys.Fault(FaultKind.Lockup);
        var heard = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(coach.Phrases.TryGet(key, out string speech));
            heard.Add(speech);
        }

        Assert.Equal(3, heard.Count);

        // Der unbekannte Fehlertyp wurde übergangen, nicht mitgezählt.
        Assert.Equal(1, coach.Phrases.Count);
    }
}
