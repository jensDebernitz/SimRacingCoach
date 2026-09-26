using DrivingCoach.Coaching.Model;
using DrivingCoach.Telemetry;

namespace DrivingCoach.Coaching;

/// <summary>
/// Zustand des Coaches, wie ihn das Overlay anzeigt. Wird vom UI-Thread
/// abgefragt, nicht per Ereignis geliefert – bei 120 Hz wäre ein Ereignis je
/// Frame nur unnötige Last auf dem Dispatcher.
/// </summary>
public sealed class CoachState
{
    public SessionInfo Session { get; internal set; } = SessionInfo.Unknown;

    public TelemetryFrame Frame { get; internal set; }

    public DeltaState Delta { get; internal set; } = DeltaState.None;

    public RecordedLap? Reference { get; internal set; }

    public IReadOnlyList<Corner> Corners { get; internal set; } = [];

    /// <summary>Auswertung der zuletzt beendeten Runde.</summary>
    public LapAnalysis? LastAnalysis { get; internal set; }

    /// <summary>Die als Nächstes kommende Kurve, falls in Reichweite.</summary>
    public Corner? UpcomingCorner { get; internal set; }

    /// <summary>Anzahl gültig aufgezeichneter Runden in dieser Sitzung.</summary>
    public int ValidLapCount { get; internal set; }

    /// <summary>
    /// Was über den Verlauf der Strecke bekannt ist. Bleibt <c>null</c>, bis
    /// die erste Runde eingeflossen ist.
    /// </summary>
    public TrackMap? TrackMap { get; internal set; }

    /// <summary>
    /// Die berechnete Ideallinie. Steht erst zur Verfügung, wenn die
    /// Streckenkarte genug Runden gesehen hat.
    /// </summary>
    /// <remarks>
    /// Wird nebenher berechnet und dann in einem Rutsch ausgetauscht. Wer sie
    /// liest, hält sie deshalb einmal in einer lokalen Variablen fest, statt
    /// die Eigenschaft mehrfach abzufragen.
    /// </remarks>
    public IdealLine? IdealLine { get; internal set; }

    /// <summary>
    /// Wie der Gierwinkel des Spiels zu lesen ist. Bis das aus einer Runde
    /// hervorgeht, steht hier die Annahme.
    /// </summary>
    public PoseConvention Pose { get; internal set; } = PoseConvention.Assumed;

    /// <summary>True, sobald <see cref="Pose"/> aus Daten stammt und nicht geraten ist.</summary>
    public bool IsPoseLearned { get; internal set; }

    public TelemetryStatus Status { get; internal set; } = new(false, "Startet …");
}

/// <summary>
/// Eine ausgewertete Runde, so wie sie nach außen gemeldet wird.
/// </summary>
/// <param name="Session">Strecke und Auto zum Zeitpunkt der Runde.</param>
/// <param name="Analysis">Die Kurve-für-Kurve-Auswertung.</param>
/// <param name="LapNumber">Wievielte gültige Runde dieser Sitzung.</param>
/// <param name="IsNewBest">True, wenn die Runde die neue Referenz wurde.</param>
public sealed record LapReview(SessionInfo Session, LapAnalysis Analysis, int LapNumber, bool IsNewBest);

/// <summary>
/// Führt Aufzeichnung, Delta, Kurvenanalyse und Fehlererkennung zusammen und
/// erzeugt daraus die Meldungen des Coaches.
/// </summary>
public sealed class CoachEngine : IDisposable
{
    private readonly ITelemetrySource _source;
    private readonly LapStore _store;
    private readonly TrackMapStore _maps;
    private readonly CoachOptions _options;
    private readonly LapRecorder _recorder;
    private readonly DeltaEngine _delta = new();
    private readonly CornerDetector _cornerDetector = new();
    private readonly LapAnalyzer _analyzer = new();
    private readonly FaultDetector _faults = new();
    private readonly SpeechScheduler _speech;
    private readonly HashSet<int> _cuedCorners = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Schützt allein den <see cref="SpeechScheduler"/>. Eigene Sperre, weil
    /// <see cref="PostExternal"/> aus einem fremden Thread kommt und
    /// <see cref="_gate"/> währenddessen von der Telemetrieschleife gehalten
    /// werden kann – die Ansage soll nicht auf den nächsten Frame warten.
    /// </summary>
    private readonly Lock _speechGate = new();

    private readonly IPhraseSource? _phrases;

    private int _lastCueLap = -1;

    /// <summary>Läuft, solange die Ideallinie im Hintergrund gerechnet wird.</summary>
    private Task? _idealLineWork;

    public CoachEngine(
        ITelemetrySource source,
        LapStore? store = null,
        CoachOptions? options = null,
        IPhraseSource? phrases = null,
        TrackMapStore? maps = null)
    {
        _source = source;
        _store = store ?? new LapStore();
        _maps = maps ?? new TrackMapStore();
        _options = options ?? new CoachOptions();
        _phrases = phrases;
        _recorder = new LapRecorder();
        _speech = new SpeechScheduler(_options);

        _recorder.LapFinished += OnLapFinished;
        _faults.FaultDetected += OnFaultDetected;

        _source.FrameReceived += OnFrameReceived;
        _source.SessionChanged += OnSessionChanged;
        _source.StatusChanged += OnStatusChanged;
    }

    /// <summary>Aktueller Zustand. Wird vom Telemetrie-Thread fortgeschrieben.</summary>
    public CoachState State { get; } = new();

    /// <summary>Wird für jede Meldung ausgelöst (Telemetrie-Thread!).</summary>
    public event Action<CoachMessage>? MessageRaised;

    /// <summary>Wird ausgelöst, wenn eine Meldung gesprochen werden soll (Telemetrie-Thread!).</summary>
    public event Action<string>? SpeechRequested;

    /// <summary>
    /// Wird nach jeder ausgewerteten Runde ausgelöst (Telemetrie-Thread!).
    /// Wer hier etwas Langsames tun will – etwa eine KI fragen –, muss das auf
    /// einen anderen Thread verlagern, sonst stockt die Telemetrie.
    /// </summary>
    public event Action<LapReview>? LapReviewed;

    public void Start() => _source.Start();

    public void Stop() => _source.Stop();

    /// <summary>Verwirft die Referenzrunde der aktuellen Kombination.</summary>
    public void ResetReference()
    {
        lock (_gate)
        {
            _store.Delete(State.Session.ReferenceKey);
            _delta.SetReference(null);
            State.Reference = null;
            State.Corners = [];
            State.LastAnalysis = null;
            State.ValidLapCount = 0;
        }

        Raise(CoachMessageKind.Info, "Referenzrunde gelöscht – die nächste gültige Runde wird die neue Referenz.", string.Empty);
    }

    private void OnSessionChanged(SessionInfo session)
    {
        lock (_gate)
        {
            State.Session = session;
            State.LastAnalysis = null;
            State.UpcomingCorner = null;
            State.ValidLapCount = 0;
            State.IdealLine = null;

            _recorder.ResetSession(session);
            _faults.Reset();
            _cuedCorners.Clear();

            // Sperrreihenfolge überall gleich: erst _gate, dann _speechGate.
            lock (_speechGate)
            {
                _speech.Reset();
            }

            if (!session.IsUsable)
            {
                _delta.SetReference(null);
                State.Reference = null;
                State.Corners = [];
                State.TrackMap = null;
                return;
            }

            // Die Streckenkarte hängt nicht am Auto: was in einer anderen
            // Klasse über den Asphalt gelernt wurde, gilt hier weiter.
            State.TrackMap = _maps.Load(session.TrackKey);

            RecordedLap? reference = _store.Load(session.ReferenceKey);
            ApplyReference(reference);

            if (reference is not null)
            {
                LearnPoseConvention(reference);
            }

            // Wurde die Strecke schon einmal gelernt, steht die Linie ohne
            // eine einzige Runde bereit.
            if (State.TrackMap is { } stored)
            {
                UpdateIdealLine(stored, reference);
            }
        }

        if (State.Reference is { } loaded)
        {
            Raise(
                CoachMessageKind.Info,
                $"{State.Session.TrackDisplayName}: Referenzrunde {FormatLapTime(loaded.LapTime)} geladen ({State.Corners.Count} Kurven).",
                string.Empty);
        }
        else
        {
            Raise(
                CoachMessageKind.Info,
                $"{session.TrackDisplayName}: keine Referenz vorhanden. Fahr eine saubere Runde – sie wird zur Referenz.",
                "Fahr eine saubere Runde. Sie wird deine Referenz.");
        }
    }

    private void OnStatusChanged(TelemetryStatus status) => State.Status = status;

    private void OnFrameReceived(TelemetrySnapshot snapshot)
    {
        TelemetryFrame frame = snapshot.Frame;

        lock (_gate)
        {
            State.Frame = frame;
            State.Delta = _delta.Update(in frame);

            _recorder.Accept(in frame);

            if (_options.FaultWarningsEnabled)
            {
                _faults.Accept(in frame);
            }

            UpdateUpcomingCorner(in frame);
        }
    }

    /// <summary>
    /// Sucht die nächste Kurve in Fahrtrichtung und gibt rechtzeitig vor dem
    /// Bremspunkt den Tipp aus der letzten Runde aus.
    /// </summary>
    private void UpdateUpcomingCorner(in TelemetryFrame frame)
    {
        IReadOnlyList<Corner> corners = State.Corners;
        if (corners.Count == 0 || State.Reference is null || !frame.IsDriving)
        {
            State.UpcomingCorner = null;
            return;
        }

        if (frame.LapsCompleted != _lastCueLap)
        {
            _lastCueLap = frame.LapsCompleted;
            _cuedCorners.Clear();
        }

        float binSize = State.Reference.BinSize;
        float lookahead = MathF.Max(frame.Speed, 10f) * (float)_options.CornerCueLeadSeconds;

        Corner? next = null;
        float bestDistance = float.MaxValue;

        foreach (Corner corner in corners)
        {
            float brakeDistance = corner.BrakingStartBin * binSize;
            float ahead = brakeDistance - frame.LapDistance;
            if (ahead >= 0f && ahead < bestDistance)
            {
                bestDistance = ahead;
                next = corner;
            }
        }

        State.UpcomingCorner = next;

        if (next is null || bestDistance > lookahead || !_cuedCorners.Add(next.Number))
        {
            return;
        }

        CornerAdvice? advice = State.LastAnalysis?.Corners
            .FirstOrDefault(c => c.Corner.Number == next.Number);

        if (advice is null || !advice.IsActionable || advice.TimeLost < _options.CornerCueMinTimeLoss)
        {
            return;
        }

        // Der angezeigte Text behält die gerechneten Zahlen; nur die Ansage darf
        // umformuliert sein. Gesagt wird bevorzugt der Technikbefund – "du
        // lenkst zu früh ein" kann der Fahrer sofort umsetzen, eine
        // Zehntelangabe nicht.
        string speech = Rephrase(PhraseKeys.Corner(next.Number), advice.BestSpeechText);

        string text = advice.Technique.HasFinding
            ? $"{advice.Text} · {advice.Technique.Text}"
            : advice.Text;

        Raise(CoachMessageKind.CornerTip, text, speech, priority: 2);
    }

    /// <summary>
    /// Holt eine vorformulierte Fassung, falls eine bereitliegt. Der Aufruf ist
    /// ein reiner Nachschlag ohne Wartezeit – siehe <see cref="IPhraseSource"/>.
    /// </summary>
    private string Rephrase(string key, string fallback)
    {
        if (_phrases is null || string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return _phrases.TryGet(key, out string? phrase) && !string.IsNullOrWhiteSpace(phrase)
            ? phrase
            : fallback;
    }

    private void OnLapFinished(LapCompleted completed)
    {
        RecordedLap lap = completed.Lap;

        if (!completed.IsValid)
        {
            Raise(
                CoachMessageKind.Info,
                $"Runde verworfen: {completed.InvalidReason}",
                string.Empty,
                priority: 0);
            return;
        }

        RecordedLap? reference;
        LapAnalysis? analysis = null;
        bool isNewBest;
        TrackMap? learned = null;

        lock (_gate)
        {
            State.ValidLapCount++;
            reference = State.Reference;

            LearnPoseConvention(lap);
            learned = LearnTrackGeometry(lap);

            if (reference is not null && State.Corners.Count > 0)
            {
                analysis = _analyzer.Analyze(lap, reference, State.Corners);
                State.LastAnalysis = analysis;
            }

            isNewBest = _store.TrySaveIfFaster(lap, reference, out RecordedLap best);
            if (isNewBest)
            {
                ApplyReference(best);
            }
        }

        if (learned is not null)
        {
            // Außerhalb der Sperre: die Karte ist ein paar hundert Kilobyte,
            // und solange geschrieben wird, soll die Telemetrie weiterlaufen.
            _maps.Save(learned);
            UpdateIdealLine(learned, reference);
        }

        if (isNewBest)
        {
            string improvement = reference is null
                ? $"Erste Referenzrunde gesetzt: {FormatLapTime(lap.LapTime)}."
                : $"Neue Bestzeit: {FormatLapTime(lap.LapTime)} ({lap.LapTime - reference.LapTime:0.00} s).";

            Raise(CoachMessageKind.Achievement, improvement, "Neue Bestzeit.", priority: 3);
        }

        if (analysis is null)
        {
            return;
        }

        ReportLapSummary(analysis);

        LapReviewed?.Invoke(new LapReview(State.Session, analysis, State.ValidLapCount, isNewBest));
    }

    /// <summary>
    /// Leitet aus einer Runde ab, wie der Gierwinkel des Spiels gemeint ist.
    /// </summary>
    /// <remarks>
    /// Einmal je Sitzung genügt: Die Konvention hängt am Spiel, nicht an Strecke
    /// oder Auto. Wo sie sich nicht sauber ableiten lässt – etwa auf einem Kurs
    /// ohne brauchbare Gerade –, bleibt die Annahme stehen, und die
    /// perspektivische Linie wird eben nicht angeboten.
    /// </remarks>
    private void LearnPoseConvention(RecordedLap lap)
    {
        if (State.IsPoseLearned || PoseConvention.Learn(lap) is not { } convention)
        {
            return;
        }

        State.Pose = convention;
        State.IsPoseLearned = true;
    }

    /// <summary>
    /// Arbeitet die Runde in die Streckenkarte ein.
    /// </summary>
    /// <returns>Die geänderte Karte, wenn sie gespeichert werden muss, sonst <c>null</c>.</returns>
    /// <remarks>
    /// Gelernt wird nur aus gültigen Runden. Eine Boxenrunde oder eine mit
    /// Zurücksetzen würde die Mittellinie quer über die Wiese ziehen, und der
    /// Fehler bliebe dauerhaft in der Datei stehen.
    /// </remarks>
    private TrackMap? LearnTrackGeometry(RecordedLap lap)
    {
        if (!lap.Channels.HasGeometry)
        {
            return null;
        }

        TrackMap map = State.TrackMap ?? TrackMap.Empty(
            State.Session.TrackKey,
            State.Session.TrackDisplayName,
            State.Session.TrackLength,
            lap.BinSize,
            lap.Channels.Count);

        if (!map.Learn(lap))
        {
            // Passt nicht zur Karte – etwa weil AMS2 dieselbe Strecke in einer
            // anderen Länge meldet. Dann lieber nichts lernen als Unsinn.
            return null;
        }

        State.TrackMap = map;
        return map;
    }

    /// <summary>
    /// Stößt die Neuberechnung der Ideallinie an.
    /// </summary>
    /// <remarks>
    /// Nicht im Telemetriethread: Der Löser braucht einige Dutzend
    /// Millisekunden, und die würden als Ruckler im Delta landen. Läuft noch
    /// eine Berechnung, wird diese Runde übersprungen – die nächste bringt die
    /// Karte ohnehin weiter.
    /// </remarks>
    private void UpdateIdealLine(TrackMap map, RecordedLap? reference)
    {
        if (!map.IsUsable || reference is null || _idealLineWork is { IsCompleted: false })
        {
            return;
        }

        // Was das Auto kann, steht nirgends – es lässt sich nur daran ablesen,
        // was in der besten Runde schon erreicht wurde.
        GripEstimate grip = GripEstimate.FromLap(reference);
        float topSpeed = MathF.Max(reference.Channels.Speed.Max(), 10f);
        TrackMap snapshot = map.Snapshot();

        _idealLineWork = Task.Run(() =>
        {
            try
            {
                IdealLine? line = IdealLineSolver.Solve(snapshot, grip, topSpeed);
                if (line is not null)
                {
                    State.IdealLine = line;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Eine Ideallinie ist ein Zusatz. Scheitert sie, fährt der
                // Coach ohne sie weiter, statt die Anwendung mitzunehmen.
                Raise(CoachMessageKind.Info, $"Ideallinie konnte nicht berechnet werden: {ex.Message}", string.Empty);
            }
        });
    }

    private void ReportLapSummary(LapAnalysis analysis)
    {
        Raise(
            CoachMessageKind.LapSummary,
            $"Runde {FormatLapTime(analysis.LapTime)} · {analysis.LapDelta:+0.00;-0.00;0.00} s zur Referenz",
            string.Empty,
            priority: 1);

        CornerAdvice[] worst = analysis.WorstFirst.Take(_options.LapSummaryCornerCount).ToArray();
        if (worst.Length == 0)
        {
            Raise(CoachMessageKind.LapSummary, "Sauber gefahren – keine auffällige Kurve.", "Saubere Runde.", priority: 1);
            ReportTechnique(analysis, []);
            return;
        }

        foreach (CornerAdvice advice in worst)
        {
            Raise(CoachMessageKind.LapSummary, advice.Text, string.Empty, priority: 1);
        }

        ReportTechnique(analysis, worst);

        // Gesprochen wird nur die teuerste Kurve – alles andere überfordert
        // zwischen zwei Runden.
        CornerAdvice biggest = worst[0];
        Raise(
            CoachMessageKind.LapSummary,
            $"Größter Verlust: {biggest.Corner.Name} mit {biggest.TimeLost:0.00} s",
            biggest.BestSpeechText,
            priority: 2);
    }

    /// <summary>
    /// Meldet, <em>wie</em> gefahren wurde: die auffälligsten Kurven und die
    /// teuerste Kurvenkombination.
    /// </summary>
    /// <remarks>
    /// Die Technikbefunde stehen bewusst getrennt von den Zeitverlusten. Eine
    /// Kurve kann auf die Hundertstel genau passen und trotzdem falsch gefahren
    /// sein – auf der nächsten Strecke mit derselben Kurvenart fällt genau das
    /// dann auf die Füße.
    /// </remarks>
    private void ReportTechnique(LapAnalysis analysis, IReadOnlyList<CornerAdvice> alreadyListed)
    {
        CornerAdvice[] findings = analysis.TechniqueFindings
            .Take(_options.LapSummaryCornerCount)
            .ToArray();

        foreach (CornerAdvice advice in findings)
        {
            Raise(
                CoachMessageKind.LapSummary,
                $"{advice.Corner.Name}: {advice.Technique.Text}",
                string.Empty,
                priority: 1);
        }

        SequenceAdvice? sequence = analysis.WorstSequencesFirst.FirstOrDefault();
        if (sequence is null)
        {
            return;
        }

        // Die Kombination bekommt nur dann eine Ansage, wenn ihr Befund über
        // die Einzelkurven hinausgeht. Ist die teuerste Kurve ohnehin schon
        // genannt, wäre die zweite Stimme dazu nur Wiederholung.
        bool covered = alreadyListed.Count > 0 &&
                       sequence.Sequence.Corners.Any(c => c.Number == alreadyListed[0].Corner.Number) &&
                       sequence.Issue != SequenceIssue.ExitCompromised;

        Raise(
            CoachMessageKind.LapSummary,
            sequence.Text,
            covered ? string.Empty : sequence.Speech,
            priority: 1);
    }

    private void OnFaultDetected(DrivingFault fault) =>
        Raise(
            CoachMessageKind.Fault,
            fault.Text,
            Rephrase(PhraseKeys.Fault(fault.Kind), fault.SpeechText),
            priority: 4,
            at: fault.At);

    /// <summary>
    /// Schleust eine von außen erzeugte Meldung ein – etwa die Rundenbesprechung
    /// der KI, die erst Sekunden nach der Runde eintrifft.
    /// </summary>
    /// <remarks>
    /// Läuft absichtlich durch denselben <see cref="SpeechScheduler"/> wie alles
    /// andere: eine KI-Besprechung darf eine Fehlerwarnung nicht übertönen und
    /// nicht mitten im Anbremsen loslegen.
    /// </remarks>
    public void PostExternal(CoachMessageKind kind, string text, string speechText, int priority = 1)
    {
        TelemetryFrame frame;

        // Der Frame wird vom Telemetrie-Thread geschrieben und ist zu groß für
        // einen atomaren Lesezugriff. Hier – anders als in der Anzeige – wird
        // er ausgewertet, also muss er unzerrissen sein.
        lock (_gate)
        {
            frame = State.Frame;
        }

        Raise(in frame, kind, text, speechText, priority, at: frame.Timestamp);
    }

    /// <summary>Setzt eine neue Referenzrunde und erkennt deren Kurven neu.</summary>
    private void ApplyReference(RecordedLap? reference)
    {
        State.Reference = reference;
        _delta.SetReference(reference);
        State.Corners = reference is null ? [] : _cornerDetector.Detect(reference);
    }

    private void Raise(
        CoachMessageKind kind,
        string text,
        string speechText,
        int priority = 1,
        double? at = null)
    {
        TelemetryFrame frame = State.Frame;
        Raise(in frame, kind, text, speechText, priority, at);
    }

    private void Raise(
        in TelemetryFrame frame,
        CoachMessageKind kind,
        string text,
        string speechText,
        int priority,
        double? at)
    {
        var message = new CoachMessage(kind, text, speechText, priority, at ?? frame.Timestamp);

        MessageRaised?.Invoke(message);

        bool speak;
        lock (_speechGate)
        {
            speak = _speech.ShouldSpeak(message, in frame);
        }

        if (speak)
        {
            SpeechRequested?.Invoke(message.SpeechText);
        }
    }

    /// <summary>Formatiert Sekunden als "1:23,456".</summary>
    public static string FormatLapTime(float seconds)
    {
        if (seconds <= 0f || float.IsNaN(seconds))
        {
            return "--:--,---";
        }

        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalMinutes}:{span.Seconds:00},{span.Milliseconds:000}";
    }

    public void Dispose()
    {
        _source.FrameReceived -= OnFrameReceived;
        _source.SessionChanged -= OnSessionChanged;
        _source.StatusChanged -= OnStatusChanged;
        _recorder.LapFinished -= OnLapFinished;
        _faults.FaultDetected -= OnFaultDetected;
        _source.Dispose();
    }
}
