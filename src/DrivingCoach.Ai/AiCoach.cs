using System.Text.Json;
using System.Text.Json.Nodes;
using DrivingCoach.Coaching;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Ai;

/// <summary>
/// Verbindet den regelbasierten Coach mit Gemini.
/// </summary>
/// <remarks>
/// <para>
/// Die Arbeitsteilung ist bewusst scharf: Alles, was innerhalb eines Frames
/// entschieden werden muss, bleibt gerechnet. Die KI bekommt nur, was Zeit hat
/// – die Besprechung nach der Runde, das Fazit nach der Session, eine Frage aus
/// der Box.
/// </para>
/// <para>
/// Die einzige Ausnahme sind die Live-Ansagen, und auch die werden nicht live
/// erzeugt: Am Rundenende steht fest, welche Kurven in der naechsten Runde
/// einen Tipp bekommen. Genau diese Saetze werden dann im Voraus geholt und im
/// <see cref="Phrasebook"/> abgelegt. Waehrend der Fahrt findet nur noch ein
/// Nachschlag statt – die Wartezeit faellt vor die Runde, nicht in die Kurve.
/// </para>
/// <para>
/// Alles ist bestmoegliche Zugabe: faellt das Netz aus, fehlt der Schluessel
/// oder antwortet der Dienst nicht, faehrt der Coach unveraendert mit seinen
/// gerechneten Hinweisen weiter.
/// </para>
/// </remarks>
public sealed class AiCoach : IDisposable
{
    /// <summary>So lange wird nach einem Fehler nicht erneut gemeckert.</summary>
    private const double ProblemRepeatSeconds = 120.0;

    /// <summary>Mehr Runden braucht kein Fazit – der Prompt bliebe sonst unbegrenzt.</summary>
    private const int MaxSessionLaps = 40;

    /// <summary>Obergrenze für den gesprochenen Teil der Rundenbesprechung.</summary>
    private const int MaxSpokenDebriefLength = 160;

    /// <summary>Die Fahrfehler, für die ein Ansagen-Vorrat geholt wird.</summary>
    private static readonly (string Kind, string Meaning)[] FaultMeanings =
    [
        (nameof(FaultKind.Lockup), "Ein Rad blockiert beim Bremsen, das Auto schiebt geradeaus."),
        (nameof(FaultKind.Wheelspin), "Die Antriebsraeder drehen durch, meist beim Herausbeschleunigen."),
        (nameof(FaultKind.OffTrack), "Alle vier Raeder sind neben der Strecke, die Runde zaehlt nicht mehr."),
        (nameof(FaultKind.OverRev), "Der Motor haengt im Drehzahlbegrenzer, es wird nicht hochgeschaltet."),
        (nameof(FaultKind.ShortShift), "Es wird deutlich zu frueh hochgeschaltet, der Motor dreht nicht aus."),
        (nameof(FaultKind.Coasting), "Das Auto rollt lange ohne Gas und ohne Bremse."),
    ];

    private readonly AiSettings _settings;
    private readonly GeminiClient _client;
    private readonly bool _ownsClient;
    private readonly List<LapReview> _laps = [];
    private readonly Lock _lapGate = new();

    private readonly CancellationTokenSource _lifetime = new();

    private CoachEngine? _engine;
    private CancellationTokenSource? _debrief;
    private string _sessionKey = string.Empty;
    private double _lastProblemAt = double.NegativeInfinity;
    private bool _faultsRequested;
    private bool _disposed;

    public AiCoach(AiSettings settings, GeminiClient? client = null)
    {
        _settings = settings;
        _ownsClient = client is null;
        _client = client ?? new GeminiClient(settings);
    }

    /// <summary>Der Vorrat, aus dem die Live-Ansagen bedient werden.</summary>
    public Phrasebook Phrases { get; } = new();

    /// <summary>True, wenn ein Schlüssel hinterlegt ist.</summary>
    public bool IsConfigured => _settings.IsConfigured;

    /// <summary>Kurzer Satz für die Statuszeile des Overlays.</summary>
    public string StatusLine => !IsConfigured
        ? $"KI aus – trag einen Gemini-Schlüssel in {AiSettings.FilePath} ein"
        : $"KI aktiv · {_settings.Model}";

    /// <summary>Hängt sich an den Coach und beginnt, Runden zu besprechen.</summary>
    public void Attach(CoachEngine engine)
    {
        _engine = engine;
        engine.LapReviewed += OnLapReviewed;

        if (IsConfigured && _settings.LivePhrasingEnabled)
        {
            // Fahrfehler haengen nicht an der Strecke, also einmal beim Start
            // holen – dann steht der Vorrat schon fuer die allererste Runde.
            RequestFaultVariants();
        }
    }

    /// <summary>Anzahl der bisher gesammelten Runden. Für Anzeige und Tests.</summary>
    public int CollectedLaps
    {
        get
        {
            lock (_lapGate)
            {
                return _laps.Count;
            }
        }
    }

    private void OnLapReviewed(LapReview review)
    {
        TrackSession(review);

        if (!IsConfigured || !_settings.LapDebriefEnabled)
        {
            return;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _debrief, source);

        // Die vorige Besprechung ist mit dem Rundenende hinfaellig: sie beschriebe
        // eine Runde, die der Fahrer gerade schon hinter sich gebracht hat.
        previous?.Cancel();
        previous?.Dispose();

        // Bewusst nicht abgewartet: der Aufruf kommt aus dem Telemetrie-Thread,
        // und der muss den naechsten Frame lesen, nicht auf das Netz warten.
        _ = DebriefAsync(review, source.Token);
    }

    /// <summary>Merkt sich die Runde und erkennt einen Streckenwechsel.</summary>
    private void TrackSession(LapReview review)
    {
        string key = review.Session.ReferenceKey;

        lock (_lapGate)
        {
            if (!string.Equals(key, _sessionKey, StringComparison.Ordinal))
            {
                _sessionKey = key;
                _laps.Clear();
                Phrases.ClearPrefix("corner:");
            }

            _laps.Add(review);

            if (_laps.Count > MaxSessionLaps)
            {
                _laps.RemoveAt(0);
            }
        }
    }

    private async Task DebriefAsync(LapReview review, CancellationToken cancellationToken)
    {
        try
        {
            GeminiResult result = await _client.GenerateAsync(
                Prompts.DebriefSystem,
                Prompts.DebriefPrompt(LapDigest.Describe(review)),
                Prompts.DebriefSchema(),
                maxOutputTokens: 1200,
                temperature: 0.35,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!result.Ok)
            {
                ReportProblem(result.Error);
                return;
            }

            ApplyDebrief(result.Text);
        }
        catch (OperationCanceledException)
        {
            // Runde ueberholt oder Coach beendet – beides kein Fehler.
        }
    }

    /// <summary>
    /// Verteilt die Antwort: Fließtext ins Overlay, Kurvenansagen in den Vorrat.
    /// </summary>
    internal void ApplyDebrief(string json)
    {
        JsonNode? root = TryParse(json);
        if (root is null)
        {
            ReportProblem("Rundenbesprechung war nicht lesbar.");
            return;
        }

        string debrief = (root["debrief"]?.GetValue<string>() ?? string.Empty).Trim();
        string spoken = (root["spoken"]?.GetValue<string>() ?? string.Empty).Trim();

        if (_settings.LivePhrasingEnabled)
        {
            ApplyCornerLines(root["cornerLines"] as JsonArray);
        }

        if (debrief.Length == 0)
        {
            return;
        }

        bool speak = _settings.SpeakDebrief && spoken.Length is > 0 and <= MaxSpokenDebriefLength;

        _engine?.PostExternal(
            CoachMessageKind.LapSummary,
            debrief,
            speak ? spoken : string.Empty,
            priority: 1);
    }

    private void ApplyCornerLines(JsonArray? lines)
    {
        // Erst raeumen: eine Kurve, die diese Runde sauber war, darf nicht mit
        // dem Tipp der vorletzten Runde beschallt werden.
        Phrases.ClearPrefix("corner:");

        if (lines is null)
        {
            return;
        }

        foreach (JsonNode? line in lines)
        {
            if (line?["corner"] is not { } cornerNode ||
                line["speech"]?.GetValue<string>() is not { } speech)
            {
                continue;
            }

            if (!TryReadInt(cornerNode, out int number))
            {
                continue;
            }

            Phrases.Set(PhraseKeys.Corner(number), [speech]);
        }
    }

    /// <summary>Holt einmalig drei Fassungen je Fahrfehler.</summary>
    private void RequestFaultVariants()
    {
        if (_faultsRequested)
        {
            return;
        }

        _faultsRequested = true;
        _ = FetchFaultVariantsAsync(_lifetime.Token);
    }

    private async Task FetchFaultVariantsAsync(CancellationToken cancellationToken)
    {
        try
        {
            GeminiResult result = await _client.GenerateAsync(
                Prompts.LiveSystem,
                Prompts.FaultPrompt(FaultMeanings),
                Prompts.FaultSchema(),
                maxOutputTokens: 900,
                temperature: 0.7,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Ok)
            {
                ApplyFaultVariants(result.Text);
            }

            // Ein Fehlschlag bleibt hier still: der Coach hat fuer jeden Fehler
            // einen festen Satz, es fehlt also nichts Wesentliches.
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal void ApplyFaultVariants(string json)
    {
        if (TryParse(json)?["faults"] is not JsonArray faults)
        {
            return;
        }

        foreach (JsonNode? entry in faults)
        {
            if (entry?["kind"]?.GetValue<string>() is not { } kindName ||
                !Enum.TryParse(kindName, ignoreCase: true, out FaultKind kind) ||
                entry["variants"] is not JsonArray variants)
            {
                continue;
            }

            string[] texts = variants
                .Select(v => v?.GetValue<string>() ?? string.Empty)
                .Where(v => v.Length > 0)
                .ToArray();

            Phrases.Set(PhraseKeys.Fault(kind), texts);
        }
    }

    /// <summary>
    /// Schreibt das Session-Fazit und legt es zusätzlich als Datei ab.
    /// </summary>
    /// <returns>Der Fazittext, oder ein Klartext-Grund, warum es keinen gibt.</returns>
    public async Task<string> SummariseSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return StatusLine;
        }

        if (!_settings.SessionSummaryEnabled)
        {
            return "Das Session-Fazit ist in ai.json abgeschaltet.";
        }

        LapReview[] laps;
        lock (_lapGate)
        {
            laps = [.. _laps];
        }

        if (laps.Length < 2)
        {
            return "Noch zu wenig Runden für ein Fazit – fahr mindestens zwei ausgewertete Runden.";
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);

        GeminiResult result = await _client.GenerateAsync(
            Prompts.SummarySystem,
            Prompts.SummaryPrompt(LapDigest.DescribeSession(laps)),
            responseSchema: null,
            maxOutputTokens: 1400,
            temperature: 0.4,
            cancellationToken: linked.Token).ConfigureAwait(false);

        if (!result.Ok)
        {
            return result.Error ?? "Das Fazit ließ sich nicht abrufen.";
        }

        string path = WriteReport(laps[^1].Session, result.Text, laps.Length);

        return path.Length > 0
            ? $"{result.Text}{Environment.NewLine}{Environment.NewLine}Abgelegt unter: {path}"
            : result.Text;
    }

    /// <summary>Beantwortet eine freie Frage mit dem aktuellen Stand als Grundlage.</summary>
    public async Task<string> AskAsync(
        string question,
        CoachState state,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return StatusLine;
        }

        if (!_settings.QuestionsEnabled)
        {
            return "Freie Fragen sind in ai.json abgeschaltet.";
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return string.Empty;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);

        GeminiResult result = await _client.GenerateAsync(
            Prompts.QuestionSystem,
            Prompts.QuestionPrompt(LapDigest.DescribeState(state), question.Trim()),
            responseSchema: null,
            maxOutputTokens: 900,
            temperature: 0.5,
            cancellationToken: linked.Token).ConfigureAwait(false);

        return result.Ok ? result.Text : result.Error ?? "Keine Antwort erhalten.";
    }

    /// <summary>
    /// Legt das Fazit als Textdatei ab, damit es den Neustart überlebt.
    /// </summary>
    /// <returns>Der Pfad, oder ein leerer String, wenn es nicht geklappt hat.</returns>
    private static string WriteReport(SessionInfo session, string text, int lapCount)
    {
        try
        {
            Directory.CreateDirectory(AiSettings.ReportDirectory);

            DateTime now = DateTime.Now;
            string name = $"{now:yyyy-MM-dd_HHmm}_{session.ReferenceKey}.md";
            string path = Path.Combine(AiSettings.ReportDirectory, name);

            string content = $"""
                # {session.TrackDisplayName} · {session.CarName}

                {now:dd.MM.yyyy HH:mm} · {lapCount} ausgewertete Runden

                {text}
                """;

            File.WriteAllText(path, content);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Ablegen ist Komfort – das Fazit steht ja bereits im Overlay.
            return string.Empty;
        }
    }

    /// <summary>
    /// Meldet ein Problem einmal und schweigt dann. Ein Netzfehler je Runde
    /// waere im Overlay genau die Ablenkung, die der Coach vermeiden soll.
    /// </summary>
    private void ReportProblem(string? problem)
    {
        if (_engine is null || string.IsNullOrWhiteSpace(problem))
        {
            return;
        }

        double now = _engine.State.Frame.Timestamp;
        if (now - _lastProblemAt < ProblemRepeatSeconds)
        {
            return;
        }

        _lastProblemAt = now;
        _engine.PostExternal(CoachMessageKind.Info, $"KI: {problem}", string.Empty);
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Liest eine Zahl, auch wenn das Modell sie als Text geliefert hat – das
    /// Schema verlangt INTEGER, aber darauf allein sollte man sich nicht verlassen.
    /// </summary>
    private static bool TryReadInt(JsonNode node, out int value)
    {
        if (node.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue(out value))
        {
            return true;
        }

        if (node.GetValueKind() == JsonValueKind.String &&
            int.TryParse(node.GetValue<string>(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_engine is not null)
        {
            _engine.LapReviewed -= OnLapReviewed;
            _engine = null;
        }

        _lifetime.Cancel();
        Interlocked.Exchange(ref _debrief, null)?.Dispose();
        _lifetime.Dispose();

        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
